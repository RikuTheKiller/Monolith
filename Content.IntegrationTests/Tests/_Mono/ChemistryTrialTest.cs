using System.Collections.Generic;
using System.Linq;
using Content.Shared._Mono.ChemistryTrial;
using Robust.Shared.GameObjects;
using ClientTrialSystem = Content.Client._Mono.ChemistryTrial.ChemistryTrialSystem;
using ServerTrialSystem = Content.Server._Mono.ChemistryTrial.ChemistryTrialSystem;

namespace Content.IntegrationTests.Tests._Mono;

[TestFixture]
[TestOf(typeof(SharedChemistryTrialSystem))]
public sealed class ChemistryTrialTest
{
    private const string Machine = "MonoChemistryTrialMachine";

    private static (string Amounts, string Temperature) Answer(ChemistryTrialRecipe recipe, ChemistryTrialDirection direction)
    {
        return direction == ChemistryTrialDirection.ProductToRecipe
            ? (ChemistryTrialCatalog.FormatAmounts(recipe.Reactants), ChemistryTrialCatalog.FormatTemperature(recipe))
            : (ChemistryTrialCatalog.FormatAmounts(recipe.Products), string.Empty);
    }

    private static string Doubled(IEnumerable<ChemistryTrialAmount> amounts)
    {
        return ChemistryTrialCatalog.FormatAmounts(amounts.Select(a => a with { Amount = a.Amount * 2 }));
    }

    /// <summary>
    /// Every recipe must be answerable by typing exactly what the machine reveals, in any case,
    /// and must reject the same recipe at a different scale.
    /// </summary>
    [Test]
    public async Task EveryRecipeIsAnswerable()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var catalog = entMan.System<ServerTrialSystem>().Catalog;
            Assert.That(catalog.Recipes, Is.Not.Empty);
            Assert.That(catalog.Categories, Is.Not.Empty);

            Assert.Multiple(() =>
            {
                Assert.That(catalog.Recipes.Select(r => ChemistryTrialCatalog.Normalize(r.Label)), Is.Unique,
                    "Every recipe label must be unique, otherwise questions are ambiguous.");

                foreach (var recipe in catalog.Recipes)
                {
                    var (reactants, temperature) = Answer(recipe, ChemistryTrialDirection.ProductToRecipe);
                    var (products, _) = Answer(recipe, ChemistryTrialDirection.RecipeToProduct);
                    var id = recipe.Reaction.Id;

                    Assert.That(ChemistryTrialCatalog.IsCorrect(recipe, ChemistryTrialDirection.ProductToRecipe, reactants, temperature),
                        $"{id}: '{reactants}' at '{temperature}' was rejected");
                    Assert.That(ChemistryTrialCatalog.IsCorrect(recipe, ChemistryTrialDirection.ProductToRecipe, reactants.ToUpperInvariant(), temperature),
                        $"{id}: upper case reactants were rejected");
                    Assert.That(ChemistryTrialCatalog.IsCorrect(recipe, ChemistryTrialDirection.ProductToRecipe, reactants.ToLowerInvariant(), temperature),
                        $"{id}: lower case reactants were rejected");
                    Assert.That(ChemistryTrialCatalog.IsCorrect(recipe, ChemistryTrialDirection.RecipeToProduct, products, string.Empty),
                        $"{id}: '{products}' was rejected");

                    Assert.That(ChemistryTrialCatalog.IsCorrect(recipe, ChemistryTrialDirection.ProductToRecipe, Doubled(recipe.Reactants), temperature),
                        Is.False, $"{id}: doubled reactants were accepted");
                    Assert.That(ChemistryTrialCatalog.IsCorrect(recipe, ChemistryTrialDirection.RecipeToProduct, Doubled(recipe.Products), string.Empty),
                        Is.False, $"{id}: doubled products were accepted");

                    var wrongTemperature = recipe.MinTemperature > 0 ? "any" : "1000-2000";
                    Assert.That(ChemistryTrialCatalog.IsCorrect(recipe, ChemistryTrialDirection.ProductToRecipe, reactants, wrongTemperature),
                        Is.False, $"{id}: wrong temperature was accepted");

                    if (recipe.Reactants.Count > 1)
                    {
                        var missing = ChemistryTrialCatalog.FormatAmounts(recipe.Reactants.Skip(1));
                        Assert.That(ChemistryTrialCatalog.IsCorrect(recipe, ChemistryTrialDirection.ProductToRecipe, missing, temperature),
                            Is.False, $"{id}: a missing reactant was accepted");
                    }
                }
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task OilExample()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();

        await server.WaitAssertion(() =>
        {
            var catalog = entMan.System<ServerTrialSystem>().Catalog;
            Assert.That(catalog.TryGetRecipe("Oil", out var oil));

            const ChemistryTrialDirection toRecipe = ChemistryTrialDirection.ProductToRecipe;
            const ChemistryTrialDirection toProduct = ChemistryTrialDirection.RecipeToProduct;

            Assert.Multiple(() =>
            {
                Assert.That(oil!.Label, Is.EqualTo("3 Oil"), "The label should include the amount made");
                Assert.That(ChemistryTrialCatalog.IsCorrect(oil!, toRecipe, "1 Carbon + 1 Welding Fuel + 1 Hydrogen", ""));
                Assert.That(ChemistryTrialCatalog.IsCorrect(oil!, toRecipe, "1 hydrogen,1 CARBON, 1   welding fuel", "any"));
                Assert.That(ChemistryTrialCatalog.IsCorrect(oil!, toRecipe, "2 Carbon + 2 Welding Fuel + 2 Hydrogen", ""), Is.False);
                Assert.That(ChemistryTrialCatalog.IsCorrect(oil!, toRecipe, "1 Carbon + 1 Welding Fuel + 1 Hydrogen", "375-621"), Is.False);
                Assert.That(ChemistryTrialCatalog.IsCorrect(oil!, toRecipe, "1 Carbon + 1 Carbon + 1 Hydrogen", ""), Is.False);
                Assert.That(ChemistryTrialCatalog.IsCorrect(oil!, toProduct, "3 Oil", ""));
                Assert.That(ChemistryTrialCatalog.IsCorrect(oil!, toProduct, "3 oil", ""));
                Assert.That(ChemistryTrialCatalog.IsCorrect(oil!, toProduct, "6 Oil", ""), Is.False);
            });
        });

        await pair.CleanReturnAsync();
    }

    [Test]
    [TestCase("", 0f, float.PositiveInfinity)]
    [TestCase("any", 0f, float.PositiveInfinity)]
    [TestCase("375-621", 375f, 621f)]
    [TestCase(" 375 - 621 K ", 375f, 621f)]
    [TestCase("368.15+", 368.15f, float.PositiveInfinity)]
    [TestCase("375+ K", 375f, float.PositiveInfinity)]
    [TestCase("375-", 375f, float.PositiveInfinity)]
    [TestCase("375-inf", 375f, float.PositiveInfinity)]
    [TestCase("375-∞", 375f, float.PositiveInfinity)]
    [TestCase(">=375", 375f, float.PositiveInfinity)]
    [TestCase("0-300", 0f, 300f)]
    [TestCase("<=300", 0f, 300f)]
    [TestCase("-300", 0f, 300f)]
    public void TemperatureParsing(string text, float min, float max)
    {
        Assert.That(ChemistryTrialCatalog.TryParseTemperature(text, out var parsedMin, out var parsedMax), $"'{text}' failed to parse");
        Assert.Multiple(() =>
        {
            Assert.That(parsedMin, Is.EqualTo(min).Within(0.001f));
            Assert.That(parsedMax, Is.EqualTo(max).Within(0.001f));
        });
    }

    [Test]
    [TestCase("hot")]
    [TestCase("375-621-900")]
    [TestCase("abc-621")]
    public void TemperatureParsingRejectsGarbage(string text)
    {
        Assert.That(ChemistryTrialCatalog.TryParseTemperature(text, out _, out _), Is.False);
    }

    /// <summary>
    /// Prediction relies on the client and the server deriving the exact same questions from the same seed.
    /// </summary>
    [Test]
    public async Task ClientAndServerAgreeOnQuestions()
    {
        // Client entity systems only exist while connected.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var server = pair.Server;
        var client = pair.Client;

        List<string> serverLabels = default!;
        List<string> serverQuestions = default!;
        List<string> clientLabels = default!;
        List<string> clientQuestions = default!;

        List<string> Questions(ChemistryTrialCatalog catalog)
        {
            var eligible = catalog.GetEligible(new HashSet<string>());
            return Enumerable.Range(0, eligible.Count * 2 + 5)
                .Select(i => SharedChemistryTrialSystem.GetQuestion(12345, i, eligible))
                .Select(q => $"{q.Reaction}/{q.Direction}")
                .ToList();
        }

        await server.WaitPost(() =>
        {
            var catalog = server.ResolveDependency<IEntityManager>().System<ServerTrialSystem>().Catalog;
            serverLabels = catalog.Recipes.Select(r => r.Label).ToList();
            serverQuestions = Questions(catalog);
        });

        await client.WaitPost(() =>
        {
            var catalog = client.ResolveDependency<IEntityManager>().System<ClientTrialSystem>().Catalog;
            clientLabels = catalog.Recipes.Select(r => r.Label).ToList();
            clientQuestions = Questions(catalog);
        });

        Assert.Multiple(() =>
        {
            Assert.That(clientLabels, Is.EqualTo(serverLabels));
            Assert.That(clientQuestions, Is.EqualTo(serverQuestions));
        });

        // Every recipe is asked once before any is repeated.
        var firstCycle = serverQuestions.Take(serverLabels.Count).Select(q => q.Split('/')[0]);
        Assert.That(firstCycle, Is.Unique);

        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task TimeTrialFlow()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var entMan = server.ResolveDependency<IEntityManager>();
        var map = await pair.CreateTestMap();

        EntityUid uid = default;
        await server.WaitPost(() => uid = entMan.SpawnEntity(Machine, map.GridCoords));

        Entity<ChemistryTrialComponent> Trial() => (uid, entMan.GetComponent<ChemistryTrialComponent>(uid));

        await server.WaitAssertion(() =>
        {
            var system = entMan.System<ServerTrialSystem>();
            var trial = Trial();
            var catalog = system.Catalog;

            system.SetDuration(trial, TimeSpan.FromSeconds(1));
            Assert.That(trial.Comp.Duration, Is.EqualTo(trial.Comp.MinDuration), "Duration should be clamped");
            system.SetDuration(trial, TimeSpan.FromMinutes(1) + TimeSpan.FromSeconds(30));
            Assert.That(trial.Comp.Duration, Is.EqualTo(TimeSpan.FromSeconds(90)));

            system.SetRevealDuration(trial, TimeSpan.FromSeconds(1000));
            Assert.That(trial.Comp.RevealDuration, Is.EqualTo(trial.Comp.MaxRevealDuration), "Reveal delay should be clamped");
            system.SetRevealDuration(trial, TimeSpan.FromSeconds(5));
            Assert.That(trial.Comp.RevealDuration, Is.EqualTo(TimeSpan.FromSeconds(5)));

            system.SetCategory(trial, "NotARealCategory", false);
            Assert.That(trial.Comp.DisabledCategories, Is.Empty, "Unknown categories should be ignored");

            system.SetAllCategories(trial, false);
            Assert.That(system.TryStart(trial), Is.False, "Should not start with no categories");

            var category = catalog.Categories[0];
            system.SetCategory(trial, category, true);
            Assert.That(system.TryStart(trial));
            Assert.That(trial.Comp.Phase, Is.EqualTo(ChemistryTrialPhase.Running));

            // Configuration is locked while running.
            system.SetDuration(trial, TimeSpan.FromMinutes(10));
            Assert.That(trial.Comp.Duration, Is.EqualTo(TimeSpan.FromSeconds(90)));
            system.SetRevealDuration(trial, TimeSpan.FromSeconds(1));
            Assert.That(trial.Comp.RevealDuration, Is.EqualTo(TimeSpan.FromSeconds(5)));

            // A correct answer moves on instantly.
            Assert.That(catalog.TryGetRecipe(trial.Comp.QuestionReaction, out var recipe));
            Assert.That(recipe!.Categories, Does.Contain(category));
            var (amounts, temperature) = Answer(recipe, trial.Comp.QuestionDirection);
            Assert.That(system.TryAnswer(trial, amounts, temperature), Is.True);
            Assert.That(trial.Comp.Passed, Is.EqualTo(1));
            Assert.That(trial.Comp.QuestionIndex, Is.EqualTo(1));
            Assert.That(trial.Comp.Revealing, Is.False);

            // A wrong answer reveals the recipe and blocks further answers.
            Assert.That(system.TryAnswer(trial, "1 Nonsense", ""), Is.False);
            Assert.That(trial.Comp.Failed, Is.EqualTo(1));
            Assert.That(trial.Comp.Revealing, Is.True);
            Assert.That(trial.Comp.QuestionIndex, Is.EqualTo(1));
            Assert.That(trial.Comp.LastAnswer, Does.Contain("1 Nonsense"));
            Assert.That(system.TryAnswer(trial, amounts, temperature), Is.Null);
        });

        await pair.RunSeconds(4f);
        await server.WaitAssertion(() =>
        {
            Assert.That(Trial().Comp.Revealing, Is.True, "The reveal should last the configured 5 seconds");
        });

        await pair.RunSeconds(1.5f);
        await server.WaitAssertion(() =>
        {
            var trial = Trial();
            Assert.That(trial.Comp.Revealing, Is.False);
            Assert.That(trial.Comp.QuestionIndex, Is.EqualTo(2));
            Assert.That(trial.Comp.Phase, Is.EqualTo(ChemistryTrialPhase.Running));
        });

        await pair.RunSeconds(90f);
        await server.WaitAssertion(() =>
        {
            var system = entMan.System<ServerTrialSystem>();
            var trial = Trial();
            Assert.That(trial.Comp.Phase, Is.EqualTo(ChemistryTrialPhase.Finished));
            Assert.That(trial.Comp.Passed, Is.EqualTo(1));
            Assert.That(trial.Comp.Failed, Is.EqualTo(1));

            system.Dismiss(trial);
            Assert.That(trial.Comp.Phase, Is.EqualTo(ChemistryTrialPhase.Idle));

            // The next trial gets a different question sequence.
            var previousSeed = trial.Comp.RunSeed;
            Assert.That(system.TryStart(trial));
            Assert.That(trial.Comp.RunSeed, Is.Not.EqualTo(previousSeed));
            system.Abort(trial);
            Assert.That(trial.Comp.Phase, Is.EqualTo(ChemistryTrialPhase.Idle));

            // With no delay, a wrong answer moves straight on to the next question.
            system.SetRevealDuration(trial, TimeSpan.Zero);
            Assert.That(system.TryStart(trial));
            Assert.That(system.TryAnswer(trial, "1 Nonsense", ""), Is.False);
            Assert.That(trial.Comp.Failed, Is.EqualTo(1));
            Assert.That(trial.Comp.Revealing, Is.False);
            Assert.That(trial.Comp.QuestionIndex, Is.EqualTo(1));
            system.Abort(trial);
        });

        await pair.CleanReturnAsync();
    }
}
