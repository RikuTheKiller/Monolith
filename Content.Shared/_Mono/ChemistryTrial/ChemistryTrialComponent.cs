using Content.Shared.Chemistry.Reaction;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._Mono.ChemistryTrial;

/// <summary>
/// A machine that quizzes the user on chemical reaction recipes against the clock.
/// All of the UI state lives here so that it can be predicted and shared between everyone using the machine.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedChemistryTrialSystem), Other = AccessPermissions.ReadExecute)]
[AutoGenerateComponentState(raiseAfterAutoHandleState: true), AutoGenerateComponentPause]
public sealed partial class ChemistryTrialComponent : Component
{
    /// <summary>
    /// How long a time trial lasts.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan Duration = TimeSpan.FromMinutes(5);

    [DataField]
    public TimeSpan MinDuration = TimeSpan.FromSeconds(10);

    [DataField]
    public TimeSpan MaxDuration = TimeSpan.FromMinutes(99) + TimeSpan.FromSeconds(59);

    /// <summary>
    /// How long the correct recipe is shown after a failed question. Zero moves on immediately.
    /// </summary>
    [DataField, AutoNetworkedField]
    public TimeSpan RevealDuration = TimeSpan.FromSeconds(3);

    [DataField]
    public TimeSpan MaxRevealDuration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Reagent groups whose recipes are excluded from the time trial.
    /// Stored as an exclusion list so that every category is enabled by default.
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<string> DisabledCategories = new();

    [DataField, AutoNetworkedField]
    public ChemistryTrialPhase Phase = ChemistryTrialPhase.Idle;

    /// <summary>
    /// Seed of the next time trial. Questions are derived from it deterministically so that answers can be predicted.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int Seed;

    /// <summary>
    /// Seed of the time trial currently in progress.
    /// </summary>
    [DataField, AutoNetworkedField]
    public int RunSeed;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan EndTime;

    [DataField, AutoNetworkedField]
    public int QuestionIndex;

    [DataField, AutoNetworkedField]
    public ProtoId<ReactionPrototype>? QuestionReaction;

    [DataField, AutoNetworkedField]
    public ChemistryTrialDirection QuestionDirection;

    /// <summary>
    /// Whether the correct answer to a failed question is currently being shown.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool Revealing;

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan RevealEndTime;

    /// <summary>
    /// The failed answer, shown next to the correct recipe.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string LastAnswer = string.Empty;

    [DataField, AutoNetworkedField]
    public int Passed;

    [DataField, AutoNetworkedField]
    public int Failed;
}

[Serializable, NetSerializable]
public enum ChemistryTrialPhase : byte
{
    Idle,
    Running,
    Finished,
}

[Serializable, NetSerializable]
public enum ChemistryTrialDirection : byte
{
    /// <summary>
    /// The products are given, the reactants and temperature must be answered.
    /// </summary>
    ProductToRecipe,

    /// <summary>
    /// The reactants and temperature are given, the products must be answered.
    /// </summary>
    RecipeToProduct,
}
