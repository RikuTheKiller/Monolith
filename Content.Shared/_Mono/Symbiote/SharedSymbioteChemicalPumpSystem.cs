using Content.Shared._Mono.Symbiote.Components;
using Content.Shared.Actions;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Traits.Assorted;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Mono.Symbiote;

/// <summary>
/// Heals the host while Chemical Pump is active, revives them if it can, and wears the pump down as the host gets hit.
/// Predicted by anyone who has the state for it, apart from restoring blood and bringing back ghosts.
/// </summary>
public abstract class SharedSymbioteChemicalPumpSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MobThresholdSystem _mobThreshold = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedRottingSystem _rotting = default!;
    [Dependency] private SharedSolutionContainerSystem _solution = default!;
    [Dependency] private SharedSymbioteSystem _symbiote = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SymbioteChemicalPumpComponent, SymbioteAbilityToggledEvent>(OnToggled);
        SubscribeLocalEvent<SymbioteChemicalPumpComponent, SymbioteAbilityUpdateEvent>(OnUpdate);

        // After the body has decided whether the hit was evaded
        SubscribeLocalEvent<SymbioteHostComponent, TryChangePartDamageEvent>(OnHostTryChangePartDamage, after: new[] { typeof(SharedBodySystem) });
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Pumps that are done retracting or bursting go away
        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<SymbioteChemicalPumpHostComponent>();
        while (query.MoveNext(out var uid, out var pump))
        {
            if (pump.EndTime is { } end && curTime >= end)
                RemCompDeferred<SymbioteChemicalPumpHostComponent>(uid);
        }
    }

    private void OnToggled(Entity<SymbioteChemicalPumpComponent> ent, ref SymbioteAbilityToggledEvent args)
    {
        if (!args.Active)
        {
            // Retracts, unless it already started bursting
            End(args.Host, ent.Comp.RetractState, ent.Comp.RetractDuration, ent.Comp.RetractSound, args.User);
            return;
        }

        // A fresh pump every time
        SetHealth(ent, ent.Comp.MaxHealth);

        // One that's still going away gets replaced, so the new one emerges from scratch
        RemComp<SymbioteChemicalPumpHostComponent>(args.Host);

        // Set before it's added, so the client already knows what to draw when the component starts up
        AddComp(args.Host, new SymbioteChemicalPumpHostComponent
        {
            Sprite = ent.Comp.Sprite,
            EmergeState = ent.Comp.EmergeState,
            StartTime = _timing.CurTime,
            BeatSound = ent.Comp.BeatSound,
            BeatFrame = ent.Comp.BeatFrame,
        });

        PlaySound(ent.Comp.EmergeSound, args.Host, args.User);
    }

    /// <summary>
    /// Plays a sound once, since sounds can't be rolled back when prediction replays.
    /// If someone's input caused it, their client predicts it and the server plays it for everyone else.
    /// Otherwise every client might predict it at once, so only the server plays it.
    /// </summary>
    /// <param name="sound">The sound to play.</param>
    /// <param name="source">Where it plays from.</param>
    /// <param name="user">Whoever's input caused it, if anyone.</param>
    private void PlaySound(SoundSpecifier? sound, EntityUid source, EntityUid? user)
    {
        if (user != null)
            _audio.PlayPredicted(sound, source, user);
        else if (_net.IsServer)
            _audio.PlayPvs(sound, source);
    }

    /// <summary>
    /// How much health the pump has right now, after regenerating since it was last hit.
    /// </summary>
    public float GetHealth(SymbioteChemicalPumpComponent pump)
    {
        var regenerated = pump.HealthRegen * (float)(_timing.CurTime - pump.HealthTime).TotalSeconds;
        return MathF.Min(pump.Health + regenerated, pump.MaxHealth);
    }

    private void SetHealth(Entity<SymbioteChemicalPumpComponent> ent, float health)
    {
        ent.Comp.Health = health;
        ent.Comp.HealthTime = _timing.CurTime;
        Dirty(ent);
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
            var health = GetHealth(pump) - GetPhysicalDamage(pump, args.Damage);
            SetHealth((actionUid, pump), health);
            if (health > 0f)
                continue;

            Burst((actionUid, pump), ent);

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

    /// <summary>
    /// Tears the pump apart instead of letting it retract, for when it runs out of health.
    /// Has to happen before the ability ends, since ending it would start it retracting.
    /// </summary>
    private void Burst(Entity<SymbioteChemicalPumpComponent> ent, EntityUid host)
    {
        if (ent.Comp.BurstState is { } burst)
            End(host, burst, ent.Comp.BurstDuration, ent.Comp.BurstSound, null);
    }

    /// <summary>
    /// Starts the pump going away, playing a state once before it's removed.
    /// </summary>
    /// <param name="host">The host the pump is on.</param>
    /// <param name="state">The state to play as it goes away. Without one, it's removed right away.</param>
    /// <param name="duration">How long the state plays for.</param>
    /// <param name="sound">The sound of it going away.</param>
    /// <param name="user">Whoever's input made it go away, if anyone.</param>
    private void End(EntityUid host, string? state, TimeSpan duration, SoundSpecifier? sound, EntityUid? user)
    {
        if (!TryComp<SymbioteChemicalPumpHostComponent>(host, out var pump) || pump.EndTime != null)
            return;

        if (state == null)
        {
            RemComp(host, pump);
            return;
        }

        pump.EndState = state;
        pump.EndStart = _timing.CurTime;
        pump.EndTime = _timing.CurTime + duration;
        Dirty(host, pump);
        PlaySound(sound, host, user);
    }

    private void OnUpdate(Entity<SymbioteChemicalPumpComponent> ent, ref SymbioteAbilityUpdateEvent args)
    {
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
