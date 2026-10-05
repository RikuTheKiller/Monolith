using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Mono.ChemistryTrial;

/// <summary>
/// An amount of a reagent in a recipe, e.g. "1 Carbon".
/// </summary>
public readonly record struct ChemistryTrialAmount(string Name, FixedPoint2 Amount, bool Catalyst = false);

/// <summary>
/// A reaction as it is presented in the chemistry time trial.
/// </summary>
public sealed class ChemistryTrialRecipe
{
    public required ProtoId<ReactionPrototype> Reaction { get; init; }

    /// <summary>
    /// The products of the reaction with the amount made, including recipe indices for products made by several recipes,
    /// e.g. "4 Ephedrine (R2)".
    /// </summary>
    public required string Label { get; init; }

    public required IReadOnlyList<ChemistryTrialAmount> Reactants { get; init; }
    public required IReadOnlyList<ChemistryTrialAmount> Products { get; init; }
    public required float MinTemperature { get; init; }
    public required float MaxTemperature { get; init; }

    /// <summary>
    /// Localized mixing methods required by the reaction, if any.
    /// </summary>
    public required IReadOnlyList<string> Mixing { get; init; }

    /// <summary>
    /// Reagent groups of the products. The recipe appears in the time trial if any of these is enabled.
    /// </summary>
    public required IReadOnlySet<string> Categories { get; init; }
}

/// <summary>
/// Every reaction that can appear in the chemistry time trial, along with the logic for checking answers.
/// Built identically on the client and the server so that answers can be predicted.
/// </summary>
public sealed class ChemistryTrialCatalog
{
    /// <summary>
    /// Margin of error for comparing numbers typed by the user, to forgive floating point noise.
    /// </summary>
    private const double Tolerance = 0.001;

    public readonly IReadOnlyList<ChemistryTrialRecipe> Recipes;
    public readonly IReadOnlyList<string> Categories;

    private readonly Dictionary<ProtoId<ReactionPrototype>, ChemistryTrialRecipe> _byReaction;

    private ChemistryTrialCatalog(List<ChemistryTrialRecipe> recipes)
    {
        Recipes = recipes;
        Categories = recipes.SelectMany(r => r.Categories).Distinct().Order(StringComparer.Ordinal).ToList();
        _byReaction = recipes.ToDictionary(r => r.Reaction);
    }

    public static ChemistryTrialCatalog Build(IPrototypeManager protoMan)
    {
        string NameOf(string reagent)
        {
            return TitleCase(protoMan.TryIndex<ReagentPrototype>(reagent, out var proto) ? proto.LocalizedName : reagent);
        }

        // Answers are typed by name, so reactions where several reagents share a name cannot be answered.
        bool NamesAreDistinct(IEnumerable<string> reagents)
        {
            var names = reagents.Select(r => Normalize(NameOf(r))).ToList();
            return names.Distinct().Count() == names.Count;
        }

        // Ordinal ordering keeps the recipe indices stable and identical on the client and the server.
        var reactions = protoMan.EnumeratePrototypes<ReactionPrototype>()
            .Where(r => r.Products.Count > 0
                        && r.Reactants.Count > 0
                        && NamesAreDistinct(r.Reactants.Keys)
                        && NamesAreDistinct(r.Products.Keys))
            .OrderBy(r => r.ID, StringComparer.Ordinal)
            .ToList();

        // Products are grouped by their (case-insensitive) name, since that is what the user sees and types.
        var recipesPerProduct = new Dictionary<string, List<string>>();
        foreach (var reaction in reactions)
        {
            foreach (var product in reaction.Products.Keys)
            {
                var key = Normalize(NameOf(product));
                if (!recipesPerProduct.TryGetValue(key, out var list))
                    recipesPerProduct[key] = list = new List<string>();

                if (!list.Contains(reaction.ID))
                    list.Add(reaction.ID);
            }
        }

        var recipes = new List<ChemistryTrialRecipe>(reactions.Count);
        foreach (var reaction in reactions)
        {
            var labels = new List<string>();
            var categories = new HashSet<string>();
            foreach (var (product, amount) in reaction.Products)
            {
                var name = NameOf(product);
                var producers = recipesPerProduct[Normalize(name)];
                labels.Add(producers.Count > 1
                    ? Loc.GetString("chemistry-trial-recipe-index", ("amount", amount.ToString()), ("name", name), ("index", producers.IndexOf(reaction.ID) + 1))
                    : Loc.GetString("chemistry-trial-amount", ("amount", amount.ToString()), ("name", name)));

                categories.Add(protoMan.TryIndex<ReagentPrototype>(product, out var proto) ? proto.Group : "Unknown");
            }

            var mixing = new List<string>();
            if (reaction.MixingCategories != null)
            {
                foreach (var category in reaction.MixingCategories)
                {
                    mixing.Add(protoMan.TryIndex(category, out var mixingProto)
                        ? Loc.GetString(mixingProto.VerbText)
                        : category.Id);
                }
            }

            recipes.Add(new ChemistryTrialRecipe
            {
                Reaction = reaction.ID,
                Label = string.Join(" + ", labels),
                Reactants = reaction.Reactants
                    .Select(r => new ChemistryTrialAmount(NameOf(r.Key), r.Value.Amount, r.Value.Catalyst))
                    .ToList(),
                Products = reaction.Products
                    .Select(p => new ChemistryTrialAmount(NameOf(p.Key), p.Value))
                    .ToList(),
                MinTemperature = reaction.MinimumTemperature,
                MaxTemperature = reaction.MaximumTemperature,
                Mixing = mixing,
                Categories = categories,
            });
        }

        return new ChemistryTrialCatalog(recipes);
    }

    public bool TryGetRecipe(ProtoId<ReactionPrototype>? reaction, [NotNullWhen(true)] out ChemistryTrialRecipe? recipe)
    {
        recipe = null;
        return reaction != null && _byReaction.TryGetValue(reaction.Value, out recipe);
    }

    /// <summary>
    /// The recipes that can appear in a time trial with the given categories disabled, in a stable order.
    /// </summary>
    public List<ChemistryTrialRecipe> GetEligible(IReadOnlySet<string> disabledCategories)
    {
        return Recipes.Where(r => r.Categories.Any(c => !disabledCategories.Contains(c))).ToList();
    }

    #region Checking

    /// <summary>
    /// Checks an answer to a question about <paramref name="recipe"/>.
    /// </summary>
    public static bool IsCorrect(ChemistryTrialRecipe recipe, ChemistryTrialDirection direction, string amounts, string temperature)
    {
        return direction switch
        {
            ChemistryTrialDirection.ProductToRecipe =>
                AmountsMatch(recipe.Reactants, amounts) && TemperatureMatches(recipe, temperature),
            ChemistryTrialDirection.RecipeToProduct =>
                AmountsMatch(recipe.Products, amounts),
            _ => false,
        };
    }

    /// <summary>
    /// Whether the typed amounts are exactly the expected ones, in any order and regardless of case.
    /// </summary>
    public static bool AmountsMatch(IReadOnlyList<ChemistryTrialAmount> expected, string text)
    {
        if (!TryParseAmounts(text, out var parsed) || parsed.Count != expected.Count)
            return false;

        foreach (var amount in expected)
        {
            if (!parsed.TryGetValue(Normalize(amount.Name), out var typed)
                || Math.Abs(typed - amount.Amount.Double()) > Tolerance)
                return false;
        }

        return true;
    }

    public static bool TemperatureMatches(ChemistryTrialRecipe recipe, string text)
    {
        if (!TryParseTemperature(text, out var min, out var max))
            return false;

        if (Math.Abs(min - recipe.MinTemperature) > Tolerance)
            return false;

        if (float.IsPositiveInfinity(recipe.MaxTemperature) || float.IsPositiveInfinity(max))
            return float.IsPositiveInfinity(recipe.MaxTemperature) && float.IsPositiveInfinity(max);

        return Math.Abs(max - recipe.MaxTemperature) <= Tolerance;
    }

    private static readonly Regex AmountRegex = new(
        @"^(?<amount>\d+(?:\.\d+)?|\.\d+)\s*[ux]?\s+(?<name>.+?)(?:\s*\(catalyst\))?$",
        RegexOptions.IgnoreCase);

    /// <summary>
    /// Parses text like "1 Carbon + 1 Welding Fuel, 1 Hydrogen" into amounts keyed by normalized reagent name.
    /// Fails on malformed parts or duplicate reagents.
    /// </summary>
    public static bool TryParseAmounts(string text, out Dictionary<string, double> amounts)
    {
        amounts = new Dictionary<string, double>();
        foreach (var part in text.Split('+', ','))
        {
            var match = AmountRegex.Match(part.Trim());
            if (!match.Success
                || !Parse.TryDouble(match.Groups["amount"].Value, NumberStyles.AllowDecimalPoint, out var amount)
                || !amounts.TryAdd(Normalize(match.Groups["name"].Value), amount))
            {
                amounts.Clear();
                return false;
            }
        }

        return amounts.Count > 0;
    }

    private const string Number = @"(\d+(?:\.\d+)?)";

    private static readonly Regex RangeRegex = new($@"^{Number}-(?:{Number}|inf)?$");
    private static readonly Regex MinimumRegex = new($@"^(?:>=|≥|>){Number}$|^{Number}\+$");
    private static readonly Regex MaximumRegex = new($@"^(?:<=|≤|<|-){Number}$");

    /// <summary>
    /// Parses a temperature range in kelvin. Accepted forms:
    /// "375-621", "375+", "375-", "375-inf", ">=375", "&lt;=621", "-621", and "" or "any" for no requirement.
    /// </summary>
    public static bool TryParseTemperature(string text, out float min, out float max)
    {
        min = 0f;
        max = float.PositiveInfinity;

        var normalized = Regex.Replace(text.ToLowerInvariant(), @"\s+", string.Empty)
            .Replace("∞", "inf")
            .TrimEnd('k');

        if (normalized is "" or "any")
            return true;

        if (RangeRegex.Match(normalized) is { Success: true } range)
        {
            min = ParseFloat(range.Groups[1].Value);
            if (range.Groups[2].Success)
                max = ParseFloat(range.Groups[2].Value);
            return true;
        }

        if (MinimumRegex.Match(normalized) is { Success: true } minimum)
        {
            min = ParseFloat(minimum.Groups[1].Success ? minimum.Groups[1].Value : minimum.Groups[2].Value);
            return true;
        }

        if (MaximumRegex.Match(normalized) is { Success: true } maximum)
        {
            max = ParseFloat(maximum.Groups[1].Value);
            return true;
        }

        return false;
    }

    private static float ParseFloat(string text)
    {
        return Parse.Float(text);
    }

    /// <summary>
    /// Lowercases and collapses whitespace so that names are compared case-insensitively.
    /// </summary>
    public static string Normalize(string name)
    {
        return Regex.Replace(name.Trim(), @"\s+", " ").ToLowerInvariant();
    }

    #endregion

    #region Formatting

    /// <summary>
    /// Capitalizes the first letter of every word, e.g. "welding fuel" to "Welding Fuel".
    /// Unlike <see cref="TextInfo.ToTitleCase"/>, the rest of each word is left alone.
    /// </summary>
    public static string TitleCase(string name)
    {
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (i == 0 || char.IsWhiteSpace(chars[i - 1]))
                chars[i] = char.ToUpperInvariant(chars[i]);
        }

        return new string(chars);
    }

    public static string FormatAmounts(IEnumerable<ChemistryTrialAmount> amounts)
    {
        return string.Join(" + ", amounts.Select(a => a.Catalyst
            ? Loc.GetString("chemistry-trial-amount-catalyst", ("amount", a.Amount.ToString()), ("name", a.Name))
            : Loc.GetString("chemistry-trial-amount", ("amount", a.Amount.ToString()), ("name", a.Name))));
    }

    public static string FormatTemperature(ChemistryTrialRecipe recipe)
    {
        var min = recipe.MinTemperature.ToString("0.##", CultureInfo.InvariantCulture);
        var max = recipe.MaxTemperature.ToString("0.##", CultureInfo.InvariantCulture);

        if (float.IsPositiveInfinity(recipe.MaxTemperature))
        {
            return recipe.MinTemperature <= 0f
                ? Loc.GetString("chemistry-trial-temperature-any")
                : Loc.GetString("chemistry-trial-temperature-minimum", ("min", min));
        }

        return Loc.GetString("chemistry-trial-temperature-range", ("min", min), ("max", max));
    }

    #endregion
}
