using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._Mono.Nudge;

public sealed partial class NudgeSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NudgeComponent, UserActivateInWorldEvent>(OnActivate);
    }

    private void OnActivate(Entity<NudgeComponent> ent, ref UserActivateInWorldEvent args)
    {
        if (args.Handled || !HasComp<MobStateComponent>(args.Target))
            return;

        args.Handled = true;

        var curTime = _timing.CurTime;
        if (curTime < ent.Comp.NextNudge)
            return;

        ent.Comp.NextNudge = curTime + ent.Comp.Cooldown;
        Dirty(ent);

        var targetIdentity = Identity.Entity(args.Target, EntityManager);
        _popup.PopupPredicted(Loc.GetString("nudge-self", ("target", targetIdentity)),
            Loc.GetString("nudge-others", ("user", Identity.Entity(ent, EntityManager)), ("target", targetIdentity)),
            args.Target,
            ent);
        _audio.PlayPredicted(ent.Comp.Sound, args.Target, ent);
    }
}
