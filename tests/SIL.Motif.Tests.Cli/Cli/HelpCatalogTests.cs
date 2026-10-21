using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SIL.Motif.Cli;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Help;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Trait("MotifTestLevel", "Unit")]
public sealed class HelpCatalogTests
{
    [Fact]
    public void HelpTextNormalizesWindowsLineEndings()
    {
        Assert.Equal("# Title\n\nText\nMore", HelpCatalog.NormalizeLineEndings("# Title\r\n\r\nText\rMore"));
    }

    [Fact]
    public void ApplyAllPendingHelpSynopsisMatchesTheCliVerbCatalog()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        var help = Assert.IsType<HelpEntry>(catalog.Find(HelpEntryKind.Command, "apply --all-pending"));
        var example = Assert.Single(help.HelpPage.Split('\n')
            .Where(line => line.StartsWith("motif apply --all-pending", StringComparison.Ordinal)));
        var usage = Assert.Single(CliVerbCatalog.All
            .Single(verb => verb.CommandName == "apply --all-pending").UsageLines);

        Assert.Equal("motif " + usage.Replace("<fwdata>", "<project>", StringComparison.Ordinal), example);
    }

    [Fact]
    public void EveryGuideTopicNamedByACommandIsAnEncodingGuide()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        var named = catalog.Entries.Where(entry => entry.Kind == HelpEntryKind.Command && entry.Guides is not null).ToArray();

        Assert.NotEmpty(named);
        foreach (var entry in named)
        {
            Assert.InRange(entry.Guides!.Count, 1, 3);
            foreach (var topic in entry.Guides)
                Assert.NotNull(catalog.Find(HelpEntryKind.Guide, "encoding/" + topic));
        }
    }

    [Fact]
    public void EveryCommandWithGuidesAlsoHasAShortRedirectLine()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        foreach (var entry in catalog.Entries.Where(entry => entry.Kind == HelpEntryKind.Command && entry.Guides is not null))
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Redirect), $"'{entry.Code}' has guides but no redirect.");
            Assert.InRange(entry.Redirect!.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, 8, 40);
        }
    }

    // Composers that change no grammar rule or lexical form, so no phenomenon guide applies.
    private static readonly IReadOnlyDictionary<string, string> ComposersWithoutGuides = new Dictionary<string, string>
    {
        ["compose-author-text"] = "adds a text to analyse, not grammar",
        ["compose-delete-empty-sense"] = "removes an empty sense, which encodes no phenomenon",
        ["compose-record-parsimony-disposition"] = "records a review decision about a Parsimony finding",
    };

    [Fact]
    public void EveryGrammarComposerNamesAnEncodingGuideUnlessAllowListed()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        var composers = catalog.Entries.Where(entry => entry.Kind == HelpEntryKind.Command
            && (entry.Code.StartsWith("compose", StringComparison.Ordinal)
                || entry.Code is "order-allomorphs" or "retire-allomorph" or "retire-redundant-zero-affix" or "new")).ToArray();

        Assert.Contains(composers, entry => entry.Code == "compose-author-phonological-rule");
        foreach (var entry in composers)
        {
            if (ComposersWithoutGuides.ContainsKey(entry.Code))
                Assert.Null(entry.Guides);
            else
                Assert.True(entry.Guides is { Count: > 0 }, $"'{entry.Code}' names no encoding guide.");
        }
    }

    [Fact]
    public void EveryTypedComposerFieldHasAHelpDescriptionAndNoDescriptionIsOrphaned()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        foreach (var composer in SIL.Motif.Commands.ProposalCommands.ComposerDescriptors)
        {
            var entry = Assert.IsType<HelpEntry>(catalog.Find(HelpEntryKind.Command, composer.Verb));
            var described = Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(entry.Fields);
            var expected = composer.FieldNames.Append("draft").Order(StringComparer.Ordinal);
            Assert.Equal(expected, described.Keys.Order(StringComparer.Ordinal));
            Assert.All(described, pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), pair.Key));
        }
    }

    [Fact]
    public void ATypedComposerVerbAppearsOnceInTheCliVerbCatalogWithTheSharedUsage()
    {
        foreach (var composer in SIL.Motif.Commands.ProposalCommands.ComposerDescriptors)
        {
            var verb = Assert.Single(CliVerbCatalog.All, descriptor => descriptor.CommandName == composer.Verb);
            Assert.StartsWith(composer.Verb + " --draft <name> --project <fwdata> --intent '<closed intent JSON>'",
                Assert.Single(verb.UsageLines), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EnglishCatalogCoversEveryReleasedCommandAndRequiredTerms()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        var commands = catalog.Entries.Where(entry => entry.Kind == HelpEntryKind.Command &&
            CommandCatalog.All.Any(command => command.Surface == CommandSurface.Released &&
                command.Name == entry.Code)).ToList();

        Assert.Equal("en", catalog.Locale);
        Assert.Equal(
            CommandCatalog.All.Where(command => command.Surface == CommandSurface.Released)
                .Select(command => command.Name).Order(StringComparer.Ordinal),
            commands.Select(entry => entry.Code).Order(StringComparer.Ordinal));
        Assert.All(commands, entry =>
        {
            Assert.InRange(entry.Title.Length, 1, 30);
            Assert.False(string.IsNullOrWhiteSpace(entry.Description));
            var sentenceCount = Regex.Matches(entry.Description, @"[.!?](?:\s|$)").Count;
            Assert.InRange(sentenceCount, 1, 3);
            Assert.NotEqual(Normalize(entry.Title), Normalize(entry.Description));
            Assert.False(string.IsNullOrWhiteSpace(entry.HelpPage));
        });
        Assert.All(new[]
        {
            "proposal", "trial", "dry-run", "assessment", "baseline", "preflight", "drift",
            "default-selection", "overview", "walkthrough", "text-coverage", "phonology-examples",
        }, code => Assert.NotNull(catalog.Find(HelpEntryKind.Term, code)));
        Assert.Empty(catalog.ValidateLinks());
    }

    [Fact]
    public void AdvancedAiParsimonyCommandsHaveCompleteHelpEntries()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));

        foreach (var code in new[]
                 {
                     "parsimony", "parsimony --wait", "parsimony show", "parsimony latest", "parsimony record-types",
                     "parsimony dispose", "parsimony revise", "parsimony retract",
                     "compose-record-parsimony-disposition", "retire-allomorph",
                     "retire-redundant-zero-affix",
                     "compose-author-phonological-rule",
                     "compose-author-affix-slot",
                     "compose-author-affix-template",
                     "compose-author-lexical-entry",
                     "compose-author-alternate-form",
                     "compose-edit-allomorph-condition",
                     "compose-edit-part-of-speech-inflection-class-default",
                     "compose-edit-affix-inflection-classes",
                     "compose-edit-stem-allomorph-stem-name",
                     "compose-edit-stem-inflection-class",
                     "compose-edit-compound-rule",
                     "compose-set-no-default-compounding",
                     "compose-rewrite-lexeme-form",
                     "order-allomorphs",
                     "compose-author-text",
                     "compose-edit-analysis-decision",
                     "compose-batch",
                 })
        {
            var entry = Assert.IsType<HelpEntry>(catalog.Find(HelpEntryKind.Command, code));
            Assert.InRange(entry.Title.Length, 1, 30);
            Assert.False(string.IsNullOrWhiteSpace(entry.Description));
            Assert.False(string.IsNullOrWhiteSpace(entry.HelpPage));
        }
    }

    [Fact]
    public void DraftWorkflowAndApplyHelpCoversPromotedCommandsAndReadinessBoundaries()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));

        foreach (var code in new[] { "label", "proposal rationale", "proposal note", "apply" })
        {
            var entry = Assert.IsType<HelpEntry>(catalog.Find(HelpEntryKind.Command, code));
            Assert.InRange(entry.Title.Length, 1, 30);
            Assert.False(string.IsNullOrWhiteSpace(entry.Description));
            Assert.False(string.IsNullOrWhiteSpace(entry.HelpPage));
        }

        var releasedApply = catalog.GetHelpPage(HelpEntryKind.Command, "apply --proposal")!;
        Assert.Contains("apply.not-ready", releasedApply, StringComparison.Ordinal);
        Assert.Contains("no stems", releasedApply, StringComparison.Ordinal);
        Assert.Contains("`--force` does not skip Readiness here", releasedApply, StringComparison.Ordinal);

        var plainApply = catalog.GetHelpPage(HelpEntryKind.Command, "apply")!;
        Assert.Contains("`--force` skips the Readiness reasons", plainApply, StringComparison.Ordinal);
        Assert.Contains("does not bypass checks", plainApply, StringComparison.Ordinal);
    }

    [Fact]
    public void DraftCreationBatchAndFinalizeHelpRequireBothReviewFields()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));

        foreach (var code in new[] { "new", "compose-batch", "finalize" })
        {
            var entry = Assert.IsType<HelpEntry>(catalog.Find(HelpEntryKind.Command, code));
            Assert.Contains("label", entry.Description, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Rationale", entry.Description, StringComparison.OrdinalIgnoreCase);
            var page = catalog.GetHelpPage(HelpEntryKind.Command, code)!;
            Assert.Contains("label", page, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Rationale", page, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void AffixAuthoringHelpDescribesConditionsOnEachForm()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        var entry = Assert.IsType<HelpEntry>(catalog.Find(HelpEntryKind.Command, "compose-author-lexical-entry"));
        var entryPage = catalog.GetHelpPage(HelpEntryKind.Command, "compose-author-lexical-entry")!;
        Assert.Contains("inflection classes it attaches to", entry.Description, StringComparison.Ordinal);
        Assert.Contains("Empty lists, repeated ids", entryPage, StringComparison.Ordinal);

        var alternate = Assert.IsType<HelpEntry>(catalog.Find(HelpEntryKind.Command, "compose-author-alternate-form"));
        var alternatePage = catalog.GetHelpPage(HelpEntryKind.Command, "compose-author-alternate-form")!;
        Assert.InRange(alternate.Title.Length, 1, 30);
        Assert.Contains("own environments", alternate.Description, StringComparison.Ordinal);
        Assert.Contains("none are copied", alternatePage, StringComparison.Ordinal);
        Assert.Contains("placement", alternatePage, StringComparison.Ordinal);
    }

    [Fact]
    public void AllomorphHelpExplainsSharedReferencesAndElsewhereOrder()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));

        var condition = catalog.GetHelpPage(HelpEntryKind.Command, "compose-edit-allomorph-condition")!;
        Assert.Contains("every other user keeps its reference", condition, StringComparison.Ordinal);
        Assert.Contains("ordered list", condition, StringComparison.Ordinal);
        var order = catalog.GetHelpPage(HelpEntryKind.Command, "order-allomorphs")!;
        Assert.Contains("Earlier matching alternate forms take precedence", order, StringComparison.Ordinal);
        Assert.Contains("elsewhere fallback", order, StringComparison.Ordinal);
        Assert.Contains("lexeme form", order, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadingOverviewHelpDoesNotTreatAggregateStandingAsEveryAnalysisOpinion()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        var page = catalog.GetHelpPage(HelpEntryKind.Guide, "reading-the-overview")!;

        Assert.DoesNotContain("has no saved project opinion for that analysis", page, StringComparison.Ordinal);
        Assert.Contains("Unknown and Disapproved analyses", page, StringComparison.Ordinal);
        Assert.Contains("PanGloss matched an analysis marked Disapproved", page, StringComparison.Ordinal);
        Assert.Contains("shows the recorded comparison detail", page, StringComparison.Ordinal);
    }

    [Fact]
    public void TextsHelpExplainsSpecificMixedOpinionsAndIncompleteSearches()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        var page = catalog.GetHelpPage(HelpEntryKind.Guide, "texts")!;

        Assert.Contains("Unknown × Different", page, StringComparison.Ordinal);
        Assert.Contains("PanGloss matched an analysis FieldWorks marked Disapproved", page, StringComparison.Ordinal);
        Assert.Contains("shows recorded comparison detail beneath it", page, StringComparison.Ordinal);
        Assert.Contains("Every word row uses the meaning named by its Matrix cell", page, StringComparison.Ordinal);
        Assert.Contains("does not establish that an analysis was not built", page, StringComparison.Ordinal);
    }

    [Fact]
    public void AssessHelpDescribesPerWordComparisonEvidenceAndAvailability()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        var page = catalog.GetHelpPage(HelpEntryKind.Command, "assess")!;

        foreach (var text in new[] { "headline", "qualification", "comparison", "meaningCode", "availability",
            "Available", "RecordedGradesOnly", "Unavailable", "matched analysis identities", "individual opinions" })
            Assert.Contains(text, page, StringComparison.Ordinal);
        Assert.Contains("incomplete search", page, StringComparison.Ordinal);
    }

    [Fact]
    public void HandoffHelpMatchesTheIndependentFileSpecification()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        var commandPage = catalog.GetHelpPage(HelpEntryKind.Command, "handoff")!;
        var agentPage = catalog.GetHelpPage(HelpEntryKind.Guide, "agents/handoff")!;

        AssertInOrder(commandPage, HandoffFileExpectations.Assessed.Select(file => file.RelativePath));
        AssertInOrder(agentPage, HandoffFileExpectations.Assessed.Select(file => file.RelativePath));
        AssertInOrder(agentPage, HandoffFileExpectations.BaselineOnly.Select(file => file.RelativePath));
        AssertInOrder(agentPage, HandoffFileExpectations.OneWordTrace.Select(file => file.RelativePath));
        foreach (var file in HandoffFileExpectations.Assessed
                     .Concat(HandoffFileExpectations.OneWordTrace)
                     .DistinctBy(file => file.RelativePath))
            Assert.Contains($"| `{file.RelativePath}` | {file.Description} |", agentPage, StringComparison.Ordinal);
        Assert.Contains("omits `parse-results.json`", commandPage, StringComparison.Ordinal);
        Assert.Contains("does not interpret", agentPage, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingLocaleUsesEnglishMetadataAndHelpPages()
    {
        var english = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        var spanish = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("es-MX"));
        var guideKind = HelpEntryKind.Guide;

        Assert.Equal("es", spanish.Locale);
        Assert.Equal(
            english.Find(HelpEntryKind.Command, "assess")?.Title,
            spanish.Find(HelpEntryKind.Command, "assess")?.Title);
        Assert.StartsWith("https://motif-docs.pages.dev/es/", spanish.Find(HelpEntryKind.Command, "assess")!.Url);
        Assert.Equal(
            english.GetHelpPage(HelpEntryKind.Command, "assess"),
            spanish.GetHelpPage(HelpEntryKind.Command, "assess"));
        Assert.Equal(
            english.Find(guideKind, "agents/start-here")?.Title,
            spanish.Find(guideKind, "agents/start-here")?.Title);
        Assert.Equal(
            english.Find(guideKind, "agents/start-here")?.Description,
            spanish.Find(guideKind, "agents/start-here")?.Description);
        Assert.Equal(
            english.GetHelpPage(guideKind, "agents/start-here"),
            spanish.GetHelpPage(guideKind, "agents/start-here"));
        Assert.Equal(
            HelpCatalog.SiteRoot + "/es/guide/agents/start-here/",
            spanish.Find(guideKind, "agents/start-here")?.Url);
    }

    private static string Normalize(string value) =>
        Regex.Replace(value, @"[^\p{L}\p{N}]", string.Empty).ToUpperInvariant();

    [Fact]
    public void UrlsUseTheStableCommandAndTermRoutes()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));

        Assert.Equal(
            HelpCatalog.SiteRoot + "/reference/commands/config-show/",
            catalog.BuildUrl(HelpEntryKind.Command, "config show"));
        Assert.Equal(
            HelpCatalog.SiteRoot + "/reference/terms/default-selection/",
            catalog.BuildUrl(HelpEntryKind.Term, "default-selection"));
        Assert.Equal(
            HelpCatalog.SiteRoot + "/reference/controls/OverviewPage/",
            HelpCatalog.BuildUrl("en", HelpEntryKind.Ui, "OverviewPage"));
        Assert.Equal(
            HelpCatalog.SiteRoot + "/es/learn/",
            HelpCatalog.BuildUrl("es", HelpEntryKind.Guide, "learn/index"));
        Assert.Equal(
            HelpCatalog.SiteRoot + "/guide/walkthroughs/open-project-overview/",
            catalog.BuildWalkthroughUrl("open-project-overview"));
    }

    [Fact]
    public void GuideCatalogKeepsHierarchicalCodesAndDerivesMetadataFromMarkdown()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        var guideKind = HelpEntryKind.Guide;
        var overview = Assert.IsType<HelpEntry>(catalog.Find(guideKind, "overview"));
        var agent = Assert.IsType<HelpEntry>(catalog.Find(guideKind, "agents/start-here"));
        var learnIndex = Assert.IsType<HelpEntry>(catalog.Find(guideKind, "learn/index"));

        Assert.Equal("Overview", overview.Title);
        Assert.Equal(
            "Overview is the first page you see. It shows the results of your last run: how many of your " +
            "words parse, how many words with approved analyses were rebuilt exactly, which words " +
            "are slow, and what the grammar check found. Opening it doesn't parse anything. Nothing here " +
            "changes your FieldWorks project.",
            overview.Description);
        Assert.StartsWith("# Overview", overview.HelpPage, StringComparison.Ordinal);
        Assert.Equal("agents/start-here", agent.Slug);
        Assert.Equal(
            HelpCatalog.SiteRoot + "/guide/agents/start-here/",
            agent.Url);
        Assert.Equal("learn/index", learnIndex.Slug);
        Assert.Equal("https://motif-docs.pages.dev/learn/", learnIndex.Url);
        Assert.Contains("# Help the parser work with your language", learnIndex.HelpPage, StringComparison.Ordinal);
        Assert.Equal(
            catalog.Entries.Count,
            catalog.Entries.Select(entry => (entry.Kind, entry.Code)).Distinct().Count());
    }

    [Fact]
    public void LearnGuidesKeepLanguageEducationWithoutMotifAuthoredParserAdvice()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        var modelling = catalog.GetHelpPage(
            HelpEntryKind.Guide, "learn/modelling-a-grammar-the-parser-can-use");
        var parser = catalog.GetHelpPage(HelpEntryKind.Guide, "learn/what-the-parser-knows");

        Assert.DoesNotContain("cannot fully use", modelling, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("first phoneme set", modelling, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("A slot with no usable affix disappears", modelling, StringComparison.Ordinal);
        Assert.DoesNotContain("reports inflectional affixes that have no slot", modelling,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("does not yet run some things FieldWorks lets you model", modelling,
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("silently repair a rule", parser, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("## Allomorph order", modelling, StringComparison.Ordinal);
        Assert.Contains("Features, their values, and inflection classes link by reference, not by name:",
            modelling, StringComparison.Ordinal);
        Assert.Contains("[Timing](cmd:timing)", modelling, StringComparison.Ordinal);
        Assert.Contains("choose **Refresh**", modelling, StringComparison.Ordinal);
    }

    [Fact]
    public void PartialGuideTranslationsFallBackToEnglishForEachMissingField()
    {
        var englishPages = new[]
        {
            new KeyValuePair<string, string>("overview", "# Overview\n\nEnglish summary.\n\nEnglish body."),
            new KeyValuePair<string, string>("agents/start-here", "# Agent guide\n\nEnglish agent summary.\n\nEnglish body."),
            new KeyValuePair<string, string>("learn/index", "# Learn\n\nEnglish index summary.\n\nEnglish body."),
        };
        var localizedPages = new[]
        {
            new KeyValuePair<string, string>("overview", "Resumen localizado.\n\nCuerpo traducido."),
            new KeyValuePair<string, string>("agents/start-here", "# Guía del agente\n\n## Secciones"),
        };

        var entries = HelpCatalog.BuildGuideEntries("es", englishPages, localizedPages);
        var overview = Assert.Single(entries, entry => entry.Code == "overview");
        var agent = Assert.Single(entries, entry => entry.Code == "agents/start-here");
        var learnIndex = Assert.Single(entries, entry => entry.Code == "learn/index");

        Assert.Equal("Overview", overview.Title);
        Assert.Equal("Resumen localizado.", overview.Description);
        Assert.Equal("Resumen localizado.\n\nCuerpo traducido.", overview.HelpPage);
        Assert.Equal("Guía del agente", agent.Title);
        Assert.Equal("English agent summary.", agent.Description);
        Assert.Equal("# Guía del agente\n\n## Secciones", agent.HelpPage);
        Assert.Equal("Learn", learnIndex.Title);
        Assert.Equal("English index summary.", learnIndex.Description);
        Assert.Contains("English body", learnIndex.HelpPage, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateGuideCodesAreRejected()
    {
        var duplicatePages = new[]
        {
            new KeyValuePair<string, string>("agents/start-here", "# Agent guide\n\nAgent summary."),
            new KeyValuePair<string, string>("agents/start-here", "# Duplicate\n\nDuplicate summary."),
        };

        Assert.Throws<InvalidDataException>(() =>
            HelpCatalog.BuildGuideEntries("en", duplicatePages, Array.Empty<KeyValuePair<string, string>>()));
    }

    [Fact]
    public void GuideLinksValidateHierarchyEscapingAndUnknownTargets()
    {
        var entries = new[]
        {
            new HelpEntry(HelpEntryKind.Command, "config show", "config-show", "Config", "", "test", null, ""),
            new HelpEntry(HelpEntryKind.Guide, "agents/start-here", "agents/start-here", "Agent guide", "", "test", null, ""),
            new HelpEntry(
                HelpEntryKind.Guide,
                "overview",
                "overview",
                "Overview",
                "",
                "test",
                "[Agent guide](guide:agents/start-here) [Config](cmd:config%20show) [Missing](guide:missing)",
                ""),
        };

        var error = Assert.Single(HelpCatalog.ValidateLinks(entries));

        Assert.Equal("guide 'overview' links to missing guide 'missing'.", error);
    }

    private static void AssertInOrder(string page, IEnumerable<string> paths)
    {
        var previous = -1;
        foreach (var path in paths)
        {
            var current = page.IndexOf($"`{path}`", previous + 1, StringComparison.Ordinal);
            Assert.True(current > previous, $"The Help page is missing or misorders `{path}`.");
            previous = current;
        }
    }
}
