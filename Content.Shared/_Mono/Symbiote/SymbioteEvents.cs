using Content.Shared.Actions;
using Content.Shared.DoAfter;
using Robust.Shared.Serialization;

namespace Content.Shared._Mono.Symbiote;

public sealed partial class SymbioteLeaveHostActionEvent : InstantActionEvent;

/// <summary>
/// Debug only, instantly bonds with the target.
/// </summary>
public sealed partial class SymbioteDebugBondActionEvent : EntityTargetActionEvent;

/// <summary>
/// Raised on a symbiote that couldn't pay for something, like upkeep, so all of its abilities with upkeep should end.
/// </summary>
[ByRefEvent]
public record struct SymbioteChemicalsDepletedEvent;

/// <summary>
/// Raised when someone finishes eating a symbiote, or feeding it to someone else.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SymbioteEatDoAfterEvent : SimpleDoAfterEvent;
