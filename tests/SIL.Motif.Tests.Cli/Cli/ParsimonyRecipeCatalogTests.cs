using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Help;
using SIL.Motif.Host.Parsimony;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class ParsimonyRecipeCatalogTests
{
    [Fact]
    public void EveryRecipeHasADistinctShortTitleThatReadsAsAPhrase()
    {
        var recipes = ParsimonyRecipeCatalog.Load().Recipes;

        Assert.All(recipes, recipe =>
        {
            Assert.False(string.IsNullOrWhiteSpace(recipe.ShortTitle), recipe.MeasureId);
            Assert.True(recipe.ShortTitle.Length <= 60, recipe.MeasureId);
            Assert.Equal(recipe.ShortTitle.ToLowerInvariant(), recipe.ShortTitle);
            Assert.False(recipe.ShortTitle.EndsWith('.'), recipe.MeasureId);
        });
        Assert.Equal(recipes.Count, recipes.Select(recipe => recipe.ShortTitle).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ItemNamesComeOnlyFromObjectsTheFactsCanName()
    {
        var named = Finding("named", ["guid-a", "guid-b"]);
        var unnamed = Finding("unnamed", ["guid-c"]);
        var describe = Describe(("guid-a", "ka-"));

        Assert.Equal(["ka-"], ParsimonyReportProducer.ItemNamesFor(named, describe));
        Assert.Equal(Array.Empty<string>(), ParsimonyReportProducer.ItemNamesFor(unnamed, describe));
    }

    [Fact]
    public void ItemNamesNeverFallBackToARawIdentity()
    {
        var finding = Finding("no-refs", []) with
        {
            AttachesTo = new ParsimonyFindingAttachment(ParsimonyAttachmentKind.AuthoredObject,
                "00000000-0000-0000-0000-00000000000d", ParsimonyAuthoredObjectKind.AdhocProhibition),
        };

        Assert.Empty(ParsimonyReportProducer.ItemNamesFor(finding, _ => null));
    }

    private static Func<string, string?> Describe(params (string Guid, string Name)[] names) =>
        guid => names.Where(item => item.Guid == guid).Select(item => item.Name).FirstOrDefault();

    private static ParsimonyFinding Finding(string id, string[] guids) => new(
        id, "P-adhoc-duplicate", ParsimonyAxis.Parsimony, ParsimonyTier.Static,
        new ParsimonyFindingAttachment(ParsimonyAttachmentKind.AuthoredObject, "00000000-0000-0000-0000-00000000000e",
            ParsimonyAuthoredObjectKind.AdhocProhibition),
        null, new ParsimonyMeasureNumber(1, 2, "prohibitions"),
        new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.GreaterThan, 0, "v1"), "sha256:" + new string('a', 64),
        [.. guids.Select(guid => new ParsimonyEvidenceReference("bundle/x", "adhoc-context",
            JsonDocument.Parse("{\"objectGuid\":\"" + guid + "\"}").RootElement))],
        "guide:parsimony/recipes/P-adhoc-duplicate", [], ParsimonyVerification.NotRun);

    [Fact]
    public void ManifestResourceLookupMatchesWindowsSeparatorNames()
    {
        const string windowsResourceName = @"help\en\guide\parsimony\parsimony-recipes.json";

        Assert.Equal(windowsResourceName, ManifestResourceLookup.FindPhysicalName(
            [windowsResourceName], "help/en/guide/parsimony/parsimony-recipes.json"));
    }

    [Fact]
    public void EveryCatalogMeasureHasOneCompleteEmbeddedRecipe()
    {
        var catalog = ParsimonyRecipeCatalog.Load();
        var help = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));

        Assert.Equal(1, catalog.FormatVersion);
        Assert.Equal(MeasureCatalog.All.Select(measure => measure.Id).Order(StringComparer.Ordinal),
            catalog.Recipes.Select(recipe => recipe.MeasureId).Order(StringComparer.Ordinal));
        Assert.Equal(catalog.Recipes.Count,
            catalog.Recipes.Select(recipe => recipe.MeasureId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(catalog.Recipes, recipe =>
        {
            var measure = MeasureCatalog.Find(recipe.MeasureId)!;
            Assert.InRange(recipe.Title.Length, 1, 30);
            Assert.Equal($"guide/parsimony/recipes/{recipe.MeasureId}.md", recipe.HelpPath);
            Assert.Equal($"guide:parsimony/recipes/{recipe.MeasureId}", recipe.RecipeLink);
            Assert.Equal(measure.RequiredCapabilities, recipe.RequiredCapabilities);
            Assert.Equal(measure.Axis == ParsimonyAxis.Both
                    ? ["parsimony", "restrictiveness"]
                    : [JsonSerializer.Serialize(measure.Axis).Trim('"')],
                recipe.Metadata.Axes);
            Assert.Equal(JsonSerializer.Serialize(measure.AttachesTo).Trim('"'), recipe.Metadata.AttachesTo);
            Assert.Equal(measure.AuthoredObjectKind is null ? null : JsonSerializer.Serialize(measure.AuthoredObjectKind).Trim('"'),
                recipe.Metadata.AuthoredObjectKind);
            Assert.Equal(measure.GroupKind is null ? null : JsonSerializer.Serialize(measure.GroupKind).Trim('"'),
                recipe.Metadata.GroupKind);
            Assert.Contains($"# {recipe.Title}\n", recipe.Page, StringComparison.Ordinal);
            Assert.Equal(recipe.Page,
                help.GetHelpPage(HelpEntryKind.Guide, $"parsimony/recipes/{recipe.MeasureId}"));
            Assert.Equal(recipe.Sources.Count, recipe.Sources.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(measure.CitationKeys, recipe.Metadata.CitationKeys);
            AssertRecipeHeadings(recipe.Page);
            if (recipe.MeasureId is "P-allo-duplicate-form" or "P-statement-unused")
            {
                Assert.Contains("Engineering check: exact redundancy in the authored grammar; no linguistic claim.",
                    recipe.Page, StringComparison.Ordinal);
            }
        });
        Assert.Null(catalog.Find("P-unknown"));
    }

    [Fact]
    public void RecipeUpdateIntentsNameOnlySemanticActionsInTheSharedCommandCatalog()
    {
        var catalog = ParsimonyRecipeCatalog.Load();
        var availableIntents = CommandCatalog.All
            .Select(command => command.RequestType.Name)
            .Where(name => name.StartsWith("Compose", StringComparison.Ordinal) &&
                           name.EndsWith("Request", StringComparison.Ordinal))
            .Select(name => name["Compose".Length..^"Request".Length])
            .ToHashSet(StringComparer.Ordinal);
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["P-adhoc-duplicate"] = ["EditAdhocProhibition"],
            ["R-word-negative-accepted"] = ["EditAffixTemplate"],
            ["R-word-disapproved-produced"] = ["EditAllomorphCondition"],
            ["B-affix-unslotted"] = ["AuthorAffixSlot", "AuthorAffixTemplate", "EditInflectionalAffix"],
            ["R-tmpl-precedence"] = ["AuthorAffixTemplate", "EditAffixTemplate"],
            ["R-slot-blocking"] = ["EditAffixSlot"],
            ["R-allo-unconditioned"] = ["EditAllomorphCondition", "OrderAllomorphs"],
            ["R-env-broad"] = ["AuthorEnvironment", "EditAllomorphCondition"],
            ["R-nc-excess"] = ["AuthorNaturalClass", "EditNaturalClass", "RelinkNaturalClass"],
            ["P-allo-alternation-family"] = ["AuthorPhonologicalRule", "RetireAllomorph"],
            ["B-adhoc-is-slot-order"] =
                ["AuthorAffixSlot", "AuthorAffixTemplate", "EditAdhocProhibition", "EditAffixTemplate",
                    "EditInflectionalAffix"],
            ["B-affix-null-vs-optional"] = ["RetireRedundantZeroAffix"],
            ["P-allo-duplicate-form"] = ["RetireAllomorph"],
            ["P-statement-unused"] = [],
        };

        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), catalog.Recipes.Select(recipe => recipe.MeasureId));
        Assert.All(catalog.Recipes, recipe =>
        {
            Assert.Equal(expected[recipe.MeasureId], recipe.Metadata.UpdateIntents);
            Assert.All(recipe.Metadata.UpdateIntents, intent => Assert.Contains(intent, availableIntents));
        });
    }

    [Fact]
    public void MissingRecipeCapabilityIsReportedByName()
    {
        var catalog = ParsimonyRecipeCatalog.Load();
        var recipe = catalog.Find("B-affix-null-vs-optional")!;

        Assert.Contains("retire-redundant-zero-affix-intent", recipe.RequiredCapabilities);
        Assert.Contains("retire-redundant-zero-affix-intent",
            MeasureCatalog.MissingCapabilities(recipe.MeasureId, ["loaded-zero-realizations"]));
    }

    [Fact]
    public void NaturalClassRecipeNamesScopedActionsAndNonProbabilisticVerification()
    {
        var recipe = ParsimonyRecipeCatalog.Load().Find("R-nc-excess")!;

        Assert.Contains("EditNaturalClass", recipe.Page, StringComparison.Ordinal);
        Assert.Contains("AuthorNaturalClass", recipe.Page, StringComparison.Ordinal);
        Assert.Contains("RelinkNaturalClass", recipe.Page, StringComparison.Ordinal);
        Assert.Contains("not a probability", recipe.Page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Use Review changes to inspect what the edit would write, then compare parser results from the current and edited scratch copies for the same words.",
            recipe.Page, StringComparison.Ordinal);
        Assert.False(Regex.IsMatch(recipe.Page, @"\b(assess\w*|candidate\w*|reject\w*|violation\w*)\b",
            RegexOptions.IgnoreCase));
        Assert.Contains("held-out", recipe.Page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RelinkNaturalClass", recipe.Metadata.UpdateIntents);
    }

    [Fact]
    public void AdhocSlotOrderRecipeNamesActionsVerificationAndGroupedFactLimits()
    {
        var page = ParsimonyRecipeCatalog.Load().GetPage("B-adhoc-is-slot-order")!;

        foreach (var action in new[]
                 {
                     "EditAffixTemplate", "AuthorAffixTemplate", "AuthorAffixSlot",
                     "EditInflectionalAffix", "EditAdhocProhibition",
                 })
            Assert.Contains(action, page, StringComparison.Ordinal);
        Assert.Contains("Dry Run", page, StringComparison.Ordinal);
        Assert.Contains("parser runs from before and after the edit", page, StringComparison.Ordinal);
        Assert.Contains("every existing Approved reading remains", page, StringComparison.Ordinal);
        Assert.Contains("reviewed negative and Disapproved swap stays unparsed", page, StringComparison.Ordinal);
        Assert.Contains("group names, members, or rationale", page, StringComparison.Ordinal);
        Assert.Contains("`adhoc_groups` is incomplete", page, StringComparison.Ordinal);
        Assert.Contains("Keep confirmed adjacency-only, Anywhere, multi-target, person-hierarchy", page,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NullOptionalRecipeNamesWholeGraphActionKeepReasonAndParserComparison()
    {
        var page = ParsimonyRecipeCatalog.Load().GetPage("B-affix-null-vs-optional")!;

        Assert.Contains("RetireRedundantZeroAffix", page, StringComparison.Ordinal);
        Assert.Contains("record a `keep` disposition with a reason", page, StringComparison.Ordinal);
        Assert.Contains("whole entry graph", page, StringComparison.Ordinal);
        Assert.Contains("ordinary primary prefix or suffix with no alternate forms", page, StringComparison.Ordinal);
        Assert.Contains("last primary form", page, StringComparison.Ordinal);
        Assert.Contains("compare parser results from the current and edited scratch copies", page,
            StringComparison.Ordinal);
        Assert.Contains("reviewed negative", page, StringComparison.Ordinal);
        Assert.Contains("Do not stage a zero-retirement change", page, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryMeasureGroundingEntryResolvesToAFullReference()
    {
        var catalog = ParsimonyRecipeCatalog.Load();
        var references = ParsimonyReferenceCatalog.Load();
        Assert.Equal(MeasureCatalog.All.SelectMany(measure => measure.CitationKeys)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
            references.References.Select(reference => reference.Key).Order(StringComparer.Ordinal));

        Assert.All(MeasureCatalog.All, measure =>
        {
            var recipe = Assert.Single(catalog.Recipes, item => item.MeasureId == measure.Id);
            var headings = recipe.Page.Split('\n', StringSplitOptions.TrimEntries)
                .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
                .Select(line => line[3..])
                .ToArray();
            Assert.Equal("Grounding", headings[^1]);
            var keys = Regex.Matches(recipe.Page,
                    @"^- \*\*\[(?<key>[a-z0-9-]+)\]\*\*", RegexOptions.Multiline)
                .Cast<Match>()
                .Select(match => match.Groups["key"].Value)
                .ToArray();

            Assert.NotEmpty(keys);
            Assert.Equal(measure.CitationKeys, keys);
            Assert.All(keys, key =>
            {
                var reference = Assert.IsType<ParsimonyReference>(references.Find(key));
                Assert.Contains(reference.Url, recipe.Page, StringComparison.Ordinal);
            });
        });
    }

    private static void AssertRecipeHeadings(string page)
    {
        var headings = page.Split('\n', StringSplitOptions.TrimEntries)
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line[3..])
            .ToArray();

        Assert.Equal(["Analyse", "Disposition", "Ask the linguist", "Update", "Verify", "Grounding"], headings);
    }
}
