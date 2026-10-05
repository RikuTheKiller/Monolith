using System.Linq;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._Mono.ChemistryTrial;

/// <summary>
/// Runs chemistry time trials. Everything triggered by the user is predicted:
/// questions are derived deterministically from a networked seed, so the client knows the next question
/// without waiting for the server. Only the timed transitions (end of a reveal, end of the trial) are server-driven.
/// </summary>
public abstract partial class SharedChemistryTrialSystem : EntitySystem
{
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    /// <summary>
    /// Length limit for typed answers, so that nobody sends a novel over the network.
    /// </summary>
    public const int MaxAnswerLength = 256;

    private ChemistryTrialCatalog? _catalog;

    public ChemistryTrialCatalog Catalog => _catalog ??= ChemistryTrialCatalog.Build(_proto);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        Subs.BuiEvents<ChemistryTrialComponent>(ChemistryTrialUiKey.Key, subs =>
        {
            subs.Event<ChemistryTrialSetDurationMessage>(OnSetDuration);
            subs.Event<ChemistryTrialSetRevealDurationMessage>(OnSetRevealDuration);
            subs.Event<ChemistryTrialSetCategoryMessage>(OnSetCategory);
            subs.Event<ChemistryTrialSetAllCategoriesMessage>(OnSetAllCategories);
            subs.Event<ChemistryTrialStartMessage>(OnStart);
            subs.Event<ChemistryTrialAbortMessage>(OnAbort);
            subs.Event<ChemistryTrialDismissMessage>(OnDismiss);
            subs.Event<ChemistryTrialAnswerMessage>(OnAnswer);
        });
    }

    private void OnSetDuration(Entity<ChemistryTrialComponent> ent, ref ChemistryTrialSetDurationMessage args)
    {
        SetDuration(ent, args.Duration);
    }

    private void OnSetRevealDuration(Entity<ChemistryTrialComponent> ent, ref ChemistryTrialSetRevealDurationMessage args)
    {
        SetRevealDuration(ent, args.Duration);
    }

    private void OnSetCategory(Entity<ChemistryTrialComponent> ent, ref ChemistryTrialSetCategoryMessage args)
    {
        SetCategory(ent, args.Category, args.Enabled);
    }

    private void OnSetAllCategories(Entity<ChemistryTrialComponent> ent, ref ChemistryTrialSetAllCategoriesMessage args)
    {
        SetAllCategories(ent, args.Enabled);
    }

    private void OnStart(Entity<ChemistryTrialComponent> ent, ref ChemistryTrialStartMessage args)
    {
        TryStart(ent);
    }

    private void OnAbort(Entity<ChemistryTrialComponent> ent, ref ChemistryTrialAbortMessage args)
    {
        Abort(ent);
    }

    private void OnDismiss(Entity<ChemistryTrialComponent> ent, ref ChemistryTrialDismissMessage args)
    {
        Dismiss(ent);
    }

    private void OnAnswer(Entity<ChemistryTrialComponent> ent, ref ChemistryTrialAnswerMessage args)
    {
        TryAnswer(ent, args.Amounts, args.Temperature);
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<ReactionPrototype>() || args.WasModified<ReagentPrototype>())
            _catalog = null;
    }

    /// <summary>
    /// Called whenever the state changes locally, so the client can refresh an open UI with predicted state.
    /// </summary>
    protected virtual void UpdateUi(Entity<ChemistryTrialComponent> ent)
    {
    }

    private void Changed(Entity<ChemistryTrialComponent> ent)
    {
        Dirty(ent);
        UpdateUi(ent);
    }

    #region Configuration

    public void SetDuration(Entity<ChemistryTrialComponent> ent, TimeSpan duration)
    {
        if (ent.Comp.Phase == ChemistryTrialPhase.Running)
            return;

        var clamped = TimeSpan.FromSeconds(Math.Clamp(
            Math.Round(duration.TotalSeconds),
            ent.Comp.MinDuration.TotalSeconds,
            ent.Comp.MaxDuration.TotalSeconds));

        if (clamped == ent.Comp.Duration)
            return;

        ent.Comp.Duration = clamped;
        Changed(ent);
    }

    public void SetRevealDuration(Entity<ChemistryTrialComponent> ent, TimeSpan duration)
    {
        if (ent.Comp.Phase == ChemistryTrialPhase.Running)
            return;

        var clamped = TimeSpan.FromSeconds(Math.Clamp(
            Math.Round(duration.TotalSeconds),
            0,
            ent.Comp.MaxRevealDuration.TotalSeconds));

        if (clamped == ent.Comp.RevealDuration)
            return;

        ent.Comp.RevealDuration = clamped;
        Changed(ent);
    }

    public void SetCategory(Entity<ChemistryTrialComponent> ent, string category, bool enabled)
    {
        // Validate against the catalog so that clients cannot fill the set with junk.
        if (ent.Comp.Phase == ChemistryTrialPhase.Running || !Catalog.Categories.Contains(category))
            return;

        var changed = enabled
            ? ent.Comp.DisabledCategories.Remove(category)
            : ent.Comp.DisabledCategories.Add(category);

        if (changed)
            Changed(ent);
    }

    public void SetAllCategories(Entity<ChemistryTrialComponent> ent, bool enabled)
    {
        if (ent.Comp.Phase == ChemistryTrialPhase.Running)
            return;

        ent.Comp.DisabledCategories.Clear();
        if (!enabled)
            ent.Comp.DisabledCategories.UnionWith(Catalog.Categories);

        Changed(ent);
    }

    #endregion

    #region Trial

    public bool TryStart(Entity<ChemistryTrialComponent> ent)
    {
        if (ent.Comp.Phase == ChemistryTrialPhase.Running || Catalog.GetEligible(ent.Comp.DisabledCategories).Count == 0)
            return false;

        ent.Comp.Phase = ChemistryTrialPhase.Running;
        ent.Comp.RunSeed = ent.Comp.Seed;
        // Advanced deterministically so that the client predicts the same seed as the server.
        ent.Comp.Seed = new System.Random(ent.Comp.Seed).Next();
        ent.Comp.EndTime = Timing.CurTime + ent.Comp.Duration;
        ent.Comp.Passed = 0;
        ent.Comp.Failed = 0;
        ent.Comp.Revealing = false;
        ent.Comp.LastAnswer = string.Empty;
        SetQuestion(ent, 0);
        Changed(ent);
        return true;
    }

    public void Abort(Entity<ChemistryTrialComponent> ent)
    {
        if (ent.Comp.Phase != ChemistryTrialPhase.Running)
            return;

        ent.Comp.Phase = ChemistryTrialPhase.Idle;
        ent.Comp.Revealing = false;
        Changed(ent);
    }

    public void Dismiss(Entity<ChemistryTrialComponent> ent)
    {
        if (ent.Comp.Phase != ChemistryTrialPhase.Finished)
            return;

        ent.Comp.Phase = ChemistryTrialPhase.Idle;
        Changed(ent);
    }

    /// <summary>
    /// Answers the current question. A correct answer moves on immediately, a wrong one reveals the recipe first.
    /// </summary>
    /// <returns>Whether the answer was correct, or null if it was not accepted at all.</returns>
    public bool? TryAnswer(Entity<ChemistryTrialComponent> ent, string amounts, string temperature)
    {
        var comp = ent.Comp;
        if (comp.Phase != ChemistryTrialPhase.Running
            || comp.Revealing
            || Timing.CurTime >= comp.EndTime
            || !Catalog.TryGetRecipe(comp.QuestionReaction, out var recipe))
            return null;

        amounts = Truncate(amounts);
        temperature = Truncate(temperature);

        if (ChemistryTrialCatalog.IsCorrect(recipe, comp.QuestionDirection, amounts, temperature))
        {
            comp.Passed++;
            SetQuestion(ent, comp.QuestionIndex + 1);
            Changed(ent);
            return true;
        }

        comp.Failed++;

        if (comp.RevealDuration <= TimeSpan.Zero)
        {
            SetQuestion(ent, comp.QuestionIndex + 1);
            Changed(ent);
            return false;
        }

        comp.Revealing = true;
        comp.RevealEndTime = Timing.CurTime + comp.RevealDuration;
        if (amounts.Length == 0)
            amounts = Loc.GetString("chemistry-trial-answer-empty");

        comp.LastAnswer = comp.QuestionDirection == ChemistryTrialDirection.ProductToRecipe
            ? Loc.GetString("chemistry-trial-answer-with-temperature",
                ("amounts", amounts),
                ("temperature", temperature.Length == 0 ? Loc.GetString("chemistry-trial-temperature-any") : temperature))
            : amounts;
        Changed(ent);
        return false;
    }

    private static string Truncate(string text)
    {
        text = text.Trim();
        return text.Length > MaxAnswerLength ? text[..MaxAnswerLength] : text;
    }

    /// <summary>
    /// Advances the trial once the reveal is over, or ends it once time has run out.
    /// The final reveal is allowed to play out before the results are shown.
    /// </summary>
    protected void UpdateTrial(Entity<ChemistryTrialComponent> ent)
    {
        var comp = ent.Comp;
        if (comp.Phase != ChemistryTrialPhase.Running)
            return;

        var now = Timing.CurTime;
        if (comp.Revealing)
        {
            if (now < comp.RevealEndTime)
                return;

            comp.Revealing = false;
            comp.LastAnswer = string.Empty;

            if (now < comp.EndTime)
            {
                SetQuestion(ent, comp.QuestionIndex + 1);
                Changed(ent);
                return;
            }
        }
        else if (now < comp.EndTime)
        {
            return;
        }

        comp.Phase = ChemistryTrialPhase.Finished;
        comp.QuestionReaction = null;
        Changed(ent);
    }

    private void SetQuestion(Entity<ChemistryTrialComponent> ent, int index)
    {
        var (reaction, direction) = GetQuestion(ent.Comp.RunSeed, index, Catalog.GetEligible(ent.Comp.DisabledCategories));
        ent.Comp.QuestionIndex = index;
        ent.Comp.QuestionReaction = reaction;
        ent.Comp.QuestionDirection = direction;
    }

    /// <summary>
    /// Deterministically picks the question at <paramref name="index"/> of a trial.
    /// Every recipe is asked once, in a shuffled order, before any recipe is repeated.
    /// </summary>
    public static (ProtoId<ReactionPrototype>? Reaction, ChemistryTrialDirection Direction) GetQuestion(
        int seed,
        int index,
        IReadOnlyList<ChemistryTrialRecipe> eligible)
    {
        if (eligible.Count == 0)
            return (null, ChemistryTrialDirection.ProductToRecipe);

        var cycle = index / eligible.Count;
        var order = new int[eligible.Count];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        // System.Random with an explicit seed is deterministic across processes, unlike IRobustRandom.
        var shuffle = new System.Random(unchecked(seed + cycle * 7919));
        for (var i = order.Length - 1; i > 0; i--)
        {
            var j = shuffle.Next(i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }

        var recipe = eligible[order[index % eligible.Count]];
        var direction = new System.Random(unchecked(seed ^ (index * 104729 + 1))).Next(2) == 0
            ? ChemistryTrialDirection.ProductToRecipe
            : ChemistryTrialDirection.RecipeToProduct;

        return (recipe.Reaction, direction);
    }

    #endregion
}
