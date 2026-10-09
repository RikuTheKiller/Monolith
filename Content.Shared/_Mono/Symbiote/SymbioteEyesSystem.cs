using Content.Shared._Mono.Symbiote.Components;
using Content.Shared._White.Overlays;
using Content.Shared.Inventory.Events;

namespace Content.Shared._Mono.Symbiote;

/// <summary>
/// Switches the symbiote's own kinds of vision on and off with its eye abilities, and lets its host see with them.
/// The symbiote is the source of the vision, like goggles are for whoever wears them, so nothing is ever added to the host.
/// Predicted, so the symbiote sees it the moment it activates the ability.
/// </summary>
public sealed class SymbioteEyesSystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SymbioteEyesComponent, SymbioteAbilityToggledEvent>(OnToggled);

        SubscribeLocalEvent<SymbioteHostComponent, RefreshEquipmentHudEvent<ThermalVisionComponent>>(RelayVision);
    }

    private void OnToggled(Entity<SymbioteEyesComponent> ent, ref SymbioteAbilityToggledEvent args)
    {
        if (TerminatingOrDeleted(args.Symbiote))
            return;

        var type = Factory.GetRegistration(ent.Comp.Vision).Type;
        if (!EntityManager.TryGetComponent(args.Symbiote, type, out var comp) || comp is not SwitchableVisionOverlayComponent vision)
        {
            Log.Error($"{ToPrettyString(args.Symbiote)} has no {ent.Comp.Vision} vision for {ToPrettyString(ent)} to switch on");
            return;
        }

        vision.IsActive = args.Active;
        Dirty(args.Symbiote, vision);

        // Lets whoever sees with it update their overlays, like switching goggles does
        var ev = new SwitchableOverlayToggledEvent(args.Symbiote, args.Active);
        RaiseLocalEvent(args.Symbiote, ref ev);
    }

    /// <summary>
    /// Asks the symbiote what it can see, when the host's vision is being worked out.
    /// </summary>
    private void RelayVision<T>(Entity<SymbioteHostComponent> ent, ref RefreshEquipmentHudEvent<T> args) where T : IComponent
    {
        if (ent.Comp.Symbiote is { } symbiote)
            RaiseLocalEvent(symbiote, ref args);
    }
}
