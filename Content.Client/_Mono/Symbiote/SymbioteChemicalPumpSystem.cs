using Content.Client.DisplacementMap;
using Content.Shared._Mono.Symbiote;
using Content.Shared._Mono.Symbiote.Components;
using Content.Shared.DisplacementMap;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Timing;

namespace Content.Client._Mono.Symbiote;

public sealed class SymbioteChemicalPumpSystem : SharedSymbioteChemicalPumpSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AnimationPlayerSystem _animation = default!;
    [Dependency] private DisplacementMapSystem _displacement = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private const string LayerKey = "symbiote-chemical-pump";
    private const string EmergeAnimationKey = "symbiote-chemical-pump-emerge";

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
        SubscribeLocalEvent<SymbioteChemicalPumpHostComponent, AnimationCompletedEvent>(OnHostAnimationCompleted);
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
        _animation.Stop(ent.Owner, null, EmergeAnimationKey);

        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;

        foreach (var key in ent.Comp.RevealedLayers)
        {
            _sprite.RemoveLayer((ent.Owner, sprite), key);
        }

        ent.Comp.RevealedLayers.Clear();
    }

    private void OnHostAnimationCompleted(Entity<SymbioteChemicalPumpHostComponent> ent, ref AnimationCompletedEvent args)
    {
        // Playing a state once leaves the layer stuck on it, so go back to the pump's own looping state
        if (args.Key != EmergeAnimationKey || !args.Finished || ent.Comp.Sprite is not { } pump)
            return;

        _sprite.LayerSetSprite(ent.Owner, LayerKey, pump);
        _sprite.LayerSetAutoAnimated(ent.Owner, LayerKey, true);
    }

    private void UpdateLayer(Entity<SymbioteChemicalPumpHostComponent> ent)
    {
        if (ent.Comp.Sprite is not { } pump || !TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (_sprite.LayerMapTryGet((ent.Owner, sprite), LayerKey, out var index, false))
        {
            // Setting it now would cut the emerging short
            if (!_animation.HasRunningAnimation(ent.Owner, EmergeAnimationKey))
                _sprite.LayerSetSprite((ent.Owner, sprite), index, pump);

            return;
        }

        // Anything without that layer, like a body with different layers, just gets it on top of everything
        int? insertAt = _sprite.LayerMapTryGet((ent.Owner, sprite), NextSlot, out var nextSlot, false) ? nextSlot : null;
        index = _sprite.AddLayer((ent.Owner, sprite), pump, insertAt);
        _sprite.LayerMapSet((ent.Owner, sprite), LayerKey, index);
        ent.Comp.RevealedLayers.Add(LayerKey);

        TryPlayEmerge((ent.Owner, ent.Comp, sprite), index);

        // Drawn for a human chest, then moved and warped onto the host's body shape the same way their armor is,
        // so it stays on the upper chest of shorter or differently shaped species
        if (!TryComp<InventoryComponent>(ent, out var inventory))
            return;

        if (_inventory.TryGetSlot(ent.Owner, Slot, out var slot, inventory))
            _sprite.LayerSetOffset((ent.Owner, sprite), index, slot.Offset);

        if (GetDisplacement(ent.Owner, inventory) is { } displacement)
            _displacement.TryAddDisplacement(displacement, sprite, index, LayerKey, ent.Comp.RevealedLayers);
    }

    /// <summary>
    /// Plays the pump emerging once, unless it finished emerging before the host came into view.
    /// </summary>
    private void TryPlayEmerge(Entity<SymbioteChemicalPumpHostComponent, SpriteComponent> ent, int index)
    {
        if (ent.Comp1.EmergeState is not { } emerge
            || _sprite.LayerGetEffectiveRsi((ent.Owner, ent.Comp2), index) is not { } rsi
            || !rsi.TryGetState(emerge, out var state))
            return;

        var length = TimeSpan.FromSeconds(state.AnimationLength);
        if (_timing.CurTime - ent.Comp1.StartTime >= length)
            return;

        // The animation only starts on its next update, so this keeps the finished pump from flashing for a frame first
        _sprite.LayerSetRsiState((ent.Owner, ent.Comp2), index, emerge);

        var animation = new Animation
        {
            Length = length,
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick
                {
                    LayerKey = LayerKey,
                    KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(new RSI.StateId(emerge), 0f) },
                },
            },
        };

        _animation.Play(ent.Owner, animation, EmergeAnimationKey);
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
