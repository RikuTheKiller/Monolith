using Content.Shared._Mono.Symbiote.Components;
using Content.Shared._White.Overlays;
using Content.Shared.Inventory.Events;

namespace Content.Shared._Mono.Symbiote;

/// <summary>
/// Switches the symbiote's own thermal vision on and off with Thermal Eyes, and lets its host see with it.
/// The symbiote is the source of the vision, like goggles are for whoever wears them, so nothing is ever added to the host.
/// Predicted, so the symbiote sees it the moment it activates the ability.
/// </summary>
public sealed class SymbioteThermalEyesSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SymbioteThermalEyesComponent, SymbioteAbilityToggledEvent>(OnToggled);

        SubscribeLocalEvent<SymbioteHostComponent, RefreshEquipmentHudEvent<ThermalVisionComponent>>(OnHostRefreshThermalVision);
    }

    private void OnToggled(Entity<SymbioteThermalEyesComponent> ent, ref SymbioteAbilityToggledEvent args)
    {
        if (TerminatingOrDeleted(args.Symbiote) || !TryComp<ThermalVisionComponent>(args.Symbiote, out var vision))
            return;

        vision.IsActive = args.Active;
        Dirty(args.Symbiote, vision);

        // Lets whoever sees with it update their overlays, like switching goggles does
        var ev = new SwitchableOverlayToggledEvent(args.Symbiote, args.Active);
        RaiseLocalEvent(args.Symbiote, ref ev);
    }

    private void OnHostRefreshThermalVision(Entity<SymbioteHostComponent> ent, ref RefreshEquipmentHudEvent<ThermalVisionComponent> args)
    {
        // The host sees with their symbiote's thermal vision too
        if (ent.Comp.Symbiote is { } symbiote)
            RaiseLocalEvent(symbiote, ref args);
    }
}
