using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Server.EUI;
using Content.Server.Ghost;
using Content.Shared._Mono.Symbiote;
using Content.Shared._Mono.Symbiote.Components;
using Content.Shared.Actions;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mind;
using Robust.Server.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._Mono.Symbiote;

/// <summary>
/// Restores blood and brings ghosts back for the Chemical Pump, and wears the pump down as the host gets hit with physical damage.
/// The pump's health is server-only, since the client never predicts the damage that wears it down.
/// </summary>
public sealed class SymbioteChemicalPumpSystem : SharedSymbioteChemicalPumpSystem
{
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private BloodstreamSystem _bloodstream = default!;
    [Dependency] private EuiManager _eui = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedMindSystem _mind = default!;
    [Dependency] private SharedSymbioteSystem _symbiote = default!;

    public override void Initialize()
    {
        base.Initialize();

        // After the body has decided whether the hit was evaded
        SubscribeLocalEvent<SymbioteHostComponent, TryChangePartDamageEvent>(OnHostTryChangePartDamage, after: new[] { typeof(SharedBodySystem) });
    }

    protected override void RestoreBlood(EntityUid host, float percentage)
    {
        if (TryComp<BloodstreamComponent>(host, out var bloodstream))
            _bloodstream.TryModifyBloodLevel(host, bloodstream.BloodMaxVolume * percentage / 100f, bloodstream);
    }

    protected override void OnRevived(EntityUid host)
    {
        // Let a ghosted host know they can come back, like a defibrillator does
        if (_mind.TryGetMind(host, out _, out var mind)
            && mind.CurrentEntity != host
            && _player.TryGetSessionById(mind.UserId, out var session))
        {
            _eui.OpenEui(new ReturnToBodyEui(mind, _mind, _player), session);
        }
    }

    private void OnHostTryChangePartDamage(Entity<SymbioteHostComponent> ent, ref TryChangePartDamageEvent args)
    {
        if (args.Evaded || args.Cancelled || ent.Comp.Symbiote is not { } symbiote)
            return;

        foreach (var (actionUid, _) in _actions.GetActions(symbiote))
        {
            if (!TryComp<SymbioteChemicalPumpComponent>(actionUid, out var pump)
                || !TryComp<SymbioteAbilityComponent>(actionUid, out var ability)
                || !_symbiote.IsAbilityActive((actionUid, ability)))
                continue;

            // The pump sits on top of the host's chest, outside their armor, so it takes the hit before any resistances do
            pump.Health -= GetPhysicalDamage(pump, args.Damage);
            if (pump.Health > 0f)
                continue;

            if (TryComp<SymbioteComponent>(symbiote, out var symbioteComp))
                _symbiote.DeactivateAbility((symbiote, symbioteComp), (actionUid, ability));
        }
    }

    private float GetPhysicalDamage(SymbioteChemicalPumpComponent pump, DamageSpecifier damage)
    {
        var physical = 0f;
        foreach (var groupId in pump.PhysicalGroups)
        {
            var group = _proto.Index(groupId);
            foreach (var type in group.DamageTypes)
            {
                if (damage.DamageDict.TryGetValue(type, out var value) && value > FixedPoint2.Zero)
                    physical += value.Float();
            }
        }

        return physical;
    }
}
