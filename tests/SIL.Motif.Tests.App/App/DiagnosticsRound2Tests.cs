using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Diagnostics round 2: each finding once, readable group names, and one set of counts.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
public sealed class DiagnosticsRound2Tests
{
    private static GrammarWarningPart Entry(string headword) => new(headword, GrammarWarningPartRole.Object,
        $"id-{headword}", "LexEntry")
    {
        Title = headword, Status = GrammarSubjectStatus.Object, FieldWorksTool = "lexiconEdit",
        FieldWorksLink = $"silfw://localhost/link?database%3dceb%26tool%3dlexiconEdit%26guid%3d{headword}%26tag%3d",
    };

    private static async Task<(WorkspaceContext, DiagnosticsPage, MotifTestWindow)> ShowAsync(
        IReadOnlyList<GrammarWarning> findings, double width)
    {
        var (context, model, _) = await DiagnosticsFix1Tests.OpenAsync(
            fake => fake.StoredGrammarCheckIs(new GrammarCheckResponse(findings, HasBaseline: true)));
        var page = new DiagnosticsPage(model);
        var window = new MotifTestWindow { Width = width, Height = 900, Content = page };
        window.Show();
        PageScreenshots.Settle(window);
        return (context, page, window);
    }

    private static IEnumerable<HyperlinkButton> Links(Control root) =>
        root.GetVisualDescendants().OfType<HyperlinkButton>().Where(link => link.Classes.Contains("warningObjectLink"));

    [Fact]
    public void EachFindingIsListedOnceAsASentenceWithItsStemAsALink()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            GrammarWarning[] findings = [DiagnosticsFix1Tests.StemWithoutCategory("hái"),
                DiagnosticsFix1Tests.StemWithoutCategory("pitnit"), DiagnosticsFix1Tests.StemWithoutCategory("sagang")];
            var (context, page, window) = await ShowAsync(findings, 1000);
            try
            {
                var blocks = page.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .Where(block => block.IsEffectivelyVisible && block.Inlines is { Count: > 0 }).ToArray();
                // One sentence per finding, each holding its stem as a link inside the text.
                Assert.Equal(3, blocks.Length);
                Assert.All(blocks, block => Assert.Single(block.Inlines!.OfType<InlineUIContainer>()));
                Assert.Equal(3, Links(page).Count());
                Assert.Equal("Lexical entry '", Assert.IsType<Run>(blocks[0].Inlines![0]).Text);
                Assert.Equal("' has no grammatical category.", Assert.IsType<Run>(blocks[0].Inlines![2]).Text);
                // Neither the raw engine line nor the finding's title is listed beside the sentence.
                var shown = DiagnosticsFix1Tests.Texts(page).ToArray();
                Assert.DoesNotContain(shown, text => text is not null && text.StartsWith("error: hc-", StringComparison.Ordinal));
                Assert.DoesNotContain("Stem has no category", shown);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void SeparatorsBetweenLinkedEntriesAreInTheTextSoEveryWrappedLineKeepsThem()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            // The sentence does not mention the entries, so they follow it as a comma-separated list.
            var entries = Enumerable.Range(0, 14).Select(index => Entry($"entry{index:00}word")).ToArray();
            var finding = new GrammarWarning(GrammarDiagnosticLevel.Error, "Several", entries, [],
                "Several entries need attention.") { Code = "recorded-code", Description = "Several entries need attention." };
            var (context, page, window) = await ShowAsync([finding], 420);
            try
            {
                var block = Assert.Single(page.GetVisualDescendants().OfType<CopyableTextBlock>(),
                    candidate => candidate.IsEffectivelyVisible && candidate.Inlines is { Count: > 0 });
                Assert.Equal(14, block.Inlines!.OfType<InlineUIContainer>().Count());
                // A comma run between each pair, never after the last: separators are text, not per-line layout.
                Assert.Equal(13, block.Inlines!.OfType<Run>().Count(run => run.Text == ", "));
                // The block wrapped onto several lines at this width.
                Assert.True(block.Bounds.Height > block.FontSize * 3, $"The sentence did not wrap: {block.Bounds.Height}");
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void GroupHeadingsUseTheWindowsNameForAKnownCodeAndTheCodeOtherwise()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var unknown = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Recorded", [], [], "Actual finding")
                { Code = "recorded-code" };
            GrammarWarning[] findings = [DiagnosticsFix1Tests.StemWithoutCategory("hái"),
                DiagnosticsFix1Tests.StemWithoutCategory("pitnit"), unknown];
            var (context, page, window) = await ShowAsync(findings, 1000);
            try
            {
                var headings = page.GetVisualDescendants().OfType<CopyableTextBlock>()
                    .Where(block => block.Classes.Contains("diagnosticGroupName")).ToArray();
                Assert.Equal(2, headings.Length);
                var known = Assert.Single(headings, heading => heading.Text!.StartsWith("Stems with No Grammatical Category", StringComparison.Ordinal));
                Assert.Equal("Stems with No Grammatical Category · 2 findings", known.Text);
                Assert.DoesNotContain("hc-stem", known.Text!, StringComparison.Ordinal);
                Assert.Equal("Rule code: hc-stem-no-grammatical-category", ToolTip.GetTip(known));
                var fallback = Assert.Single(headings, heading => heading.Text!.StartsWith("recorded-code", StringComparison.Ordinal));
                Assert.Equal("recorded-code · 1 finding", fallback.Text);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void ALargeKindShowsOnePageOfSentencesAtATime()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var findings = Enumerable.Range(0, 135).Select(index => DiagnosticsFix1Tests.StemWithoutCategory($"stem{index}")).ToArray();
            var (context, page, window) = await ShowAsync(findings, 1000);
            try
            {
                var list = Assert.Single(page.GetVisualDescendants().OfType<FindingSentenceList>());
                Assert.Equal(20, list.Children.OfType<CopyableTextBlock>().Count());
                var more = Assert.Single(list.Children.OfType<Button>(), button => Equals(button.Content, "Show 20 more"));
                more.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                PageScreenshots.Settle(window);
                Assert.Equal(20, list.Children.OfType<CopyableTextBlock>().Count());
                Assert.Contains("stem20", Links(list).Select(link => ((TextBlock)link.Content!).Text));
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(60));
    }

    [Theory]
    [InlineData("hc-stem-no-grammatical-category", "Stems with No Grammatical Category")]
    [InlineData("hc-duplicate-feature-bundle", "Duplicate Segment Features")]
    [InlineData("grammar.msa.lex-entry-infl-type-unresolved", "Unresolved Entry Inflection Type")]
    [InlineData("fwdata.dangling-reference", "Reference to a Missing Item")]
    public void EveryRuleCodeTheAppCanProduceHasATitleCaseNameInTheResx(string code, string expected) =>
        Assert.Equal(expected, MotifAppStrings.DiagnosticKindName(code));

    [Fact]
    public void ACodeTheAppDoesNotNameHasNoNameSoThePageShowsTheCode()
    {
        Assert.Null(MotifAppStrings.DiagnosticKindName("recorded-code"));
        Assert.Null(MotifAppStrings.DiagnosticKindName(null));
        Assert.Null(MotifAppStrings.DiagnosticKindName(" "));
    }

    [Fact]
    public void TheStatusBarAndTheSummaryLineGiveTheSameCountsInTheSameWords()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var findings = new List<GrammarWarning>();
            findings.AddRange(Enumerable.Range(0, 136).Select(index => DiagnosticsFix1Tests.StemWithoutCategory($"stem{index}")));
            findings.AddRange(Enumerable.Range(0, 11).Select(index => new GrammarWarning(GrammarDiagnosticLevel.Warning, "Other", [], [],
                $"warn {index}") { Code = "lexicon.other" }));
            findings.AddRange(Enumerable.Range(0, 4).Select(index => new GrammarWarning(GrammarDiagnosticLevel.Information, "Note", [], [],
                $"note {index}") { Code = "lexicon.note" }));
            var check = new GrammarCheckResponse(findings, HasBaseline: true);
            var (context, page, window) = await ShowAsync(findings, 1000);
            try
            {
                var counts = SIL.Motif.Commands.Catalog.WarningsCommand.FromCheck(check);
                var overview = SampleOverview.Populated() with
                {
                    Warnings = new OverviewWarningsSummary(counts.TotalCount, null, null, null)
                    {
                        ErrorCount = counts.ErrorCount, WarningCount = counts.WarningCount, InformationCount = counts.InformationCount,
                    },
                };
                var strip = new HealthStripViewModel(context) { Overview = overview };
                var summary = Assert.Single(DiagnosticsFix1Tests.Texts(page), text => text is not null && text.StartsWith("151 findings", StringComparison.Ordinal));
                Assert.Equal("136 errors, 11 warnings, 4 information findings", strip.WarningText);
                Assert.Equal("136 errors, 11 warnings", strip.BarText);
                Assert.Contains(strip.BarText, summary, StringComparison.Ordinal);
                Assert.Equal("151 findings: 136 errors, 11 warnings, 4 information findings.", summary);
                Assert.DoesNotContain("grammar warnings", strip.WarningText, StringComparison.Ordinal);
            }
            finally { window.Close(); await context.StopProjectWorkAsync(); }
        }, TimeSpan.FromSeconds(60));
    }

    [Fact]
    public void DiagnosticsSentencesAreSentenceCaseAndGroupNamesAreTitleCase()
    {
        Assert.Equal("Diagnostics are advice only. Read and edit rules in FieldWorks.", MotifAppStrings.DiagnosticsIntro);
        Assert.Equal("Recorded diagnostics are unavailable.", MotifAppStrings.DiagnosticsUnavailable);
        Assert.Equal("Recorded Parser Steps are unavailable.", MotifAppStrings.DiagnosticsNoWork);
        Assert.Equal("Parser Steps per measured word: 3 of 4 words have recorded Parser Steps.",
            string.Format(MotifAppStrings.DiagnosticsMeasuredFormat, 3, 4));
    }
}
