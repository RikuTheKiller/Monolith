using Robust.Shared.GameStates;

namespace Content.Shared._Mono.Symbiote.Components;

/// <summary>
/// A host whose mouth is taken over by their symbiote's Tendril Mouth, so only the symbiote can speak with it.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedSymbioteTendrilMouthSystem))]
public sealed partial class SymbioteTendrilMouthHostComponent : Component
{
    [DataField, AutoNetworkedField]
    public EntityUid Symbiote;
}
