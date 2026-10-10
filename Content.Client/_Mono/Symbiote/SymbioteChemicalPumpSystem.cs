using Content.Client.DisplacementMap;
using Content.Shared._Mono.Symbiote;
using Content.Shared._Mono.Symbiote.Components;
using Content.Shared.DisplacementMap;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Mono.Symbiote;

/// <summary>
/// Draws the Chemical Pump on its host and plays its sounds.
/// Everything is worked out from the pump's networked times once per frame, after prediction has settled,
/// so prediction removing and re-adding the pump, or anyone seeing the host late, can't make it play twice or out of time.
/// </summary>
public sealed class SymbioteChemicalPumpSystem : SharedSymbioteChemicalPumpSystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
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

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        var pumps = EntityQueryEnumerator<SymbioteChemicalPumpHostComponent, SpriteComponent>();
        while (pumps.MoveNext(out var uid, out var pump, out var sprite))
        {
            var visuals = EnsureComp<SymbioteChemicalPumpVisualsComponent>(uid);
            UpdatePump((uid, pump, sprite, visuals));
        }

        // Pumps that are gone
        var stale = EntityQueryEnumerator<SymbioteChemicalPumpVisualsComponent, SpriteComponent>();
        while (stale.MoveNext(out var uid, out var visuals, out var sprite))
        {
            if (HasComp<SymbioteChemicalPumpHostComponent>(uid))
                continue;

            foreach (var key in visuals.RevealedLayers)
            {
                _sprite.RemoveLayer((uid, sprite), key);
            }

            RemCompDeferred<SymbioteChemicalPumpVisualsComponent>(uid);
        }
    }

    private void UpdatePump(Entity<SymbioteChemicalPumpHostComponent, SpriteComponent, SymbioteChemicalPumpVisualsComponent> ent)
    {
        var (uid, pump, sprite, visuals) = ent;
        if (pump.Sprite is not SpriteSpecifier.Rsi { RsiState: var pumpState } pumpSprite)
            return;

        if (!_sprite.LayerMapTryGet((uid, sprite), LayerKey, out var index, false))
            index = AddLayer(ent, pumpSprite);

        if (!_sprite.TryGetLayer((uid, sprite), index, out var layer, false)
            || _sprite.LayerGetEffectiveRsi((uid, sprite), index) is not { } rsi)
            return;

        var now = _timing.CurTime;

        // Going away, like retracting or bursting
        if (pump.EndStart is { } endStart)
        {
            if (visuals.EndSoundFor != endStart)
            {
                visuals.EndSoundFor = endStart;
                if (now < pump.EndTime)
                    _audio.PlayEntity(pump.EndSound, Filter.Local(), uid, false);
            }

            ShowOnce((uid, sprite), layer, rsi, pump.EndState, (float)(now - endStart).TotalSeconds);
            visuals.LastFrame = -1;
            return;
        }

        var sinceStart = (float)(now - pump.StartTime).TotalSeconds;
        var emergeLength = 0f;

        // Emerging
        if (pump.EmergeState is { } emerge && rsi.TryGetState(emerge, out var emergeState))
        {
            emergeLength = emergeState.AnimationLength;

            if (sinceStart < emergeLength)
            {
                if (visuals.EmergeSoundFor != pump.StartTime)
                {
                    visuals.EmergeSoundFor = pump.StartTime;
                    _audio.PlayEntity(pump.EmergeSound, Filter.Local(), uid, false);
                }

                Show((uid, sprite), layer, emerge, sinceStart);
                visuals.LastFrame = -1;
                return;
            }
        }

        // Beating, in time with when it finished emerging, so everyone sees and hears the same beat
        if (!rsi.TryGetState(pumpState, out var state) || state.AnimationLength <= 0f)
            return;

        Show((uid, sprite), layer, pumpState, (sinceStart - emergeLength) % state.AnimationLength);

        var frame = layer.AnimationFrame;
        if (frame == pump.BeatFrame && visuals.LastFrame != frame)
            _audio.PlayEntity(pump.BeatSound, Filter.Local(), uid, false);

        visuals.LastFrame = frame;
    }

    /// <summary>
    /// Shows a state on the pump's layer at a set point in its animation.
    /// </summary>
    private void Show(Entity<SpriteComponent> sprite, SpriteComponent.Layer layer, string state, float time)
    {
        if (layer.State != state)
            _sprite.LayerSetRsiState(layer, state);

        _sprite.LayerSetAutoAnimated(layer, false);
        _sprite.LayerSetAnimationTime(layer, time);
        _sprite.LayerSetVisible(layer, true);
    }

    /// <summary>
    /// Shows a state that plays once, hiding the layer when it's done instead of looping.
    /// </summary>
    private void ShowOnce(Entity<SpriteComponent> sprite, SpriteComponent.Layer layer, RSI rsi, string? state, float time)
    {
        if (state == null || !rsi.TryGetState(state, out var rsiState) || time >= rsiState.AnimationLength)
        {
            _sprite.LayerSetVisible(layer, false);
            return;
        }

        Show(sprite, layer, state, time);
    }

    /// <summary>
    /// Adds the pump's layer, placed and shaped for the host's body.
    /// </summary>
    /// <param name="ent">The host.</param>
    /// <param name="pump">What the pump looks like.</param>
    /// <returns>The index of the pump's layer.</returns>
    private int AddLayer(Entity<SymbioteChemicalPumpHostComponent, SpriteComponent, SymbioteChemicalPumpVisualsComponent> ent, SpriteSpecifier pump)
    {
        var (uid, _, spriteComp, visuals) = ent;
        var sprite = (uid, spriteComp);

        // Anything without that layer, like a body with different layers, just gets it on top of everything
        int? insertAt = _sprite.LayerMapTryGet(sprite, NextSlot, out var nextSlot, false) ? nextSlot : null;
        var index = _sprite.AddLayer(sprite, pump, insertAt);
        _sprite.LayerMapSet(sprite, LayerKey, index);
        visuals.RevealedLayers.Add(LayerKey);

        // Drawn for a human chest, then moved and warped onto the host's body shape the same way their armor is,
        // so it stays on the upper chest of shorter or differently shaped species
        if (TryComp<InventoryComponent>(uid, out var inventory))
        {
            if (_inventory.TryGetSlot(uid, Slot, out var slot, inventory))
                _sprite.LayerSetOffset(sprite, index, slot.Offset);

            if (GetDisplacement(uid, inventory) is { } displacement)
                _displacement.TryAddDisplacement(displacement, spriteComp, index, LayerKey, visuals.RevealedLayers);
        }

        // The displacement map goes in front of it, which moves it up
        return _sprite.LayerMapGet(sprite, LayerKey);
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
