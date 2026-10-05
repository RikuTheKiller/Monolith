using Robust.Shared.Serialization;

namespace Content.Shared._Mono.ChemistryTrial;

[Serializable, NetSerializable]
public enum ChemistryTrialUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class ChemistryTrialSetDurationMessage(TimeSpan duration) : BoundUserInterfaceMessage
{
    public readonly TimeSpan Duration = duration;
}

[Serializable, NetSerializable]
public sealed class ChemistryTrialSetRevealDurationMessage(TimeSpan duration) : BoundUserInterfaceMessage
{
    public readonly TimeSpan Duration = duration;
}

[Serializable, NetSerializable]
public sealed class ChemistryTrialSetCategoryMessage(string category, bool enabled) : BoundUserInterfaceMessage
{
    public readonly string Category = category;
    public readonly bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class ChemistryTrialSetAllCategoriesMessage(bool enabled) : BoundUserInterfaceMessage
{
    public readonly bool Enabled = enabled;
}

[Serializable, NetSerializable]
public sealed class ChemistryTrialStartMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ChemistryTrialAbortMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ChemistryTrialDismissMessage : BoundUserInterfaceMessage;

[Serializable, NetSerializable]
public sealed class ChemistryTrialAnswerMessage(string amounts, string temperature) : BoundUserInterfaceMessage
{
    /// <summary>
    /// Reactants or products depending on the question, e.g. "1 Carbon + 1 Welding Fuel + 1 Hydrogen".
    /// </summary>
    public readonly string Amounts = amounts;

    /// <summary>
    /// Temperature range, e.g. "375-621". Ignored for questions that ask for products.
    /// </summary>
    public readonly string Temperature = temperature;
}
