namespace Content.Client._Mono.Symbiote;

/// <summary>
/// Client-only. Keeps track of drawing a host's Chemical Pump.
/// Lives on its own, so prediction removing and re-adding the networked pump doesn't reset it.
/// </summary>
[RegisterComponent]
[Access(typeof(SymbioteChemicalPumpSystem))]
public sealed partial class SymbioteChemicalPumpVisualsComponent : Component
{
    /// <summary>
    /// Every sprite layer added to draw the pump, like the displacement map fitting it to the host's body.
    /// </summary>
    [ViewVariables]
    public readonly HashSet<string> RevealedLayers = new();

    /// <summary>
    /// The frame the pump was on last time it was drawn, so it beats once when it reaches the beat frame.
    /// </summary>
    [ViewVariables]
    public int LastFrame = -1;
}
