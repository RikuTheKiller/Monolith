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

    /// <summary>
    /// The start time of the pump being drawn, to tell when it's replaced by a new one.
    /// </summary>
    [ViewVariables]
    public TimeSpan? PumpStart;

    /// <summary>
    /// When this client starts drawing the pump emerging, which is when it first saw it, rather than when the server started it.
    /// Otherwise a pump learned about late would skip the start of emerging.
    /// </summary>
    [ViewVariables]
    public TimeSpan EmergeStart;

    /// <summary>
    /// When this client started drawing the pump going away, which is when it first saw it go, for the same reason.
    /// </summary>
    [ViewVariables]
    public TimeSpan? EndStart;

    /// <summary>
    /// The state the pump is going away with, kept so it can finish after the pump itself is gone.
    /// </summary>
    [ViewVariables]
    public string? EndState;
}
