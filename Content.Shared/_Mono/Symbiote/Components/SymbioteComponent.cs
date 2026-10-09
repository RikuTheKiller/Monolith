using Content.Shared.Alert;
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
    /// What the symbiote's abilities run on. Can briefly go out of bounds, since it only gets clamped once per tick.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Chemicals;

    /// <summary>
    /// <see cref="Chemicals"/>, but within bounds.
    /// </summary>
    public float ClampedChemicals => Math.Clamp(Chemicals, 0f, MaxChemicals);

    [DataField]
    public float MaxChemicals = 300f;

    /// <summary>
    /// Chemicals gained per second inside a living host.
    /// </summary>
    [DataField]
    public float ChemicalRegen = 1f;

    /// <summary>
    /// Chemicals lost per second anywhere other than inside a living host.
    /// </summary>
    [DataField]
    public float ChemicalDrain = 5f;

    /// <summary>
    /// How long regeneration pauses for after losing chemicals for any reason.
    /// </summary>
    [DataField]
    public TimeSpan ChemicalRegenLockout = TimeSpan.FromSeconds(3);

    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan ChemicalRegenLockoutEnd;

    /// <summary>
    /// How often the symbiote updates its chemicals and active abilities.
    /// </summary>
    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(0.1);

    /// <summary>
    /// Networked so the client regenerates and drains chemicals in step with the server.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan NextUpdate;

    [DataField]
    public ProtoId<AlertPrototype> ChemicalsAlert = "SymbioteChemicals";
}
