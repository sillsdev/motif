using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
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
        ["violation", "rejected", "Parser finding", "Assessment", "Unknown (timed out)", "Baseline"];

    private const int TimedOutWordCount = 2;
    private static readonly string[] StepLimitedWords = ["mwalimu", "stopped-2", "stopped-3"];

    [Theory]
    [InlineData(0, "No known word matches; some named connections could not be followed", "No known matches; incomplete")]
    [InlineData(2, "At least 2 of your words use something a warning names; some named connections could not be followed", "At least 2 words")]
    public async Task WarningTotalsQualifyIncompleteRoutes(int known, string total, string kind)
    {
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        var warnings = Populated().Warnings! with
        {
            YourWords = new WarningWordsTouched(known, 0, [])
                { IsComplete = false, AttributionLimits = [WarningAttributionReason.UnsupportedKind] },
            ByKind = [new GrammarWarningSummary("mixed", "Mixed", GrammarDiagnosticLevel.Warning, 1)
                { YourWords = known, WordAttributionComplete = false }],
        };
        fake.OverviewCompletesWith(Populated() with { Warnings = warnings });
        await context.OpenProjectAsync(ProjectPath);
        Assert.Equal(total, page.WarningsYourWordsText);
        Assert.Equal(kind, Assert.Single(page.WarningKindRows).IdentityMatchedWords);
    }

    [Fact]
    public async Task TheSpeedTileLeadsWithTheStoredWordCountAndTotalParseTime()
    {
        using var culture = new CultureScope(System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        fake.OverviewCompletesWith(Populated());

        await context.OpenProjectAsync(ProjectPath);

        Assert.Equal("142 words · 38 s total word time", page.SpeedMain);
        Assert.Equal("median 6.4 ms a word · 95th percentile 48.2 ms", page.SpeedMedian);
        Assert.Equal("3 stopped at the step limit", page.SpeedDetails);
        Assert.Equal([("mwalimu", "700 ms"), ("hawajafika", "48 ms")],
            page.SlowestWordRows.Select(row => (row.Word, row.TimeText)));
        Assert.Equal([("Morphological rules", "56%"), ("Phonological rules", "21%"),
                      ("Lexical entries", "7%"), ("Root lookup", "4%"), ("Not attributed", "12%")],
            page.TimingKindShares.Select(row => (row.Label, row.ShareText)));
    }

    [Fact]
    public void SpeedTileKeepsSlowestWordsCompactAndMarksAStoppedWord()
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
                var speed = window.GetLogicalDescendants().OfType<Control>()
                    .Single(control => Avalonia.Automation.AutomationProperties.GetName(control) == "Speed");
                var summary = speed.GetLogicalDescendants().OfType<TextBlock>()
                    .Single(text => text.Text?.StartsWith("Slowest:", StringComparison.Ordinal) == true);

                Assert.Contains("mwalimu", summary.Text, StringComparison.Ordinal);
                Assert.Contains("◐ Stopped", summary.Text, StringComparison.Ordinal);
                Assert.Contains("700 ms", summary.Text, StringComparison.Ordinal);
                Assert.Empty(speed.GetLogicalDescendants().OfType<SIL.Motif.App.Views.WordRow>());
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task TheSpeedTileRetainsTimingPrecisionForPercentilesAndSlowestWords()
    {
        using var culture = new CultureScope(System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        fake.OverviewCompletesWith(Populated() with
        {
            Timing = Populated().Timing with
            {
                MedianMs = 0.123456,
                Percentile95Ms = 0.654321,
                SlowestWords = [new SlowWordTiming("z-slowest", 0.800001), new SlowWordTiming("a-next", 0.8)],
                MeasuredWordCount = 2,
            },
        });

        await context.OpenProjectAsync(ProjectPath);

        Assert.Equal("median 0.123456 ms a word · 95th percentile 0.654321 ms", page.SpeedMedian);
        Assert.Equal([("z-slowest", "0.800001 ms"), ("a-next", "0.8 ms")],
            page.SlowestWordRows.Select(row => (row.Word, row.TimeText)));
    }

    [Fact]
    public void PopulatedOverviewCaptureFixtureKeepsStoppedCountsConsistent()
    {
        var overview = Populated();

        AssertCaptureStopCounts(overview, "populated");
        Assert.Equal(TimedOutWordCount, overview.TextCoverage.UnknownWords - overview.Timing.StepLimitedWordCount);
    }

    [Fact]
    public async Task TheSlowestWordsTakeTheirRowsFromAParseThatArrivesAfterTheOverview()
    {
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        fake.OverviewCompletesWith(Populated());
        await context.OpenProjectAsync(ProjectPath);
        Assert.False(page.SlowestWordRows[0].HasCard);

        context.Assess.Words.Load([new AssessmentWordResult("mwalimu", "capped", true, "INCOMPLETE — step limit", 700, null)]);

        var mwalimu = page.SlowestWordRows[0];
        Assert.True(mwalimu.HasCard);
        Assert.Same(context.Assess.Words.Find("mwalimu")!.WordRow, mwalimu.Row);
        Assert.Equal("700 ms", mwalimu.TimeText);
    }

    [Theory]
    [InlineData(1, 0.045, "1 word · 45 ms total word time")]
    [InlineData(9, 0.8, "9 words · 0.8 s total word time")]
    [InlineData(1318, 612.4, "1,318 words · 612.4 s total word time")]
    public async Task TheSpeedHeadlineReadsInTheUnitThatSuitsTheTotal(int words, double seconds, string expected)
    {
        using var culture = new CultureScope(System.Globalization.CultureInfo.GetCultureInfo("en-US"));
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

    [Theory]
    [InlineData(0.04, 0.4)]
    [InlineData(0.05, 0.5)]
    public async Task APositiveSubMillisecondResidualAndItsShareRemainOnTheOverview(double residualMs, double share)
    {
        var timing = new OverviewTiming(0.1, 0.1, [], 0)
        {
            MeasuredWordCount = 1,
            Kinds = [new TimingAggregateRow("morph_rule", "morph_rule", 0.1 - residualMs, 1 - share, 1)
                { Kind = "morph_rule" }],
            Attribution = new WordTimeAttribution(1, 0.1, 0.1 - residualMs, residualMs, share, 0, false),
        };
        var response = Populated() with { Timing = timing };
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        fake.OverviewCompletesWith(response);

        await context.OpenProjectAsync(ProjectPath);

        var residual = Assert.Single(page.TimingKindShares, row => row.IsNotAttributed);
        Assert.Equal(residualMs, residual.ElapsedMs);
        Assert.Equal(TimingShare.FormatPercent(share), residual.ShareText);
    }

    [Fact]
    public async Task ZeroAndUnavailableResidualsDoNotAppearAsMeasuredPositiveShares()
    {
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        fake.OverviewCompletesWith(Populated() with
        {
            Timing = Populated().Timing with
            {
                Attribution = new WordTimeAttribution(1, 0.1, 0.1, 0, 0, 0, false),
            },
        });

        await context.OpenProjectAsync(ProjectPath);
        Assert.DoesNotContain(page.TimingKindShares, row => row.IsNotAttributed);

        fake.OverviewCompletesWith(Populated() with
        {
            Timing = Populated().Timing with
            {
                Attribution = new WordTimeAttribution(1, 0.1, 0.1, null, null, 0, false),
            },
        });
        await page.RetryOverviewCommand.ExecuteAsync(null);
        Assert.DoesNotContain(page.TimingKindShares, row => row.IsNotAttributed);
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
        Assert.Equal(string.Empty, page.SpeedMedian);
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
        Assert.Equal("83% of the words in your Selection · 88% of their 611 places", page.TextCoverageWords);
        Assert.Equal(["81 same", "37 different", "17 no parse", "5 stopped", "2 not parsed"],
            page.TextCoverageSegments.Select(segment => $"{segment.CountText} {segment.Label}"));
        Assert.All(page.TextCoverageSegments, segment => Assert.NotNull(segment.Command));
        Assert.Equal("71 of 84 rebuilt", page.AccuracyMain);
        Assert.Equal("The grammar still builds 71 of the 84 words you approved in FieldWorks.", page.AccuracyCaption);
        Assert.Equal(["71 kept", "3 built something else", "8 lost", "2 stopped"],
            page.AccuracySegments.Select(segment => $"{segment.CountText} {segment.Label}"));
        Assert.All(page.AccuracySegments, segment => Assert.Equal(MarkKind.Meaning, segment.Mark.Kind));
        Assert.Equal("1 disapproved analysis still built · PanGloss confirms 9 of 14 words marked Unknown",
            page.AccuracyBreakdown);
        Assert.Equal("20 warnings · 0 errors · 4 information findings", page.WarningsCount);
        Assert.Equal("14 of your words use something a warning names", page.WarningsYourWordsText);
        Assert.Equal("3 spelling candidates; not confirmed uses", page.WarningsSpellingCandidatesText);
        Assert.Equal("Grammar warning", Assert.Single(page.WarningKindRows).Name);
        Assert.Equal("At least 4 words", Assert.Single(page.WarningKindRows).IdentityMatchedWords);
        Assert.Equal("2 spelling candidates; not confirmed uses", Assert.Single(page.WarningKindRows).SpellingCandidates);
        foreach (var text in new[]
                 {
                     page.TextCoverageMain, page.TextCoverageWords, page.AccuracyMain,
                     page.AccuracyCaption, page.AccuracyBreakdown, page.SpeedMain, page.SpeedMedian,
                     page.SpeedDetails, page.WarningsCount,
                 })
            foreach (var word in EngineWords)
                Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OneWarningNamedWordUsesTheSingularWindowSentence()
    {
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        var overview = Populated();
        fake.OverviewCompletesWith(overview with
        {
            Warnings = overview.Warnings! with
            {
                YourWords = new WarningWordsTouched(1, 1, []),
            },
        });

        await context.OpenProjectAsync(ProjectPath);

        Assert.Equal("1 of your words uses something a warning names", page.WarningsYourWordsText);
    }

    [Fact]
    public async Task TheOverviewAndWarningsControlsUseTheSameDistinctWordCount()
    {
        var words = new[] { "walikata", "anakata", "wamekata" }
            .Select(word => new ObjectUseWord(new SIL.Motif.Contract.Responses.WordRow(
                word, WordRowOutcome.Different, "Lost", WordRowTone.Problem)))
            .ToArray();
        const string guid = "33333333-3333-3333-3333-333333333333";
        var finding = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Finding",
            [new GrammarWarningPart("named item", GrammarWarningPartRole.Object, guid, "MoForm")
            {
                Reach = new WarningReach(WarningWordsPath.Uses)
                {
                    AllomorphIds = [guid],
                },
            }], [], "warning: finding")
        {
            Code = "test.finding",
            YourWords = new WarningWords(WarningWordsMatch.Identity, words, [])
            {
                Paths = [WarningWordsPath.ThroughAllomorphs],
            },
        };
        var findings = new[] { finding };
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        fake.OverviewCompletesWith(Populated() with
        {
            Warnings = Populated().Warnings! with { YourWords = WarningWordsQuery.Touched(findings) },
        });
        await context.OpenProjectAsync(ProjectPath);

        var warningPage = new GrammarWarningsViewModel();
        warningPage.Load(findings);

        Assert.Equal("Touch your words · 3 words", warningPage.TouchYourWordsText);
        Assert.Equal("3 of your words use something a warning names", page.WarningsYourWordsText);
    }

    [Fact]
    public async Task TheWarningsTileShowsEachReportLevel()
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
        Assert.Equal("20 warnings · 0 errors · 4 information findings", page.WarningsCount);
    }

    [Fact]
    public async Task TheAppAndOverviewCommandKeepSpellingCandidatesOutsideTheExactUseCount()
    {
        var response = Populated();
        var cli = CommandTextRenderer.Render(CommandOutcome<OverviewResponse>.Success(response), asJson: false).Output;
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        fake.OverviewCompletesWith(response);

        await context.OpenProjectAsync(ProjectPath);

        Assert.Equal("14 of your words use something a warning names", page.WarningsYourWordsText);
        Assert.Equal("3 spelling candidates; not confirmed uses", page.WarningsSpellingCandidatesText);
        Assert.Contains("14 of your words use something a finding names (7 don't parse)", cli,
            StringComparison.Ordinal);
        Assert.Contains("Not counted: 3 spelling candidates; not confirmed uses of the phoneme", cli,
            StringComparison.Ordinal);
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
                var tiles = window.GetLogicalDescendants().OfType<Control>()
                    .Where(tile => tile.Classes.Contains("overviewTile")).ToArray();
                Assert.Equal(4, tiles.Length);
                Assert.All(tiles, tile => Assert.Equal(tiles[0].Bounds.Size, tile.Bounds.Size));
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
                            Assert.Equal(detail.FontSize, text.FontSize);
                        });
                        Assert.All(entries[index].Children.OfType<HyperlinkButton>(), link =>
                        {
                            Assert.NotNull(link.Command);
                            Assert.NotEmpty(Avalonia.Automation.AutomationProperties.GetName(link) ?? string.Empty);
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
    public async Task LookFirstDescribesOneLostWordInTheSingular()
    {
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        var overview = Populated();
        fake.OverviewCompletesWith(overview with
        {
            LookFirst = overview.LookFirst with { ApprovedLostWords = ["hawajafika"] },
        });
        await context.OpenProjectAsync(ProjectPath);
        Assert.Equal("1 approved word is Lost; the grammar builds nothing for it.", page.LookFirstRows[0].Summary);
    }

    [Fact]
    public async Task LookFirstUsesStoredResultsAndOpensTheExactMatrixCellsOrTimingWords()
    {
        using var culture = new CultureScope(System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        var (fake, context) = NewContext();
        var page = new OverviewPageModel(context);
        var texts = new TextsPageModel(context);
        var timing = new TimingPageModel(context);
        fake.OverviewCompletesWith(Populated());
        await context.OpenProjectAsync(ProjectPath);

        var rows = page.LookFirstRows;
        Assert.Equal(3, rows.Count);
        Assert.Equal("1", rows[0].Number);
        Assert.Contains("6 approved words are Lost", rows[0].Summary);
        Assert.Contains("3 words use kat (named by a grammar warning)", rows[0].Detail);
        Assert.Equal("2", rows[1].Number);
        Assert.Contains("3 words stopped at the step limit", rows[1].Summary);
        Assert.Equal("33.6 s of 38 s total word time", rows[1].Detail);
        Assert.Equal("3", rows[2].Number);
        Assert.Contains("3 Unknown words differ", rows[2].Summary);
        Assert.Equal(string.Empty, rows[2].Detail);

        rows[0].OpenCommand.Execute(null);
        Assert.Equal([new TextsListCell(WordProjectStatus.Approved, CompareColumnKind.NoParse)],
            texts.Assess.Compare.Cells.Where(cell => cell.IsSelected)
                .Select(cell => new TextsListCell(cell.Row, cell.Column)));
        rows[1].OpenCommand.Execute(null);
        Assert.Equal(StepLimitedWords, timing.Focus!.Words);
        rows[2].OpenCommand.Execute(null);
        Assert.Equal([new TextsListCell(WordProjectStatus.Candidate, CompareColumnKind.NoMatch)],
            texts.Assess.Compare.Cells.Where(cell => cell.IsSelected)
                .Select(cell => new TextsListCell(cell.Row, cell.Column)));
    }

    [Fact]
    public void LookFirstRowsRevealTheirLinkOnHoverOrFocus()
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
                window.Width = 1040;
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                var links = window.GetLogicalDescendants().OfType<Button>()
                    .Where(link => link.Classes.Contains("overviewLookFirstRow")).ToArray();
                Assert.Equal(page.LookFirstRows.Count, links.Length);
                var actionHint = Assert.Single(links[0].GetLogicalDescendants().OfType<TextBlock>(),
                    text => text.Classes.Contains("overviewLookFirstAction"));
                Assert.Equal(0, actionHint.Opacity);
                links[0].BringIntoView();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var point = links[0].TranslatePoint(new Avalonia.Point(
                    links[0].Bounds.Width / 2, links[0].Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.True(links[0].IsPointerOver);
                Assert.Equal(1, actionHint.Opacity);
                window.MouseMove(new Avalonia.Point(-100, -100));
                Dispatcher.UIThread.RunJobs();
                Assert.True(links[0].Focus(Avalonia.Input.NavigationMethod.Tab));
                Assert.Equal(1, actionHint.Opacity);
                foreach (var (link, row) in links.Zip(page.LookFirstRows))
                {
                    Assert.True(link.IsTabStop);
                    Assert.IsAssignableFrom<CommunityToolkit.Mvvm.Input.IRelayCommand>(link.Command);
                    Assert.Equal(row.LinkText, AutomationProperties.GetName(link));
                    Assert.Contains(link.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == row.Summary);
                    if (row.Detail.Length > 0)
                        Assert.Contains(link.GetLogicalDescendants().OfType<TextBlock>(), text => text.Text == row.Detail);
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

            Assert.Contains("142 words · 38 s total word time", shown);
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
                var grid = window.GetLogicalDescendants().OfType<UniformGrid>()
                    .Single(candidate => candidate.Classes.Contains("overviewTiles"));
                var tiles = grid.Children.OfType<Control>()
                    .Where(tile => tile.Classes.Contains("overviewTile") && tile.IsEffectivelyVisible).ToArray();
                Assert.Equal("Speed", Avalonia.Automation.AutomationProperties.GetName(tiles[0]));
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
        new OverviewTextCoverage(118, 17, StepLimitedWords.Length + TimedOutWordCount, 2, 611, 540)
        {
            SameWords = 81,
            DifferentWords = 37,
            OccurrenceCoveragePercent = 88.4,
        },
        new OverviewAccuracy(71, 84, 3, 2, 1, 3, 9, 14)
        {
            ApprovedWordsNoMatch = 3,
            ApprovedWordsNoParse = 8,
            ApprovedWordsUnknown = 2,
        },
        new OverviewTiming(6.4, 48.2, [new SlowWordTiming("mwalimu", 700), new SlowWordTiming("hawajafika", 48)],
            StepLimitedWords.Length)
        {
            MeasuredWordCount = 142,
            Kinds =
            [
                new TimingAggregateRow("morph_rule", "morph_rule", 21280, 0.56, 142) { Kind = "morph_rule" },
                new TimingAggregateRow("phon_rule", "phon_rule", 7980, 0.21, 142) { Kind = "phon_rule" },
                new TimingAggregateRow("lex_entry", "lex_entry", 2660, 0.07, 142) { Kind = "lex_entry" },
                new TimingAggregateRow("root_index", "root_index", 1520, 0.04, 142) { Kind = "root_index" },
            ],
            Attribution = new WordTimeAttribution(142, 38000, 33440, 4560, 0.12, 0, false),
        },
        new OverviewWarningsSummary(24, 20, "Grammar warning", 20)
        {
            ErrorCount = 0,
            WarningCount = 20,
            InformationCount = 4,
            YourWords = new WarningWordsTouched(14, 7, []) { BySpellingOnly = 3 },
            ByKind =
            [
                new GrammarWarningSummary("test.finding", "Grammar warning", GrammarDiagnosticLevel.Warning, 6)
                {
                    YourWords = 4,
                    BySpellingOnly = 2,
                },
            ],
        })
    {
        LookFirst = new OverviewLookFirst(
            ["lost-1", "lost-2", "lost-3", "lost-4", "lost-5", "lost-6"],
            [new OverviewSharedMorpheme("kat", 3, true), new OverviewSharedMorpheme("ja-", 3, false)],
            StepLimitedWords, 33600, 3)
        { SharedLostMorphemesAvailable = true },
        SelectionResolved = true,
        WordCoveragePercent = 83.1,
        ProjectFileName = "Sample.fwdata",
        BaselineCapturedUtc = DateTimeOffset.Parse("2026-09-22T06:00:00Z"),
        BaselineSourceLastWriteUtc = DateTimeOffset.Parse("2026-09-22T05:40:00Z"),
    };

    internal static void AssertCaptureStopCounts(OverviewResponse overview, string scene)
    {
        Assert.True(overview.TextCoverage.UnknownWords >= overview.Timing.StepLimitedWordCount,
            $"Overview scene '{scene}' has more step-limited words than generally stopped words.");
        Assert.True(overview.LookFirst.StepLimitedWords.Count == overview.Timing.StepLimitedWordCount,
            $"Overview scene '{scene}' reports {overview.Timing.StepLimitedWordCount} step-limited words but " +
            $"lists {overview.LookFirst.StepLimitedWords.Count}.");
    }
}
