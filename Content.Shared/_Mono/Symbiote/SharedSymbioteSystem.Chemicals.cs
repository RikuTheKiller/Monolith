using Content.Shared._Mono.Symbiote.Components;

namespace Content.Shared._Mono.Symbiote;

public abstract partial class SharedSymbioteSystem
{
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<SymbioteComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            var ent = (uid, comp);

            if (curTime >= comp.NextChemicalUpdate)
            {
                comp.NextChemicalUpdate = curTime + comp.ChemicalUpdateInterval;
                Dirty(uid, comp);

                UpdateChemicals(ent);
            }

            ResolveChemicals(ent);
        }
    }

    /// <summary>
    /// Regenerates chemicals inside a living host, and drains them anywhere else.
    /// </summary>
    private void UpdateChemicals(Entity<SymbioteComponent> ent)
    {
        var seconds = (float)ent.Comp.ChemicalUpdateInterval.TotalSeconds;

        PayAbilityUpkeep(ent, seconds);

        // Regeneration can't happen during the drain anyway, so it doesn't need to pause it
        if (ent.Comp.Host is not { } host || _mobState.IsDead(host))
        {
            LoseChemicals(ent, ent.Comp.ChemicalDrain * seconds, lockout: false);
            return;
        }

        if (!IsChemicalRegenLockedOut(ent))
            AddChemicals(ent, ent.Comp.ChemicalRegen * seconds);
    }

    /// <summary>
    /// Clamps chemicals back into bounds once per tick, so the order of everything that changed them doesn't matter.
    /// </summary>
    private void ResolveChemicals(Entity<SymbioteComponent> ent)
    {
        // Something couldn't be paid for, so everything with upkeep ends
        if (ent.Comp.Chemicals < 0f)
            DeactivateAllAbilities(ent);

        var clamped = ent.Comp.ClampedChemicals;
        if (clamped == ent.Comp.Chemicals)
            return;

        ent.Comp.Chemicals = clamped;
        Dirty(ent);
    }

    public bool HasChemicals(Entity<SymbioteComponent> ent, float amount)
    {
        return ent.Comp.Chemicals >= amount;
    }

    /// <summary>
    /// Spends chemicals if the symbiote has enough, like for activating an ability.
    /// </summary>
    public bool TrySpendChemicals(Entity<SymbioteComponent> ent, float amount)
    {
        if (!HasChemicals(ent, amount))
            return false;

        LoseChemicals(ent, amount);
        return true;
    }

    /// <summary>
    /// Takes away chemicals even if the symbiote doesn't have enough, like for upkeep.
    /// Going below zero means something couldn't be paid for, which gets resolved at the end of the tick.
    /// </summary>
    /// <param name="ent">The symbiote.</param>
    /// <param name="amount">How many chemicals to take away.</param>
    /// <param name="lockout">Whether to pause regeneration, like losing chemicals usually does.</param>
    public void LoseChemicals(Entity<SymbioteComponent> ent, float amount, bool lockout = true)
    {
        if (amount <= 0f)
            return;

        ent.Comp.Chemicals -= amount;

        if (lockout)
            StartChemicalRegenLockout(ent);

        Dirty(ent);
    }

    /// <summary>
    /// Adds chemicals, even past the maximum. Anything over it is lost at the end of the tick.
    /// </summary>
    public void AddChemicals(Entity<SymbioteComponent> ent, float amount)
    {
        if (amount <= 0f)
            return;

        ent.Comp.Chemicals += amount;
        Dirty(ent);
    }

    /// <summary>
    /// Whether regeneration is paused from recently losing chemicals.
    /// </summary>
    public bool IsChemicalRegenLockedOut(Entity<SymbioteComponent> ent)
    {
        return _timing.CurTime < ent.Comp.ChemicalRegenLockoutEnd;
    }

    private void StartChemicalRegenLockout(Entity<SymbioteComponent> ent)
    {
        ent.Comp.ChemicalRegenLockoutEnd = _timing.CurTime + ent.Comp.ChemicalRegenLockout;
        Dirty(ent);
    }
}
