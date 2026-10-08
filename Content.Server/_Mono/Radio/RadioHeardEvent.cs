using Content.Server.Radio;

namespace Content.Server._Mono.Radio;

/// <summary>
/// Raised on someone wearing a radio that just received a message, as soon as it reaches their ears.
/// Lets others hearing through them, like a symbiote in their head, hear it too.
/// </summary>
/// <param name="Radio">The radio the message came through.</param>
/// <param name="Message">The message, in every form it can be understood in.</param>
[ByRefEvent]
public readonly record struct RadioHeardEvent(EntityUid Radio, RadioReceiveEvent Message);
