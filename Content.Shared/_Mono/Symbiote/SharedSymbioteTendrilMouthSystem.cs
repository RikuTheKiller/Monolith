using System.Linq;
using Content.Shared._Mono.Chat;
using Content.Shared._Mono.Symbiote.Components;
using Content.Shared.Chat;
using Content.Shared.Chat.Prototypes;

namespace Content.Shared._Mono.Symbiote;

/// <summary>
/// Lets a symbiote take over its host's mouth, speaking through it while the host is muted.
/// </summary>
public abstract partial class SharedSymbioteTendrilMouthSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SymbioteTendrilMouthComponent, SymbioteAbilityToggledEvent>(OnToggled);
        SubscribeLocalEvent<SymbioteComponent, ChatRedirectEvent>(OnSymbioteChatRedirect);
    }

    private void OnToggled(Entity<SymbioteTendrilMouthComponent> ent, ref SymbioteAbilityToggledEvent args)
    {
        if (args.Active)
        {
            var mouth = EnsureComp<SymbioteTendrilMouthHostComponent>(args.Host);
            mouth.Symbiote = args.Symbiote;
            Dirty(args.Host, mouth);
        }
        else
        {
            RemComp<SymbioteTendrilMouthHostComponent>(args.Host);
        }

        OnMouthToggled(args.Symbiote, args.Host, args.Active);
    }

    /// <summary>
    /// Called after the host's mouth is taken over or given back.
    /// </summary>
    /// <param name="symbiote">The symbiote whose Tendril Mouth it is.</param>
    /// <param name="host">The host whose mouth it is.</param>
    /// <param name="active">Whether the symbiote took it over or gave it back.</param>
    protected virtual void OnMouthToggled(EntityUid symbiote, EntityUid host, bool active)
    {
    }

    /// <summary>
    /// Whether the symbiote's Tendril Mouth is active in its host.
    /// </summary>
    protected bool HasMouth(Entity<SymbioteComponent> ent, out EntityUid host)
    {
        host = default;
        if (ent.Comp.Host is not { } symbioteHost
            || !TryComp<SymbioteTendrilMouthHostComponent>(symbioteHost, out var mouth)
            || mouth.Symbiote != ent.Owner)
            return false;

        host = symbioteHost;
        return true;
    }

    private void OnSymbioteChatRedirect(Entity<SymbioteComponent> ent, ref ChatRedirectEvent args)
    {
        // Everything the symbiote says comes out of the host's mouth
        if (!HasMouth(ent, out var host))
            return;

        // The mouth can make vocal emotes, but the rest of the host's body isn't the symbiote's to move.
        // Symbiotes can't emote themselves, so not redirecting the others means they just don't happen.
        if (args.Type == InGameICChatType.Emote && !args.Emotes.Any(emote => emote.Category.HasFlag(EmoteCategory.Vocal)))
            return;

        args.Source = host;
    }
}
