using Content.Shared._Mono.ChemistryTrial;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._Mono.ChemistryTrial;

[UsedImplicitly]
public sealed class ChemistryTrialBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private ChemistryTrialWindow? _window;

    public ChemistryTrialBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<ChemistryTrialWindow>();

        // Everything is predicted, so the UI reacts instantly instead of fighting with the server.
        _window.DurationChanged += duration => SendPredictedMessage(new ChemistryTrialSetDurationMessage(duration));
        _window.RevealDurationChanged += duration => SendPredictedMessage(new ChemistryTrialSetRevealDurationMessage(duration));
        _window.CategoryToggled += (category, enabled) => SendPredictedMessage(new ChemistryTrialSetCategoryMessage(category, enabled));
        _window.AllCategoriesToggled += enabled => SendPredictedMessage(new ChemistryTrialSetAllCategoriesMessage(enabled));
        _window.StartPressed += () => SendPredictedMessage(new ChemistryTrialStartMessage());
        _window.AbortPressed += () => SendPredictedMessage(new ChemistryTrialAbortMessage());
        _window.DismissPressed += () => SendPredictedMessage(new ChemistryTrialDismissMessage());
        _window.AnswerSubmitted += (amounts, temperature) => SendPredictedMessage(new ChemistryTrialAnswerMessage(amounts, temperature));

        Update();
    }

    public override void Update()
    {
        base.Update();

        if (_window == null || !EntMan.TryGetComponent(Owner, out ChemistryTrialComponent? trial))
            return;

        _window.UpdateState(trial, EntMan.System<ChemistryTrialSystem>().Catalog);
    }
}
