using System;
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
            "default-selection", "overview", "walkthrough",
        }, code => Assert.NotNull(catalog.Find(HelpEntryKind.Term, code)));
        Assert.Empty(catalog.ValidateLinks());
    }

    [Fact]
    public void MissingLocaleUsesEnglishMetadataAndHelpPages()
    {
        var english = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("en"));
        var spanish = HelpCatalog.Load(System.Globalization.CultureInfo.GetCultureInfo("es-MX"));

        Assert.Equal("es", spanish.Locale);
        Assert.Equal(
            english.Find(HelpEntryKind.Command, "assess")?.Title,
            spanish.Find(HelpEntryKind.Command, "assess")?.Title);
        Assert.StartsWith("https://motif-docs.pages.dev/es/", spanish.Find(HelpEntryKind.Command, "assess")!.Url);
        Assert.Equal(
            english.GetHelpPage(HelpEntryKind.Command, "assess"),
            spanish.GetHelpPage(HelpEntryKind.Command, "assess"));
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
            HelpCatalog.SiteRoot + "/guide/walkthroughs/open-project-overview/",
            catalog.BuildWalkthroughUrl("open-project-overview"));
    }
}
