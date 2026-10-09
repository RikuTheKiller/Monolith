namespace Content.Shared._Mono.Symbiote.Components;

/// <summary>
/// An ability that enhances its host's eyesight by switching on one of the symbiote's own kinds of vision.
/// The host sees with it too, the same way someone wearing goggles sees with the goggles' vision.
/// </summary>
[RegisterComponent]
[Access(typeof(SymbioteEyesSystem))]
public sealed partial class SymbioteEyesComponent : Component
{
    /// <summary>
    /// The name of the vision component on the symbiote that this ability switches on, like ThermalVision.
    /// </summary>
    [DataField(required: true)]
    public string Vision = string.Empty;
}
