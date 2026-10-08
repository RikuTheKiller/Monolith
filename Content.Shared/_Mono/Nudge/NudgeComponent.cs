using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._Mono.Nudge;

/// <summary>
/// Lets an entity nudge mobs to get their attention, by clicking them outside of combat mode.
/// Meant for things that can't do much else, like a symbiote without a host.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
[Access(typeof(NudgeSystem))]
public sealed partial class NudgeComponent : Component
{
    [DataField]
    public SoundSpecifier Sound = new SoundPathSpecifier("/Audio/Voice/Slime/slime_squish.ogg");

    [DataField]
    public TimeSpan Cooldown = TimeSpan.FromSeconds(1);

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan NextNudge;
}
