using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Help;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class HelpCatalogTests
{
    [Fact]
    public void EnglishCatalogCoversEveryReleasedCommandAndRequiredTerms()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        var commands = catalog.Entries.Where(entry => entry.Kind == HelpEntryKind.Command).ToList();

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
            "proposal", "dry-run", "assessment", "baseline", "preflight", "drift",
            "default-selection", "overview", "walkthrough", "text-coverage",
        }, code => Assert.NotNull(catalog.Find(HelpEntryKind.Term, code)));
        Assert.Empty(catalog.ValidateLinks());
    }

    [Fact]
    public void OverviewHelpDoesNotTreatAggregateStandingAsEveryAnalysisOpinion()
    {
        var catalog = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        var page = catalog.GetHelpPage(HelpEntryKind.Guide, "overview")!;

        Assert.DoesNotContain("has no saved project opinion for that analysis", page, StringComparison.Ordinal);
        Assert.Contains("Unknown and Disapproved analyses", page, StringComparison.Ordinal);
        Assert.Contains("PanGloss matched an analysis FieldWorks marked Disapproved", page, StringComparison.Ordinal);
        Assert.Contains("shows recorded comparison detail beneath it", page, StringComparison.Ordinal);
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
            "Overview is the first page when you open a project. It gathers stored results and project counts " +
            "so you can see where things stand without starting a new run.",
            overview.Description);
        Assert.StartsWith("# Overview", overview.HelpPage, StringComparison.Ordinal);
        Assert.Equal("agents/start-here", agent.Slug);
        Assert.Equal(
            HelpCatalog.SiteRoot + "/guide/agents/start-here/",
            agent.Url);
        Assert.Equal("learn/index", learnIndex.Slug);
        Assert.Equal("https://motif-docs.pages.dev/learn/", learnIndex.Url);
        Assert.Contains("# Teach a dumb computer your language", learnIndex.HelpPage, StringComparison.Ordinal);
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
}
