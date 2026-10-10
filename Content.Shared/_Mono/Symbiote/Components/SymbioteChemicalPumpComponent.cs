using Content.Shared.Damage.Prototypes;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Mono.Symbiote.Components;

/// <summary>
/// Marks a symbiote ability as Chemical Pump, which continuously heals the host and can bring them back from the dead.
/// The pump sits on top of the host's chest with its own health, taking any physical damage the host gets hit with before their armor does.
/// The ability ends if its health runs out.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class SymbioteChemicalPumpComponent : Component
{
    /// <summary>
    /// What the pump looks like on the host's chest.
    /// </summary>
    [DataField(required: true)]
    public SpriteSpecifier Sprite = default!;

    /// <summary>
    /// The state in the sprite's RSI that plays once as the pump emerges, before the sprite's own state takes over.
    /// </summary>
    [DataField]
    public string? EmergeState;

    /// <summary>
    /// How long the pump takes to emerge, which should match how long the emerge state plays for.
    /// The server can't read sprites, so it has to be given here.
    /// </summary>
    [DataField]
    public TimeSpan EmergeDuration = TimeSpan.FromSeconds(0.4);

    /// <summary>
    /// The state in the sprite's RSI that plays once as the pump retracts after the ability ends.
    /// </summary>
    [DataField]
    public string? RetractState;

    /// <summary>
    /// How long the pump takes to retract, which should match how long the retract state plays for.
    /// The server can't read sprites, so it has to be given here.
    /// </summary>
    [DataField]
    public TimeSpan RetractDuration = TimeSpan.FromSeconds(0.4);

    /// <summary>
    /// The state in the sprite's RSI that plays once as the pump bursts apart from running out of health.
    /// Without one, it just retracts.
    /// </summary>
    [DataField]
    public string? BurstState;

    /// <summary>
    /// How long the pump takes to burst, which should match how long the burst state plays for.
    /// The server can't read sprites, so it has to be given here.
    /// </summary>
    [DataField]
    public TimeSpan BurstDuration = TimeSpan.FromSeconds(0.85);

    /// <summary>
    /// The sound of the pump emerging.
    /// </summary>
    [DataField]
    public SoundSpecifier? EmergeSound;

    /// <summary>
    /// The sound of one beat, played every time the sprite's own state reaches <see cref="BeatFrame"/>.
    /// </summary>
    [DataField]
    public SoundSpecifier? BeatSound;

    /// <summary>
    /// The frame of the sprite's own state that the pump beats on, like where it swells.
    /// </summary>
    [DataField]
    public int BeatFrame;

    /// <summary>
    /// The sound of the pump retracting.
    /// </summary>
    [DataField]
    public SoundSpecifier? RetractSound;

    /// <summary>
    /// The sound of the pump bursting apart.
    /// </summary>
    [DataField]
    public SoundSpecifier? BurstSound;

    /// <summary>
    /// How much healing the pump has to spend per second, before effectiveness.
    /// </summary>
    [DataField]
    public float Healing = 12.5f;

    /// <summary>
    /// What the pump heals and how effectively, from most effective to least.
    /// Healing goes to the most effective tier first, and only what's left over reaches the next.
    /// </summary>
    [DataField(required: true)]
    public List<SymbioteChemicalPumpTier> Tiers = new();

    /// <summary>
    /// The name of the host's blood solution, read directly so the client can predict how much blood is missing.
    /// </summary>
    [DataField]
    public string BloodSolution = "bloodstream";

    /// <summary>
    /// The damage groups that hurt the pump when the host takes them.
    /// </summary>
    [DataField]
    public List<ProtoId<DamageGroupPrototype>> PhysicalGroups = new() { "Brute", "Burn" };

    /// <summary>
    /// How much health a fresh pump starts with.
    /// </summary>
    [DataField]
    public float MaxHealth = 50f;

    /// <summary>
    /// How much health the pump regenerates per second.
    /// </summary>
    [DataField]
    public float HealthRegen = 10f;

    /// <summary>
    /// How much health the pump had at <see cref="HealthTime"/>, before regenerating since then.
    /// Kept this way so it only changes when the pump is hit, and anyone can work out how much it has now.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Health;

    /// <summary>
    /// When the pump had <see cref="Health"/>.
    /// </summary>
    [DataField, AutoNetworkedField, AutoPausedField]
    public TimeSpan HealthTime;
}

/// <summary>
/// Things the Chemical Pump heals equally effectively.
/// </summary>
[DataDefinition]
public sealed partial class SymbioteChemicalPumpTier
{
    /// <summary>
    /// How much of the healing spent on this tier actually heals.
    /// </summary>
    [DataField(required: true)]
    public float Effectiveness;

    /// <summary>
    /// The damage types in this tier.
    /// </summary>
    [DataField]
    public List<ProtoId<DamageTypePrototype>> Types = new();

    /// <summary>
    /// Whether lost blood is in this tier, measured as a percentage of maximum blood volume.
    /// </summary>
    [DataField]
    public bool Blood;
}
