using System.Diagnostics.CodeAnalysis;

namespace Content.Server.Chat.Managers;

public interface IChatSanitizationManager
{
    public void Initialize();

    public bool TrySanitizeEmoteShorthands(string input,
        out string sanitized,
        [NotNullWhen(true)] out string? emoteKey); // Mono - The key instead of the worded emote, so it's worded for whoever ends up doing it
}
