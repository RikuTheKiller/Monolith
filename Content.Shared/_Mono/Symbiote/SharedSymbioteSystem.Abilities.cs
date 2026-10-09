using Content.Shared._Mono.Symbiote.Components;
using Content.Shared.Actions.Events;

namespace Content.Shared._Mono.Symbiote;

public abstract partial class SharedSymbioteSystem
{
    private void InitializeAbilities()
    {
        SubscribeLocalEvent<SymbioteComponent, SymbioteAbilityActionEvent>(OnAbilityAction);
        SubscribeLocalEvent<SymbioteAbilityComponent, ActionPerformedEvent>(OnAbilityPerformed);
    }

    private void OnAbilityAction(Entity<SymbioteComponent> ent, ref SymbioteAbilityActionEvent args)
    {
        if (args.Handled || !TryComp<SymbioteAbilityComponent>(args.Action, out var ability) || !CheckHasHost(ent, out _))
            return;

        var abilityEnt = (args.Action.Owner, ability);
        if (IsAbilityActive(abilityEnt))
        {
            DeactivateAbility(ent, abilityEnt);
            args.Handled = true;
            return;
        }

        // Not handling a failed activation keeps it from going on cooldown
        args.Handled = TryActivateAbility(ent, abilityEnt);
    }

    private void OnAbilityPerformed(Entity<SymbioteAbilityComponent> ent, ref ActionPerformedEvent args)
    {
        // Performing an action clears its cooldown afterwards, which undoes the one SetAbilityActive just started
        if (!IsAbilityActive(ent))
            _actions.SetCooldown(ent, ent.Comp.Cooldown);
    }

    /// <summary>
    /// Whether the ability is active, which is whether its action is toggled on.
    /// </summary>
    public bool IsAbilityActive(Entity<SymbioteAbilityComponent> ability)
    {
        return _actions.TryGetActionData(ability, out var action, logError: false) && action.Toggled;
    }

    /// <summary>
    /// Activates the ability if the symbiote is bonded and can pay for it.
    /// </summary>
    public bool TryActivateAbility(Entity<SymbioteComponent> ent, Entity<SymbioteAbilityComponent> ability)
    {
        if (IsAbilityActive(ability) || ent.Comp.Host is not { } host)
            return false;

        if (!TrySpendChemicals(ent, ability.Comp.ActivationCost))
        {
            _popup.PopupClient(Loc.GetString("symbiote-ability-not-enough-chemicals"), ent, ent);
            return false;
        }

        SetAbilityActive(ent, ability, host, true);
        return true;
    }

    /// <summary>
    /// Deactivates the ability and starts its cooldown.
    /// </summary>
    public void DeactivateAbility(Entity<SymbioteComponent> ent, Entity<SymbioteAbilityComponent> ability)
    {
        if (!IsAbilityActive(ability) || ent.Comp.Host is not { } host)
            return;

        SetAbilityActive(ent, ability, host, false);
    }

    public void DeactivateAllAbilities(Entity<SymbioteComponent> ent)
    {
        foreach (var (actionUid, _) in _actions.GetActions(ent))
        {
            if (TryComp<SymbioteAbilityComponent>(actionUid, out var ability))
                DeactivateAbility(ent, (actionUid, ability));
        }
    }

    private void SetAbilityActive(Entity<SymbioteComponent> ent, Entity<SymbioteAbilityComponent> ability, EntityUid host, bool active)
    {
        // The action being toggled on is what makes the ability active
        _actions.SetToggled(ability, active);

        // Abilities go on cooldown once they end, not when they start.
        // This covers every way of ending, but ending through the action itself also needs OnAbilityPerformed.
        if (!active)
            _actions.SetCooldown(ability, ability.Comp.Cooldown);

        var ev = new SymbioteAbilityToggledEvent(ent, host, active);
        RaiseLocalEvent(ability, ref ev);
    }

    /// <summary>
    /// Pays the upkeep of every active ability, even if the symbiote can't afford it.
    /// </summary>
    private void PayAbilityUpkeep(Entity<SymbioteComponent> ent, float seconds)
    {
        var upkeep = 0f;
        foreach (var (actionUid, action) in _actions.GetActions(ent))
        {
            if (action.Toggled && TryComp<SymbioteAbilityComponent>(actionUid, out var ability))
                upkeep += ability.Upkeep;
        }

        LoseChemicals(ent, upkeep * seconds);
    }

    /// <summary>
    /// Lets every active ability do whatever it does over time, in step with the symbiote's chemicals.
    /// </summary>
    private void UpdateAbilities(Entity<SymbioteComponent> ent, float seconds)
    {
        if (ent.Comp.Host is not { } host)
            return;

        foreach (var (actionUid, action) in _actions.GetActions(ent))
        {
            if (!action.Toggled || !HasComp<SymbioteAbilityComponent>(actionUid))
                continue;

            var ev = new SymbioteAbilityUpdateEvent(ent, host, seconds);
            RaiseLocalEvent(actionUid, ref ev);
        }
    }
}
