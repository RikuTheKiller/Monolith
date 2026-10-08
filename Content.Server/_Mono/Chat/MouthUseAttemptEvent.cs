namespace Content.Server._Mono.Chat;

/// <summary>
/// Raised on a body when someone tries to use its mouth, to speak or make vocal emotes.
/// Without a working mouth, speech doesn't happen, and vocal emotes get pantomimed.
/// </summary>
/// <param name="User">Who's trying to use the mouth. Usually the body itself, but not always, like a symbiote speaking through its host.</param>
[ByRefEvent]
public record struct MouthUseAttemptEvent(EntityUid User)
{
    /// <summary>
    /// Whether the mouth doesn't work for the user.
    /// </summary>
    public bool Blocked;

    /// <summary>
    /// Why it doesn't work, shown to the user when they try to speak.
    /// </summary>
    public LocId? Reason;
}
