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
/// Opens or closes a health analyzer on the host.
/// </summary>
public sealed partial class SymbioteCheckBloodActionEvent : InstantActionEvent;

/// <summary>
/// Toggles whichever symbiote ability the action belongs to.
/// </summary>
public sealed partial class SymbioteAbilityActionEvent : InstantActionEvent;

/// <summary>
/// Raised on an ability's action when it gets activated or deactivated.
/// </summary>
/// <param name="Symbiote">The symbiote whose ability it is.</param>
/// <param name="Host">The symbiote's host.</param>
/// <param name="Active">Whether it got activated or deactivated.</param>
/// <param name="User">Whoever's input toggled it, if anyone. Only their client predicts it, which matters for things that can't be rolled back, like sounds.</param>
[ByRefEvent]
public readonly record struct SymbioteAbilityToggledEvent(EntityUid Symbiote, EntityUid Host, bool Active, EntityUid? User);

/// <summary>
/// Raised on an active ability's action every time the symbiote updates, for whatever it does over time.
/// </summary>
[ByRefEvent]
public readonly record struct SymbioteAbilityUpdateEvent(EntityUid Symbiote, EntityUid Host, float Seconds);

/// <summary>
/// Raised when someone finishes eating a symbiote, or feeding it to someone else.
/// </summary>
[Serializable, NetSerializable]
public sealed partial class SymbioteEatDoAfterEvent : SimpleDoAfterEvent;
