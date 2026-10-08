using Content.Shared.Chat;
using Content.Shared.Chat.Prototypes;

namespace Content.Shared._Mono.Chat;

/// <summary>
/// Raised on an entity that means to say or emote something in character, to decide which body carries it out.
/// Normally their own, but it can be another's, like a symbiote speaking through its host's mouth.
/// </summary>
/// <param name="Source">Whose body carries it out. Starts as the entity's own, and can be changed to someone else's.</param>
/// <param name="Type">How it's being said, or whether it's an emote.</param>
/// <param name="Message">What's being said, or the emote being typed. Null for emotes performed directly, like from the emote menu.</param>
/// <param name="Emotes">The emotes it performs, whether typed or performed directly. Empty if it isn't an emote, or is one without any effects.</param>
[ByRefEvent]
public record struct ChatRedirectEvent(EntityUid Source, InGameICChatType Type, string? Message, IReadOnlyList<EmotePrototype> Emotes);
