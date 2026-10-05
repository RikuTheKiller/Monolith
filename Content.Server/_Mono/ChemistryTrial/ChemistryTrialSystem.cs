using Content.Shared._Mono.ChemistryTrial;
using Robust.Shared.Random;

namespace Content.Server._Mono.ChemistryTrial;

public sealed partial class ChemistryTrialSystem : SharedChemistryTrialSystem
{
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ChemistryTrialComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<ChemistryTrialComponent> ent, ref MapInitEvent args)
    {
        // Every later seed is derived from this one deterministically, so that the client can predict it.
        ent.Comp.Seed = _random.Next();
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Timed transitions are left to the server: they are not triggered by the user, so there is nothing to predict.
        var query = EntityQueryEnumerator<ChemistryTrialComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            UpdateTrial((uid, comp));
        }
    }
}
