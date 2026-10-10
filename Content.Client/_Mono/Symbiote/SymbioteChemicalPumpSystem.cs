using Content.Client.DisplacementMap;
using Content.Shared._Mono.Symbiote;
using Content.Shared._Mono.Symbiote.Components;
using Content.Shared.DisplacementMap;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;

namespace Content.Client._Mono.Symbiote;

public sealed class SymbioteChemicalPumpSystem : SharedSymbioteChemicalPumpSystem
{
    [Dependency] private DisplacementMapSystem _displacement = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private const string LayerKey = "symbiote-chemical-pump";

    /// <summary>
    /// The pump sits on top of the host's armor, so it's placed and shaped like their outer clothing.
    /// </summary>
    private const string Slot = "outerClothing";

    /// <summary>
    /// The layer the pump is drawn right before, so it ends up right after the outer clothing's layers.
    /// </summary>
    private const string NextSlot = "eyes";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SymbioteChemicalPumpHostComponent, ComponentStartup>(OnHostStartup);
        SubscribeLocalEvent<SymbioteChemicalPumpHostComponent, AfterAutoHandleStateEvent>(OnHostHandleState);
        SubscribeLocalEvent<SymbioteChemicalPumpHostComponent, ComponentShutdown>(OnHostShutdown);
    }

    private void OnHostStartup(Entity<SymbioteChemicalPumpHostComponent> ent, ref ComponentStartup args)
    {
        UpdateLayer(ent);
    }

    private void OnHostHandleState(Entity<SymbioteChemicalPumpHostComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        UpdateLayer(ent);
    }

    private void OnHostShutdown(Entity<SymbioteChemicalPumpHostComponent> ent, ref ComponentShutdown args)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        foreach (var key in ent.Comp.RevealedLayers)
        {
            _sprite.RemoveLayer((ent, sprite), key);
        }

        ent.Comp.RevealedLayers.Clear();
    }

    private void UpdateLayer(Entity<SymbioteChemicalPumpHostComponent> ent)
    {
        if (ent.Comp.Sprite is not { } pump || !TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (_sprite.LayerMapTryGet((ent, sprite), LayerKey, out var index, false))
        {
            _sprite.LayerSetSprite((ent, sprite), index, pump);
            return;
        }

        // Anything without that layer, like a body with different layers, just gets it on top of everything
        int? insertAt = _sprite.LayerMapTryGet((ent, sprite), NextSlot, out var nextSlot, false) ? nextSlot : null;
        index = _sprite.AddLayer((ent, sprite), pump, insertAt);
        _sprite.LayerMapSet((ent, sprite), LayerKey, index);
        ent.Comp.RevealedLayers.Add(LayerKey);

        // Drawn for a human chest, then moved and warped onto the host's body shape the same way their armor is,
        // so it stays on the upper chest of shorter or differently shaped species
        if (!TryComp<InventoryComponent>(ent, out var inventory))
            return;

        if (_inventory.TryGetSlot(ent, Slot, out var slot, inventory))
            _sprite.LayerSetOffset((ent, sprite), index, slot.Offset);

        if (GetDisplacement(ent, inventory) is { } displacement)
            _displacement.TryAddDisplacement(displacement, sprite, index, LayerKey, ent.Comp.RevealedLayers);
    }

    /// <summary>
    /// The displacement map the host's outer clothing would use, picked the same way clothing picks it.
    /// </summary>
    private DisplacementData? GetDisplacement(EntityUid host, InventoryComponent inventory)
    {
        var displacement = inventory.Displacements.GetValueOrDefault(Slot);

        switch (CompOrNull<HumanoidAppearanceComponent>(host)?.Sex)
        {
            case Sex.Male when inventory.MaleDisplacements.Count > 0:
                return inventory.MaleDisplacements.GetValueOrDefault(Slot);
            case Sex.Female when inventory.FemaleDisplacements.Count > 0:
                return inventory.FemaleDisplacements.GetValueOrDefault(Slot);
            default:
                return displacement;
        }
    }
}
