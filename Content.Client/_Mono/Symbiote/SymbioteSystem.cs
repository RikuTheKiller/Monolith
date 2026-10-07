using Content.Shared._Mono.Symbiote;
using Content.Shared._Mono.Symbiote.Components;
using Content.Shared.Alert.Components;

namespace Content.Client._Mono.Symbiote;

public sealed partial class SymbioteSystem : SharedSymbioteSystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SymbioteComponent, GetGenericAlertCounterAmountEvent>(OnGetCounterAmount);
    }

    private void OnGetCounterAmount(Entity<SymbioteComponent> ent, ref GetGenericAlertCounterAmountEvent args)
    {
        if (args.Handled || ent.Comp.ChemicalsAlert != args.Alert)
            return;

        // The counter only shows whole numbers
        args.Amount = (int)MathF.Floor(ent.Comp.ClampedChemicals);
    }
}
