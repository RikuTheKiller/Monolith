using Content.Shared._Mono.Symbiote.Components;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Traits.Assorted;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Mono.Symbiote;

/// <summary>
/// Heals the host while Chemical Pump is active, and revives them if it can.
/// Predicted, apart from restoring blood and whatever the server does about the pump's health.
/// </summary>
public abstract class SharedSymbioteChemicalPumpSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private SharedRottingSystem _rotting = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SymbioteChemicalPumpComponent, SymbioteAbilityToggledEvent>(OnToggled);
        SubscribeLocalEvent<SymbioteChemicalPumpComponent, SymbioteAbilityUpdateEvent>(OnUpdate);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Retracted pumps go away
        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<SymbioteChemicalPumpHostComponent>();
        while (query.MoveNext(out var uid, out var pump))
        {
            if (pump.RetractEnd is { } end && curTime >= end)
                RemCompDeferred<SymbioteChemicalPumpHostComponent>(uid);
        }
    }

    private void OnToggled(Entity<SymbioteChemicalPumpComponent> ent, ref SymbioteAbilityToggledEvent args)
    {
        if (!args.Active)
        {
            Retract(ent, args.Host);
            return;
        }

        // A fresh pump every time
        ent.Comp.Health = ent.Comp.MaxHealth;

        // One that's still retracting gets replaced, so the new one emerges from scratch
        RemComp<SymbioteChemicalPumpHostComponent>(args.Host);

        // Set before it's added, so the client already knows what to draw when the component starts up
        AddComp(args.Host, new SymbioteChemicalPumpHostComponent
        {
            Sprite = ent.Comp.Sprite,
            EmergeState = ent.Comp.EmergeState,
            StartTime = _timing.CurTime,
            RetractState = ent.Comp.RetractState,
        });
    }

    /// <summary>
    /// Starts the pump retracting into the host's chest, after which it goes away.
    /// </summary>
    private void Retract(Entity<SymbioteChemicalPumpComponent> ent, EntityUid host)
    {
        if (!TryComp<SymbioteChemicalPumpHostComponent>(host, out var pump) || pump.RetractEnd != null)
            return;

        if (pump.RetractState == null)
        {
            RemComp(host, pump);
            return;
        }

        pump.RetractEnd = _timing.CurTime + ent.Comp.RetractDuration;
        Dirty(host, pump);
        OnRetracting((host, pump));
    }

    /// <summary>
    /// Called when the pump starts retracting, since changes the client predicts itself don't come with a new state.
    /// </summary>
    protected virtual void OnRetracting(Entity<SymbioteChemicalPumpHostComponent> ent)
    {
    }

    private void OnUpdate(Entity<SymbioteChemicalPumpComponent> ent, ref SymbioteAbilityUpdateEvent args)
    {
        ent.Comp.Health = MathF.Min(ent.Comp.Health + ent.Comp.HealthRegen * args.Seconds, ent.Comp.MaxHealth);

        Heal(ent, args.Host, ent.Comp.Healing * args.Seconds);
        TryRevive(args.Host);
    }

    /// <summary>
    /// Spends the healing on the most effective tier first, spreading it evenly across everything in it,
    /// and moves on to the next tier with whatever's left once everything in the tier is healed.
    /// </summary>
    private void Heal(Entity<SymbioteChemicalPumpComponent> ent, EntityUid host, float healing)
    {
        if (!TryComp<DamageableComponent>(host, out var damageable))
            return;

        var heal = new DamageSpecifier();
        var bloodHeal = 0f;
        var targets = new List<(ProtoId<DamageTypePrototype>? Type, float Damage)>();

        foreach (var tier in ent.Comp.Tiers)
        {
            if (healing <= 0f)
                break;

            targets.Clear();
            foreach (var type in tier.Types)
            {
                if (damageable.Damage.DamageDict.TryGetValue(type, out var damage) && damage > 0)
                    targets.Add((type, damage.Float()));
            }

            if (tier.Blood && GetMissingBlood(ent, host) is var missing && missing > 0f)
                targets.Add((null, missing));

            // Everything gets an equal share. Going from least to most damaged means anything that needs less than its share
            // passes the rest on to everything after it, instead of it going to waste.
            targets.Sort((a, b) => a.Damage.CompareTo(b.Damage));

            for (var i = 0; i < targets.Count; i++)
            {
                var (type, damage) = targets[i];
                var share = healing / (targets.Count - i);
                var healed = MathF.Min(damage, share * tier.Effectiveness);
                healing -= healed / tier.Effectiveness;

                if (type is { } damageType)
                    heal.DamageDict[damageType] = -healed;
                else
                    bloodHeal = healed;
            }
        }

        if (!heal.Empty)
            _damageable.TryChangeDamage(host, heal, ignoreResistances: true, interruptsDoAfters: false, damageable: damageable);

        if (bloodHeal > 0f)
            RestoreBlood(host, bloodHeal);
    }

    /// <summary>
    /// How much of the host's blood is missing, as a percentage of its maximum blood volume.
    /// Read straight from their blood solution, which the client can see too.
    /// </summary>
    private float GetMissingBlood(Entity<SymbioteChemicalPumpComponent> ent, EntityUid host)
    {
        if (!_solution.TryGetSolution(host, ent.Comp.BloodSolution, out _, out var blood))
            return 0f;

        return (1f - blood.FillFraction) * 100f;
    }

    /// <summary>
    /// Restores a percentage of the host's maximum blood volume.
    /// Only the server can, since it's the one that knows what the host's blood is made of.
    /// </summary>
    /// <param name="host">The host to restore blood to.</param>
    /// <param name="percentage">How much to restore, as a percentage of their maximum blood volume.</param>
    protected virtual void RestoreBlood(EntityUid host, float percentage)
    {
    }

    /// <summary>
    /// Brings a dead host back once they're healed above the crit threshold, unless they can't be revived, like with a defibrillator.
    /// </summary>
    private void TryRevive(EntityUid host)
    {
        if (!_mobState.IsDead(host)
            || _rotting.IsRotten(host)
            || HasComp<UnrevivableComponent>(host)
            || !_mobThreshold.TryGetThresholdForState(host, MobState.Critical, out var critThreshold)
            || !TryComp<DamageableComponent>(host, out var damageable)
            || damageable.TotalDamage >= critThreshold)
            return;

        _mobState.ChangeMobState(host, MobState.Alive);
        OnRevived(host);
    }

    /// <summary>
    /// Called after the pump brings the host back from the dead.
    /// </summary>
    protected virtual void OnRevived(EntityUid host)
    {
    }
}
