using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins what the Overview says: speed first, from the stored parse times; freshness left to the top bar; a
/// refused read said in one plain line; and the tiles in the window's words.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class OverviewPageWordsTests
{
    private static readonly string ProjectPath = Path.Combine(Path.GetTempPath(), "sample.fwdata");

    private static readonly string[] EngineWords =
        ["violation", "rejected", "candidate", "Parser finding", "Assessment", "Unknown (timed out)", "Baseline"];

    [Fact]
    public async Task TheSpeedTileLeadsWithTheStoredWordCountAndTotalParseTime()
    {
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        fake.OverviewCompletesWith(Populated());

        await context.OpenProjectAsync(ProjectPath);

        Assert.Equal("142 words in 38 s", page.SpeedMain);
        Assert.Equal("median 6.4 ms a word · 95th percentile 48 ms", page.SpeedMedian);
        Assert.Equal("3 stopped at the step limit · slowest: mwalimu 700 ms, hawajafika 48 ms", page.SpeedDetails);
    }

    [Theory]
    [InlineData(1, 0.045, "1 word in 45 ms")]
    [InlineData(9, 0.8, "9 words in 0.8 s")]
    [InlineData(1318, 612.4, "1,318 words in 612 s")]
    public async Task TheSpeedHeadlineReadsInTheUnitThatSuitsTheTotal(int words, double seconds, string expected)
    {
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        fake.OverviewCompletesWith(Populated() with
        {
            AssessmentElapsedSeconds = seconds,
            Timing = Populated().Timing with { MeasuredWordCount = words },
        });

        await context.OpenProjectAsync(ProjectPath);

        Assert.Equal(expected, page.SpeedMain);
    }

    [Fact]
    public async Task AParseWithoutMeasuredTimesSaysSoInTheSpeedTile()
    {
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        fake.OverviewCompletesWith(Populated() with
        {
            AssessmentElapsedSeconds = null,
            Timing = new OverviewTiming(null, null, [], 0),
        });

        await context.OpenProjectAsync(ProjectPath);

        Assert.Equal("No parse times recorded", page.SpeedMain);
        Assert.Equal("Parse all words to measure how fast PanGloss is.", page.SpeedMedian);
        Assert.Equal(string.Empty, page.SpeedDetails);
    }

    [Fact]
    public async Task TheTilesSpeakInTheWindowsWords()
    {
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        fake.OverviewCompletesWith(Populated());

        await context.OpenProjectAsync(ProjectPath);

        Assert.Equal("118 of 142 words parse", page.TextCoverageMain);
        Assert.Equal("83% of the words in your Selection · 88% of their 611 occurrences", page.TextCoverageWords);
        Assert.Equal(["118 parsed", "17 no parse", "5 stopped (step or time limit)", "2 skipped"],
            page.TextCoverageSegments.Select(segment => $"{segment.CountText} {segment.Label}"));
        Assert.Equal("71 of 84 rebuilt", page.AccuracyMain);
        Assert.Equal("The grammar still builds 71 of the 84 words you approved in FieldWorks.", page.AccuracyCaption);
        Assert.Equal(["71 rebuilt", "3 built another reading", "8 no parse", "2 stopped"],
            page.AccuracySegments.Select(segment => $"{segment.CountText} {segment.Label}"));
        Assert.Equal("1 disapproved analysis still built · PanGloss confirms 9 of 14 words marked Unknown",
            page.AccuracyBreakdown);
        Assert.Equal("24 warnings · 0 errors", page.WarningsCount);
        Assert.Equal("4 worth a look", page.WarningsDetails);
        foreach (var text in new[]
                 {
                     page.TextCoverageMain, page.TextCoverageWords, page.AccuracyMain,
                     page.AccuracyCaption, page.AccuracyBreakdown, page.SpeedMain, page.SpeedMedian,
                     page.SpeedDetails, page.WarningsCount, page.WarningsDetails,
                 })
            foreach (var word in EngineWords)
                Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheWarningsTileAndTheSidebarBadgeGiveOneCount()
    {
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        var warnings = new WarningsPageModel(context);
        fake.StoredGrammarCheckIs(new GrammarCheckResponse(
            [.. Enumerable.Range(0, 20).Select(_ => Finding(GrammarDiagnosticLevel.Warning)),
             .. Enumerable.Range(0, 4).Select(_ => Finding(GrammarDiagnosticLevel.Information))],
            HasBaseline: true));
        fake.OverviewCompletesWith(Populated());

        await context.OpenProjectAsync(ProjectPath);

        Assert.Equal("24", warnings.Badge);
        Assert.StartsWith(warnings.Badge + " warnings", page.WarningsCount, StringComparison.Ordinal);
    }

    [Fact]
    public void EachTileIsNamedForScreenReadersByItsVisibleTitle()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = NewContext();
            var page = new OverviewPageModel(context);
            fake.OverviewCompletesWith(Populated());
            await context.OpenProjectAsync(ProjectPath);

            var window = Show(page);
            try
            {
                var tiles = window.GetLogicalDescendants().OfType<Button>()
                    .Where(button => button.Classes.Contains("overviewTile")).ToArray();
                Assert.Equal(4, tiles.Length);
                foreach (var tile in tiles)
                {
                    var title = tile.GetLogicalDescendants().OfType<TextBlock>()
                        .Single(text => text.Classes.Contains("overviewTileTitle")).Text!;
                    Assert.Contains(title, Avalonia.Automation.AutomationProperties.GetName(tile),
                        StringComparison.OrdinalIgnoreCase);
                    foreach (var bar in tile.GetLogicalDescendants().OfType<OutcomeBar>())
                        Assert.Contains(title, Avalonia.Automation.AutomationProperties.GetName(bar),
                            StringComparison.OrdinalIgnoreCase);
                }
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void EachTilesBarHasAKeyNamingEveryColourWithItsCount()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = NewContext();
            var page = new OverviewPageModel(context);
            fake.OverviewCompletesWith(Populated());
            await context.OpenProjectAsync(ProjectPath);

            var window = Show(page);
            try
            {
                var bars = window.GetLogicalDescendants().OfType<OutcomeBar>().ToArray();
                Assert.Equal(2, bars.Length);
                var detail = window.GetLogicalDescendants().OfType<TextBlock>()
                    .First(text => text.Classes.Contains("overviewTileDetail"));
                foreach (var bar in bars)
                {
                    var parts = bar.GetLogicalDescendants().OfType<Border>()
                        .Where(part => part.Classes.Contains("outcomeSegment") && !part.Classes.Contains("swatch")).ToArray();
                    var entries = bar.GetLogicalDescendants().OfType<StackPanel>()
                        .Where(entry => entry.Classes.Contains("outcomeLegendEntry")).ToArray();
                    Assert.Equal(bar.Segments!.Count, parts.Length);
                    // Parts that share a colour share one swatch, so the key shows each colour once.
                    var colours = bar.Segments.Select((segment, index) => (segment, index)).GroupBy(item => item.segment.Mark).ToArray();
                    Assert.Equal(colours.Length, entries.Length);
                    for (var index = 0; index < entries.Length; index++)
                    {
                        var swatch = entries[index].Children.OfType<Border>().Single();
                        var colour = parts[colours[index].First().index].Classes.Where(name => name != "outcomeSegment").Order();
                        Assert.Equal(colour, swatch.Classes.Where(name => name is not ("outcomeSegment" or "swatch")).Order());
                        var words = entries[index].Children.OfType<TextBlock>().Select(text => text.Text ?? string.Empty);
                        Assert.Equal(string.Join(", ", colours[index].Select(item => $"{item.segment.CountText} {item.segment.Label}")),
                            string.Join(" ", words));
                        Assert.All(entries[index].Children.OfType<TextBlock>(), text =>
                        {
                            Assert.IsNotAssignableFrom<SelectableTextBlock>(text);
                            Assert.Equal(detail.FontSize, text.FontSize);
                        });
                        Assert.Equal(detail.FontWeight, entries[index].Children.OfType<TextBlock>().Last().FontWeight);
                    }
                }
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task ARefusedReadSaysSoInOnePlainLineAndOffersTryAgain()
    {
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        fake.OnOverview((_, _) => Task.FromResult(CommandOutcome<OverviewResponse>.Refused(
            new Refusal("overview.unexpected", FailureReason.Refused, "The store could not be read."))));

        await context.OpenProjectAsync(ProjectPath);

        Assert.True(page.HasOverviewRefusal);
        Assert.Equal("Motif could not read this project's numbers.", page.OverviewRefusalLine);
        Assert.Equal("sample", page.ProjectTitle);
        Assert.Equal("sample.fwdata", page.ProjectFileName);
        Assert.False(page.ShowNumbers);
        Assert.False(page.ShowTiles);
        Assert.False(page.ShowWarningsTile);
        Assert.Equal(new Uri("https://github.com/sillsdev/motif/issues"), page.ReportProblemUri);

        fake.OverviewCompletesWith(Populated());
        await page.RetryOverviewCommand.ExecuteAsync(null);

        Assert.False(page.HasOverviewRefusal);
        Assert.True(page.ShowNumbers);
        Assert.True(page.ShowTiles);
        Assert.Equal(2, fake.OverviewRequests.Count);
    }

    [Fact]
    public void ARefusedReadShowsNoBaselineLineNoZerosAndNoEmptyTiles()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = NewContext();
            var page = new OverviewPageModel(context);
            fake.OnOverview((_, _) => Task.FromResult(CommandOutcome<OverviewResponse>.Refused(
                new Refusal("overview.unexpected", FailureReason.Refused, "The store could not be read."))));
            await context.OpenProjectAsync(ProjectPath);

            var shown = VisibleText(page);

            Assert.Contains("Motif could not read this project's numbers.", shown);
            Assert.DoesNotContain(shown, text => text.Contains("No Baseline", StringComparison.Ordinal));
            Assert.DoesNotContain(shown, text => text.Contains("No Assessment", StringComparison.Ordinal));
            Assert.DoesNotContain("0", shown);
            Assert.DoesNotContain(shown, text => text.Contains("could not complete", StringComparison.Ordinal));
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ThePopulatedOverviewShowsNoDigestsAndNoFreshnessOfItsOwn()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = NewContext();
            var page = new OverviewPageModel(context);
            fake.OverviewCompletesWith(Populated());
            await context.OpenProjectAsync(ProjectPath);

            var shown = VisibleText(page);

            Assert.Contains("142 words in 38 s", shown);
            Assert.DoesNotContain(shown, text => text.Contains("cccccccc", StringComparison.Ordinal));
            Assert.DoesNotContain(shown, text => text.Contains("dddddddd", StringComparison.Ordinal));
            Assert.DoesNotContain(shown, text => text.Contains("grammar ", StringComparison.Ordinal) &&
                text.Contains("Selection ", StringComparison.Ordinal));
            Assert.DoesNotContain(shown, text => text.Contains("saved", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(shown, text => text.Contains("opened", StringComparison.Ordinal));
            Assert.DoesNotContain(shown, text => text.StartsWith("Assessed", StringComparison.Ordinal));
            foreach (var word in EngineWords)
                Assert.DoesNotContain(shown, text => text.Contains(word, StringComparison.OrdinalIgnoreCase));
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void TheSpeedTileComesFirst()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (fake, context) = NewContext();
            var page = new OverviewPageModel(context);
            fake.OverviewCompletesWith(Populated());
            await context.OpenProjectAsync(ProjectPath);

            var window = Show(page);
            try
            {
                var tiles = window.GetLogicalDescendants().OfType<Button>()
                    .Where(button => button.Classes.Contains("overviewTile") && button.IsEffectivelyVisible)
                    .OrderBy(button => Grid.GetRow(button)).ThenBy(button => Grid.GetColumn(button))
                    .Select(button => Avalonia.Automation.AutomationProperties.GetName(button)).ToArray();
                Assert.Equal("Open Speed in Timing", tiles[0]);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    private static List<string> VisibleText(OverviewPageModel page)
    {
        var window = Show(page);
        try
        {
            return window.GetLogicalDescendants().OfType<TextBlock>()
                .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text))
                .Select(text => text.Text!).ToList();
        }
        finally
        {
            window.Close();
        }
    }

    private static Window Show(OverviewPageModel page)
    {
        var window = new Window { Width = 1240, Height = 900, Content = new OverviewPage(page) };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return window;
    }

    private static GrammarWarning Finding(GrammarDiagnosticLevel level) =>
        new(level, string.Empty, [], [new GrammarWarningPart("a finding", GrammarWarningPartRole.Text)], "a finding")
        {
            Group = "Findings",
            Code = "test.finding",
        };

    private static (FakeCommandClient Fake, WorkspaceContext Context) NewContext()
    {
        var fake = new FakeCommandClient();
        return (fake, WorkspaceContextTests.NewContext(fake));
    }

    internal static OverviewResponse Populated() => new(
        "Sample", DateTimeOffset.Parse("2026-09-30T08:00:00Z"), DateTimeOffset.Parse("2026-09-30T06:11:00Z"),
        142, 2, 0, 611, 1318, 47, 862, "assessment/one", DateTimeOffset.Parse("2026-09-30T08:51:00Z"), 38,
        "sha256:" + new string('c', 64), "sha256:" + new string('d', 64),
        new OverviewTextCoverage(118, 17, 5, 2, 611, 540) { OccurrenceCoveragePercent = 88.4 },
        new OverviewAccuracy(71, 84, 3, 2, 1, 3, 9, 14)
        {
            ApprovedWordsNoMatch = 3,
            ApprovedWordsNoParse = 8,
            ApprovedWordsUnknown = 2,
        },
        new OverviewTiming(6.4, 48.2, [new SlowWordTiming("mwalimu", 700), new SlowWordTiming("hawajafika", 48)], 3)
        {
            MeasuredWordCount = 142,
        },
        new OverviewWarningsSummary(24, 20, "Parser finding", 20)
        {
            ErrorCount = 0,
            WarningCount = 20,
            InformationCount = 4,
        })
    {
        SelectionResolved = true,
        WordCoveragePercent = 83.1,
        ProjectFileName = "Sample.fwdata",
        BaselineCapturedUtc = DateTimeOffset.Parse("2026-09-22T06:00:00Z"),
        BaselineSourceLastWriteUtc = DateTimeOffset.Parse("2026-09-22T05:40:00Z"),
    };
}
