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
/// Toggles whichever symbiote ability the action belongs to.
/// </summary>
public sealed partial class SymbioteAbilityActionEvent : InstantActionEvent;

/// <summary>
/// Raised on an ability's action when it gets activated or deactivated.
/// </summary>
[ByRefEvent]
public readonly record struct SymbioteAbilityToggledEvent(EntityUid Symbiote, EntityUid Host, bool Active);

/// <summary>
/// Raised when someone finishes eating a symbiote, or feeding it to someone else.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SymbioteEatDoAfterEvent : SimpleDoAfterEvent;
