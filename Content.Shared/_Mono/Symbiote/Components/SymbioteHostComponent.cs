using Robust.Shared.GameStates;

namespace Content.Shared._Mono.Symbiote.Components;

/// <summary>
/// A host with a symbiote living inside their skull.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(SharedSymbioteSystem))]
public sealed partial class SymbioteHostComponent : Component
{
    public const string ContainerId = "symbiote";

    /// <summary>
    /// The symbiote living inside this host.
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Symbiote;
}
