using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._Mono.Symbiote.Components;

/// <summary>
/// A host that has had a Chemical Pump on their chest, drawn on top of whatever they're wearing.
/// Stays once added, with the pump's state in their appearance, the same way doors keep their state.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(SharedSymbioteChemicalPumpSystem))]
public sealed partial class SymbioteChemicalPumpHostComponent : Component
{
    /// <summary>
    /// What the pump looks like while it's beating.
    /// </summary>
    [DataField, AutoNetworkedField]
    public SpriteSpecifier? Sprite;

    /// <summary>
    /// The state that plays once as the pump emerges, if any.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? EmergeState;

    /// <summary>
    /// The state that plays once as the pump retracts, if any.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? RetractState;

    /// <summary>
    /// The state that plays once as the pump bursts, if any.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? BurstState;

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
    /// When the pump moves on from emerging, retracting or bursting. Null while it's beating or gone.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan? NextStateChange;

    /// <summary>
    /// Client-only. The frame the pump was on last time it was checked, so it beats once when it reaches the beat frame.
    /// </summary>
    public int LastFrame = -1;

    /// <summary>
    /// Client-only. The state the client last drew the pump in, so it only reacts when that changes.
    /// </summary>
    public SymbioteChemicalPumpState? ShownState;
}

[Serializable, NetSerializable]
public enum SymbioteChemicalPumpVisuals : byte
{
    State,
}

[Serializable, NetSerializable]
public enum SymbioteChemicalPumpState : byte
{
    None,
    Emerging,
    Active,
    Retracting,
    Bursting,
}
