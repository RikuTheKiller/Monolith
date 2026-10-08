using Content.Server._EinsteinEngines.Language;
using Content.Server._Mono.Chat;
using Content.Shared._EinsteinEngines.Language.Events;
using Content.Shared._Mono.Symbiote;
using Content.Shared._Mono.Symbiote.Components;

namespace Content.Server._Mono.Symbiote;

public sealed partial class SymbioteTendrilMouthSystem : SharedSymbioteTendrilMouthSystem
{
    [Dependency] private LanguageSystem _language = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SymbioteComponent, DetermineEntityLanguagesEvent>(OnSymbioteDetermineLanguages, after: new[] { typeof(LanguageSystem) });

        SubscribeLocalEvent<SymbioteTendrilMouthHostComponent, LanguagesUpdateEvent>(OnHostLanguagesUpdate);
        SubscribeLocalEvent<SymbioteTendrilMouthHostComponent, MouthUseAttemptEvent>(OnHostMouthUseAttempt);
    }

    protected override void OnMouthToggled(EntityUid symbiote, EntityUid host, bool active)
    {
        if (TerminatingOrDeleted(symbiote))
            return;

        _language.UpdateEntityLanguages(symbiote);

        // Start off speaking whatever the host was speaking
        if (active)
            _language.SetLanguage(symbiote, _language.GetLanguage(host).ID);
    }

    private void OnHostMouthUseAttempt(Entity<SymbioteTendrilMouthHostComponent> ent, ref MouthUseAttemptEvent args)
    {
        // The tendrils only answer to the symbiote
        if (args.User == ent.Comp.Symbiote)
            return;

        args.Blocked = true;
        args.Reason = "symbiote-tendril-mouth-muted";
    }

    private void OnSymbioteDetermineLanguages(Entity<SymbioteComponent> ent, ref DetermineEntityLanguagesEvent args)
    {
        // Speaks the host's languages instead of Psychomantic, but still understands everything
        if (!HasMouth(ent, out var host))
            return;

        args.SpokenLanguages.Clear();
        foreach (var language in _language.GetSpokenLanguages(host))
        {
            args.SpokenLanguages.Add(language);
        }
    }

    private void OnHostLanguagesUpdate(Entity<SymbioteTendrilMouthHostComponent> ent, ref LanguagesUpdateEvent args)
    {
        // Keep up with the host learning or forgetting languages, like from a translator implant
        _language.UpdateEntityLanguages(ent.Comp.Symbiote);
    }
}
