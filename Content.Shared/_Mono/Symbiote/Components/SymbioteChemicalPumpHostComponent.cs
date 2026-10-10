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
    /// Client-only. Every sprite layer added to draw the pump, like the displacement map fitting it to the host's body.
    /// </summary>
    public readonly HashSet<string> RevealedLayers = new();
}
