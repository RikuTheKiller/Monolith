using Content.Shared._Mono.Chat;
using Content.Shared.ActionBlocker;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Speech;
using Content.Shared.Whitelist;

namespace Content.Shared.Chat;

// Mono - Shared so the client can tell which emotes someone can use, like for the emote wheel
public abstract partial class SharedChatSystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    /// <summary>
    /// Whether someone can use the emote, with whichever body would carry it out.
    /// </summary>
    public bool CanUseEmote(EntityUid speaker, EmotePrototype emote)
    {
        var body = Redirect(speaker, InGameICChatType.Emote, null, [emote]);
        return _actionBlocker.CanEmote(body) && AllowedToUseEmote(body, emote);
    }

    /// <summary>
    /// Decides whose body carries out what someone means to say or emote.
    /// Normally their own, but it can be another's, like a symbiote speaking through its host's mouth.
    /// </summary>
    /// <param name="speaker">Who means to say or emote it.</param>
    /// <param name="type">How it's being said, or whether it's an emote.</param>
    /// <param name="message">What's being said, or the emote being typed, if any.</param>
    /// <param name="emotes">The emotes it performs, whether typed or performed directly.</param>
    /// <returns>Whose body carries it out.</returns>
    public EntityUid Redirect(EntityUid speaker, InGameICChatType type, string? message, IReadOnlyList<EmotePrototype> emotes)
    {
        var ev = new ChatRedirectEvent(speaker, type, message, emotes);
        RaiseLocalEvent(speaker, ref ev);
        return ev.Source;
    }

    /// <summary>
    /// Checks if we can use this emote based on the emotes whitelist, blacklist, and availibility to the entity.
    /// Moved here from the server's ChatSystem.
    /// </summary>
    /// <param name="source">The entity that is speaking</param>
    /// <param name="emote">The emote being used</param>
    protected bool AllowedToUseEmote(EntityUid source, EmotePrototype emote)
    {
        // If emote is in AllowedEmotes, it will bypass whitelist and blacklist
        if (TryComp<SpeechComponent>(source, out var speech) &&
            speech.AllowedEmotes.Contains(emote.ID))
        {
            return true;
        }

        // Check the whitelist and blacklist
        if (_whitelist.IsWhitelistFail(emote.Whitelist, source) ||
            _whitelist.IsBlacklistPass(emote.Blacklist, source))
        {
            return false;
        }

        // Check if the emote is available for all
        if (!emote.Available)
        {
            return false;
        }

        return true;
    }
}
