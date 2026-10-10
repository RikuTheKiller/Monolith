using System.Diagnostics.CodeAnalysis;
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
    private const string EndAnimationKey = "symbiote-chemical-pump-end";

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
        _animation.Stop(ent.Owner, null, EndAnimationKey);

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

    protected override void OnEnding(Entity<SymbioteChemicalPumpHostComponent> ent)
    {
        UpdateLayer(ent);
    }

    private void UpdateLayer(Entity<SymbioteChemicalPumpHostComponent> ent)
    {
        if (ent.Comp.Sprite is not { } pump || !TryComp<SpriteComponent>(ent, out var sprite))
            return;

        if (!_sprite.LayerMapTryGet((ent.Owner, sprite), LayerKey, out var index, false))
            index = AddLayer((ent.Owner, ent.Comp, sprite), pump);

        if (ent.Comp.EndTime != null)
        {
            TryPlayEnd((ent.Owner, ent.Comp, sprite), index);
            return;
        }

        // Started again before the old one was gone, which only some clients see as a whole new pump.
        // It's still showing how the old one went away, rather than the pump itself or it emerging.
        // Resetting to an older state while predicting it going away doesn't count, since that pump isn't new.
        if (pump is SpriteSpecifier.Rsi { RsiState: var pumpState }
            && _sprite.TryGetLayer((ent.Owner, sprite), LayerKey, out var layer, false)
            && layer.State != pumpState
            && layer.State != ent.Comp.EmergeState
            && ShouldEmerge((ent.Owner, ent.Comp, sprite), index, out _, out _))
        {
            _animation.Stop(ent.Owner, null, EndAnimationKey);
            TryPlayEmerge((ent.Owner, ent.Comp, sprite), index);
        }
    }

    /// <summary>
    /// Adds the pump's layer, placed and shaped for the host's body, and starts it emerging if it's new.
    /// </summary>
    /// <param name="ent">The host.</param>
    /// <param name="pump">What the pump looks like.</param>
    /// <returns>The index of the pump's layer.</returns>
    private int AddLayer(Entity<SymbioteChemicalPumpHostComponent, SpriteComponent> ent, SpriteSpecifier pump)
    {
        var sprite = (ent.Owner, ent.Comp2);

        // Anything without that layer, like a body with different layers, just gets it on top of everything
        int? insertAt = _sprite.LayerMapTryGet(sprite, NextSlot, out var nextSlot, false) ? nextSlot : null;
        var index = _sprite.AddLayer(sprite, pump, insertAt);
        _sprite.LayerMapSet(sprite, LayerKey, index);
        ent.Comp1.RevealedLayers.Add(LayerKey);

        if (ent.Comp1.EndTime == null)
            TryPlayEmerge(ent, index);

        // Drawn for a human chest, then moved and warped onto the host's body shape the same way their armor is,
        // so it stays on the upper chest of shorter or differently shaped species
        if (TryComp<InventoryComponent>(ent, out var inventory))
        {
            if (_inventory.TryGetSlot(ent.Owner, Slot, out var slot, inventory))
                _sprite.LayerSetOffset(sprite, index, slot.Offset);

            if (GetDisplacement(ent.Owner, inventory) is { } displacement)
                _displacement.TryAddDisplacement(displacement, ent.Comp2, index, LayerKey, ent.Comp1.RevealedLayers);
        }

        // The displacement map goes in front of it, which moves it up
        return _sprite.LayerMapGet(sprite, LayerKey);
    }

    /// <summary>
    /// Plays the pump going away once, like retracting or bursting.
    /// It's removed when it's done, so it's left on the last frame until then.
    /// </summary>
    private void TryPlayEnd(Entity<SymbioteChemicalPumpHostComponent, SpriteComponent> ent, int index)
    {
        if (_animation.HasRunningAnimation(ent.Owner, EndAnimationKey)
            || ent.Comp1.EndState is not { } end
            || _sprite.LayerGetEffectiveRsi((ent.Owner, ent.Comp2), index) is not { } rsi
            || !rsi.TryGetState(end, out var state))
            return;

        _animation.Stop(ent.Owner, null, EmergeAnimationKey);
        _sprite.LayerSetRsiState((ent.Owner, ent.Comp2), index, end);
        _animation.Play(ent.Owner, OnceAnimation(end, state.AnimationLength), EndAnimationKey);
    }

    /// <summary>
    /// Plays the pump emerging once, unless it finished emerging before the host came into view.
    /// </summary>
    private void TryPlayEmerge(Entity<SymbioteChemicalPumpHostComponent, SpriteComponent> ent, int index)
    {
        if (!ShouldEmerge(ent, index, out var emerge, out var length))
            return;

        // The animation only starts on its next update, so this keeps the finished pump from flashing for a frame first
        _sprite.LayerSetRsiState((ent.Owner, ent.Comp2), index, emerge);
        _animation.Play(ent.Owner, OnceAnimation(emerge, length), EmergeAnimationKey);
    }

    /// <summary>
    /// Whether the pump is new enough that it should still be emerging.
    /// </summary>
    /// <param name="ent">The host.</param>
    /// <param name="index">The index of the pump's layer.</param>
    /// <param name="emerge">The state that plays as it emerges.</param>
    /// <param name="length">How long it takes to emerge, in seconds.</param>
    private bool ShouldEmerge(
        Entity<SymbioteChemicalPumpHostComponent, SpriteComponent> ent,
        int index,
        [NotNullWhen(true)] out string? emerge,
        out float length)
    {
        emerge = ent.Comp1.EmergeState;
        length = 0f;

        if (emerge == null
            || _sprite.LayerGetEffectiveRsi((ent.Owner, ent.Comp2), index) is not { } rsi
            || !rsi.TryGetState(emerge, out var state))
            return false;

        length = state.AnimationLength;
        return _timing.CurTime - ent.Comp1.StartTime < TimeSpan.FromSeconds(length);
    }

    /// <summary>
    /// Plays a state on the pump's layer once, instead of looping it.
    /// </summary>
    private static Animation OnceAnimation(string state, float length)
    {
        return new Animation
        {
            Length = TimeSpan.FromSeconds(length),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick
                {
                    LayerKey = LayerKey,
                    KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(new RSI.StateId(state), 0f) },
                },
            },
        };
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
