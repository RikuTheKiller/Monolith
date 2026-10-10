using Content.Client.DisplacementMap;
using Content.Shared._Mono.Symbiote;
using Content.Shared._Mono.Symbiote.Components;
using Content.Shared.DisplacementMap;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Robust.Client.Animations;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Client._Mono.Symbiote;

/// <summary>
/// Draws the Chemical Pump on its host, playing whatever its state calls for whenever it changes, like doors do.
/// </summary>
public sealed class SymbioteChemicalPumpSystem : SharedSymbioteChemicalPumpSystem
{
    [Dependency] private AnimationPlayerSystem _animation = default!;
    [Dependency] private AppearanceSystem _appearance = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private DisplacementMapSystem _displacement = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private SpriteSystem _sprite = default!;

    private const string LayerKey = "symbiote-chemical-pump";
    private const string AnimationKey = "symbiote-chemical-pump";

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

        SubscribeLocalEvent<SymbioteChemicalPumpHostComponent, AppearanceChangeEvent>(OnAppearanceChange);
    }

    private void OnAppearanceChange(Entity<SymbioteChemicalPumpHostComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null || ent.Comp.Sprite is not { } pump)
            return;

        if (!_appearance.TryGetData<SymbioteChemicalPumpState>(ent, SymbioteChemicalPumpVisuals.State, out var state, args.Component))
            state = SymbioteChemicalPumpState.None;

        // The host's appearance changes for all sorts of reasons, like taking damage, and those shouldn't restart the pump's animation
        if (state == ent.Comp.ShownState)
            return;

        ent.Comp.ShownState = state;

        var sprite = (ent.Owner, args.Sprite);
        if (!_sprite.LayerMapTryGet(sprite, LayerKey, out var index, false))
        {
            if (state == SymbioteChemicalPumpState.None)
                return;

            index = AddLayer(ent, args.Sprite, pump);
        }

        if (_animation.HasRunningAnimation(ent, AnimationKey))
            _animation.Stop(ent.Owner, AnimationKey);

        ent.Comp.LastFrame = -1;
        _sprite.LayerSetVisible(sprite, index, state != SymbioteChemicalPumpState.None);

        switch (state)
        {
            case SymbioteChemicalPumpState.Emerging:
                PlayOnce(ent, args.Sprite, index, ent.Comp.EmergeState);
                break;
            case SymbioteChemicalPumpState.Active:
                _sprite.LayerSetSprite(sprite, index, pump);
                _sprite.LayerSetAutoAnimated(sprite, index, true);
                break;
            case SymbioteChemicalPumpState.Retracting:
                PlayOnce(ent, args.Sprite, index, ent.Comp.RetractState);
                break;
            case SymbioteChemicalPumpState.Bursting:
                PlayOnce(ent, args.Sprite, index, ent.Comp.BurstState);
                break;
        }
    }

    /// <summary>
    /// Plays a state on the pump's layer once from the start, holding the last frame until the pump's state changes again.
    /// </summary>
    private void PlayOnce(EntityUid host, SpriteComponent sprite, int index, string? state)
    {
        if (state == null
            || _sprite.LayerGetEffectiveRsi((host, sprite), index) is not { } rsi
            || !rsi.TryGetState(state, out var rsiState))
            return;

        // The animation only starts on its next update, so this keeps the last state from flashing for a frame first
        _sprite.LayerSetRsiState((host, sprite), index, state);

        var animation = new Animation
        {
            Length = TimeSpan.FromSeconds(rsiState.AnimationLength),
            AnimationTracks =
            {
                new AnimationTrackSpriteFlick
                {
                    LayerKey = LayerKey,
                    KeyFrames = { new AnimationTrackSpriteFlick.KeyFrame(new RSI.StateId(state), 0f) },
                },
            },
        };

        _animation.Play(host, animation, AnimationKey);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);

        // The pump beats whenever it swells, so the sound always matches what it looks like
        var query = EntityQueryEnumerator<SymbioteChemicalPumpHostComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var pump, out var sprite))
        {
            if (GetState(uid) != SymbioteChemicalPumpState.Active
                || !_sprite.TryGetLayer((uid, sprite), LayerKey, out var layer, false))
            {
                pump.LastFrame = -1;
                continue;
            }

            var frame = layer.AnimationFrame;
            if (frame == pump.BeatFrame && pump.LastFrame != frame)
                _audio.PlayEntity(pump.BeatSound, Filter.Local(), uid, false);

            pump.LastFrame = frame;
        }
    }

    /// <summary>
    /// Adds the pump's layer, placed and shaped for the host's body.
    /// </summary>
    /// <param name="host">The host.</param>
    /// <param name="sprite">The host's sprite.</param>
    /// <param name="pump">What the pump looks like.</param>
    /// <returns>The index of the pump's layer.</returns>
    private int AddLayer(EntityUid host, SpriteComponent sprite, SpriteSpecifier pump)
    {
        // Anything without that layer, like a body with different layers, just gets it on top of everything
        int? insertAt = _sprite.LayerMapTryGet((host, sprite), NextSlot, out var nextSlot, false) ? nextSlot : null;
        var index = _sprite.AddLayer((host, sprite), pump, insertAt);
        _sprite.LayerMapSet((host, sprite), LayerKey, index);

        // Drawn for a human chest, then moved and warped onto the host's body shape the same way their armor is,
        // so it stays on the upper chest of shorter or differently shaped species
        if (TryComp<InventoryComponent>(host, out var inventory))
        {
            if (_inventory.TryGetSlot(host, Slot, out var slot, inventory))
                _sprite.LayerSetOffset((host, sprite), index, slot.Offset);

            // The pump stays on the host for good, so its layers never need removing and don't have to be kept track of
            if (GetDisplacement(host, inventory) is { } displacement)
                _displacement.TryAddDisplacement(displacement, sprite, index, LayerKey, new HashSet<string>());
        }

        // The displacement map goes in front of it, which moves it up
        return _sprite.LayerMapGet((host, sprite), LayerKey);
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
