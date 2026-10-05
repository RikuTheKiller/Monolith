using Content.Shared._Mono.ChemistryTrial;

namespace Content.Client._Mono.ChemistryTrial;

public sealed partial class ChemistryTrialSystem : SharedChemistryTrialSystem
{
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    /// <summary>
    /// Machines whose UI needs refreshing. Refreshes are batched to once per frame, so the UI only ever shows
    /// the final predicted state rather than every intermediate state from prediction resets.
    /// </summary>
    private readonly HashSet<EntityUid> _dirtyUis = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChemistryTrialComponent, AfterAutoHandleStateEvent>(OnAfterState);
    }

    private void OnAfterState(Entity<ChemistryTrialComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        _dirtyUis.Add(ent);
    }

    protected override void UpdateUi(Entity<ChemistryTrialComponent> ent)
    {
        _dirtyUis.Add(ent);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        foreach (var uid in _dirtyUis)
        {
            if (_ui.TryGetOpenUi<ChemistryTrialBoundUserInterface>(uid, ChemistryTrialUiKey.Key, out var bui))
                bui.Update();
        }

        _dirtyUis.Clear();
    }
}
