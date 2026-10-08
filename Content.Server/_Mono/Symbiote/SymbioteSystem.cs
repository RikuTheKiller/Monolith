using System.Linq;
using Content.Server._Mono.Radio;
using Content.Server._NF.Salvage;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Components;
using Content.Server.Chat.Systems;
using Content.Server.Medical;
using Content.Server.Nutrition.EntitySystems;
using Content.Server.Radio.EntitySystems;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared.Chat;
using Content.Shared._Mono.Symbiote;
using Content.Shared._Mono.Symbiote.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Polymorph;
using Content.Shared.Temperature;

namespace Content.Server._Mono.Symbiote;

public sealed partial class SymbioteSystem : SharedSymbioteSystem
{
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private FlammableSystem _flammable = default!;
    [Dependency] private FoodSystem _food = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private RadioSystem _radio = default!;
    [Dependency] private TemperatureSystem _temperature = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private VomitSystem _vomit = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SymbioteComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<SymbioteComponent, CheckTargetedSpeechEvent>(OnCheckTargetedSpeech);
        SubscribeLocalEvent<SymbioteComponent, ModifyChangedTemperatureEvent>(OnTemperatureChange);
        SubscribeLocalEvent<SymbioteComponent, TryIgniteEvent>(OnIgniteAttempt);

        SubscribeLocalEvent<SymbioteHostComponent, BodyPartRemovedEvent>(OnHostBodyPartRemoved);
        SubscribeLocalEvent<SymbioteHostComponent, BeingGibbedEvent>(OnHostGibbed);
        SubscribeLocalEvent<SymbioteHostComponent, PolymorphedEvent>(OnHostPolymorphed);
        SubscribeLocalEvent<SymbioteHostComponent, OnTemperatureChangeEvent>(OnHostTemperatureChange);
        SubscribeLocalEvent<SymbioteHostComponent, RadioHeardEvent>(OnHostRadioHeard);
    }

    #region Bonding

    protected override LocId? GetBodyHostProblem(EntityUid target)
    {
        if (HasComp<NFSalvageMobRestrictionsComponent>(target))
            return "symbiote-host-problem-expedition";

        if (!HasComp<BloodstreamComponent>(target))
            return "symbiote-host-problem-no-bloodstream";

        if (!_body.GetBodyChildrenOfType(target, BodyPartType.Head).Any())
            return "symbiote-host-problem-no-head";

        return null;
    }

    protected override bool IsMouthBlocked(EntityUid target, EntityUid popupUser)
    {
        return _food.IsMouthBlocked(target, popupUser);
    }

    protected override void OnBonded(Entity<SymbioteComponent> ent, EntityUid host)
    {
        _flammable.Extinguish(ent);
        SyncTemperature(ent, host);
    }

    #endregion

    #region Host

    private void OnHostBodyPartRemoved(Entity<SymbioteHostComponent> ent, ref BodyPartRemovedEvent args)
    {
        // Like decapitation
        KickOutIfInvalid(ent);
    }

    /// <summary>
    /// Kicks the symbiote out if its host stopped being a valid host, like by losing their head.
    /// </summary>
    private void KickOutIfInvalid(Entity<SymbioteHostComponent> host)
    {
        if (!TryComp<SymbioteComponent>(host.Comp.Symbiote, out var symbiote))
            return;

        if (GetHostProblem(host, currentHost: true) != null)
            TryLeaveHost((host.Comp.Symbiote.Value, symbiote));
    }

    private void OnHostGibbed(Entity<SymbioteHostComponent> ent, ref BeingGibbedEvent args)
    {
        // Survives its host getting gibbed, but not getting deleted outright
        if (TryComp<SymbioteComponent>(ent.Comp.Symbiote, out var symbiote))
            TryLeaveHost((ent.Comp.Symbiote.Value, symbiote));
    }

    private void OnHostPolymorphed(Entity<SymbioteHostComponent> ent, ref PolymorphedEvent args)
    {
        if (!TryComp<SymbioteComponent>(ent.Comp.Symbiote, out var symbiote))
            return;

        var symbioteEnt = (ent.Comp.Symbiote.Value, symbiote);
        if (!TryLeaveHost(symbioteEnt))
            return;

        // Follow the host into their new body, or get kicked out if it can't host a symbiote
        if (IsValidHost(args.NewEntity))
            Bond(symbioteEnt, args.NewEntity, popup: false);
        else
            _transform.DropNextTo(ent.Comp.Symbiote.Value, args.NewEntity); // The old body is already in the paused map
    }

    private void OnHostRadioHeard(Entity<SymbioteHostComponent> ent, ref RadioHeardEvent args)
    {
        // Hears whatever reaches its host's ears, but understands it with its own brain
        if (ent.Comp.Symbiote is { } symbiote)
            _radio.HearRadio(symbiote, args.Radio, args.Message);
    }

    private void OnHostTemperatureChange(Entity<SymbioteHostComponent> ent, ref OnTemperatureChangeEvent args)
    {
        if (ent.Comp.Symbiote is { } symbiote)
            SyncTemperature(symbiote, ent);
    }

    /// <summary>
    /// Sets the symbiote's temperature to its host's.
    /// </summary>
    private void SyncTemperature(EntityUid symbiote, EntityUid host)
    {
        if (TryComp<TemperatureComponent>(host, out var hostTemp))
            _temperature.ForceChangeTemperature(symbiote, hostTemp.CurrentTemperature);
    }

    #endregion

    private void OnCheckTargetedSpeech(Entity<SymbioteComponent> ent, ref CheckTargetedSpeechEvent args)
    {
        // A bonded symbiote whispers to its host, and nobody else hears it. Emotes aren't speech.
        args.ChatTypeIgnore.Add(InGameICChatType.Emote);

        if (ent.Comp.Host is not { } host)
            return;

        args.Targets.Add(ent);
        args.Targets.Add(host);
    }

    private void OnMobStateChanged(Entity<SymbioteComponent> ent, ref MobStateChangedEvent args)
    {
        // A dead symbiote has no business staying in a host
        if (args.NewMobState == MobState.Dead && ent.Comp.Host is { } host && TryLeaveHost(ent))
            MakeHostVomit(host);
    }

    protected override void MakeHostVomit(EntityUid host)
    {
        // Corpses don't vomit
        if (_mobState.IsDead(host))
            return;

        // Pretty mild and mostly cosmetic
        _vomit.Vomit(host, -10, -10);
    }

    private void OnTemperatureChange(Entity<SymbioteComponent> ent, ref ModifyChangedTemperatureEvent args)
    {
        // Inside a host, the symbiote's temperature only follows the host's
        if (ent.Comp.Host != null)
            args.TemperatureDelta = 0;
    }

    private void OnIgniteAttempt(Entity<SymbioteComponent> ent, ref TryIgniteEvent args)
    {
        if (ent.Comp.Host != null)
            args.Cancelled = true;
    }
}
