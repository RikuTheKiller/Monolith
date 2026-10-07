using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Mono.Symbiote.Components;

/// <summary>
/// A symbiote that bonds with a host by being eaten and lives inside their skull.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(SharedSymbioteSystem))]
public sealed partial class SymbioteComponent : Component
{
    /// <summary>
    /// The host this symbiote is currently bonded to, if any.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Host;

    /// <summary>
    /// How long it takes to eat the symbiote yourself.
    /// </summary>
    [DataField]
    public TimeSpan EatDelay = TimeSpan.FromSeconds(3);

    /// <summary>
    /// How long it takes to feed the symbiote to someone else.
    /// </summary>
    [DataField]
    public TimeSpan FeedDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Components added to the symbiote while it's inside a host.
    /// </summary>
    [DataField]
    public ComponentRegistry? AddOnBond;

    /// <summary>
    /// Components removed from the symbiote while it's inside a host.
    /// </summary>
    [DataField]
    public ComponentRegistry? RemoveOnBond;

    /// <summary>
    /// The action for leaving the host, which the symbiote only has while bonded.
    /// </summary>
    [DataField]
    public EntProtoId LeaveHostAction = "ActionSymbioteLeaveHost";

    /// <summary>
    /// Created when the symbiote spawns, so bonding never has to spawn it.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? LeaveHostActionEntity;

    /// <summary>
    /// Played when a hostless symbiote nudges someone to get their attention.
    /// </summary>
    [DataField]
    public SoundSpecifier NudgeSound = new SoundPathSpecifier("/Audio/Voice/Slime/slime_squish.ogg");

    [DataField]
    public TimeSpan NudgeCooldown = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan NextNudge;
}
