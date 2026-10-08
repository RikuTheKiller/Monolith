namespace Content.Shared._Mono.Symbiote.Components;

/// <summary>
/// A symbiote ability, on the action that toggles it. It's active while the action is toggled on.
/// </summary>
[RegisterComponent]
[Access(typeof(SharedSymbioteSystem))]
public sealed partial class SymbioteAbilityComponent : Component
{
    /// <summary>
    /// Chemicals spent to activate the ability.
    /// </summary>
    [DataField]
    public float ActivationCost;

    /// <summary>
    /// Chemicals spent per second to keep the ability active.
    /// </summary>
    [DataField]
    public float Upkeep;

    /// <summary>
    /// How long after the ability ends before it can be activated again.
    /// </summary>
    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(3);
}
