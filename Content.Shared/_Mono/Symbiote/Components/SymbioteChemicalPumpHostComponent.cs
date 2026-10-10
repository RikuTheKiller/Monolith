using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._Mono.Symbiote.Components;

/// <summary>
/// A host with a Chemical Pump on their chest, drawn on top of whatever they're wearing.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
[Access(typeof(SharedSymbioteChemicalPumpSystem))]
public sealed partial class SymbioteChemicalPumpHostComponent : Component
{
    /// <summary>
    /// What the pump looks like.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SpriteSpecifier? Sprite;

    /// <summary>
    /// The state that plays once as the pump emerges, if any.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? EmergeState;

    /// <summary>
    /// When the pump started emerging, so anyone who only sees the host later doesn't see it emerge again.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan StartTime;

    /// <summary>
    /// Client-only. Every sprite layer added to draw the pump, like the displacement map fitting it to the host's body.
    /// </summary>
    public readonly HashSet<string> RevealedLayers = new();
}
