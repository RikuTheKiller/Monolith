using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Utility;

namespace Content.Shared._Mono.Symbiote.Components;

/// <summary>
/// A host with a Chemical Pump on their chest, drawn on top of whatever they're wearing.
/// Stays while the pump goes away after the ability ends, by retracting or bursting.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
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
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan StartTime;

    /// <summary>
    /// The sound of one beat.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SoundSpecifier? BeatSound;

    /// <summary>
    /// The frame of the sprite's own state that the pump beats on.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int BeatFrame;

    /// <summary>
    /// The state that plays once as the pump goes away, like it retracting or bursting.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? EndState;

    /// <summary>
    /// When the pump started going away. Null while it's still active.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan? EndStart;

    /// <summary>
    /// When the pump is done going away and gets removed. Null while it's still active.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan? EndTime;
}
