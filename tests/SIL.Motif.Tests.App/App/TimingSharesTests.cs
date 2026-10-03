using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that every share on the Timing page is of the chosen words' whole measured parse time, that the time no
/// rule recorded is shown as unattributed, and that no count is shown without saying what it counts.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class TimingSharesTests
{
    private const string ProjectPath = @"C:\projects\sample.fwdata";

    private static readonly (string Word, int? Ms)[] NineWords =
    [
        ("mwalimu", 700), ("hawajafika", 48), ("walikula", 12), ("alikula", 9), ("ninakula", 9),
        ("tunakula", 8), ("wanakula", 7), ("unakula", 6), ("kula", 1),
    ];

    private static readonly TimingAggregateRow[] Kinds =
    [
        new("morph_rule", "morph_rule", 480, 480d / 800, 9) { Calls = 420 },
        new("phon_rule", "phon_rule", 160, 160d / 800, 7) { Calls = 70 },
        new("root_index", "root_index", 40, 40d / 800, 9) { Calls = null },
    ];

    private static readonly TimingAggregateRow[] Rules =
    [
        new("Subject agreement", "Subject agreement", 288, 288d / 800, 6) { Kind = "morph_rule", IdentityQuality = "authored" },
        new("Vowel harmony", "Vowel harmony", 120, 120d / 800, 7) { Kind = "phon_rule", IdentityQuality = "authored" },
    ];

    [Theory]
    [InlineData(795, "795 ms")]
    [InlineData(9999.9, "9,999.9 ms")]
    [InlineData(10000, "10.0 s")]
    [InlineData(10832, "10.8 s")]
    public void RecordedDurationsUseMillisecondsUnderTenSecondsAndOneDecimalSecondsAbove(
        double milliseconds, string expected)
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal(expected, TimingShare.FormatDuration(milliseconds));
    }

    [Fact]
    public async Task TwoKindsSharingAKeySelectOnlyTheClickedObject()
    {
        TimingAggregateRow[] rules =
        [
            new("same-key", "One", 2, 0.2, 1) { Kind = "morph_rule" },
            new("same-key", "One", 1, 0.1, 1) { Kind = "phon_rule" },
        ];
        var timing = await LoadedTiming(NineWords, Kinds, rules);
        await timing.ChooseRuleCommand.ExecuteAsync(rules[1]);
        Assert.Same(rules[1], timing.SelectedRuleRow);
        Assert.Equal([false, true], timing.RuleRows.Select(row => row.IsChosen));
    }

    [Fact]
    public async Task KindSharesAreOfTheWordsWholeParseTimeWithOtherTimeApart()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming(NineWords, Kinds, Rules);

        Assert.Equal(
            [("Morphological rules", "60%", "480 ms"), ("Phonological rules", "20%", "160 ms"),
                ("Root lookup", "5%", "40 ms"), ("Not attributed", "15%", "120 ms")],
            timing.KindShares.Select(share => (share.Label, share.ShareText, share.TimeText)));
        Assert.Null(timing.KindShares[^1].Source);
        Assert.Equal(1d, timing.KindShares.Sum(share => share.Share!.Value), precision: 6);
        Assert.Equal("Every share is of the 800 ms these 9 words took to parse.", timing.ShareDenominatorText);
        Assert.Equal("Not attributed: 120 ms (15%).", timing.NotAttributedText);
        Assert.Empty(timing.OverrunText);
        Assert.Equal(["420 calls", "70 calls", "Not counted"], timing.KindCallShares.Select(share => share.CallsText));
    }

    [Fact]
    public void APartialCallTotalSaysItIncludesOnlyRecordedCalls()
    {
        var source = new TimingAggregateRow("morph_rule", "morph_rule", 10, 0.5, 1)
        {
            Calls = 5,
            CallsArePartial = true,
        };
        var share = new TimingShare("Morphological rules", "morph_rule", 10, 0.5, 1, source);

        Assert.Equal("5 recorded calls; some call counts unavailable", share.CallsText);
    }

    [Fact]
    public async Task SharesUseTheResponseForFractionalWordTime()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming([("fast", 1)],
            [new TimingAggregateRow("morph_rule", "morph_rule", 0.9, 0.5, 1)], [],
            attribution: new WordTimeAttribution(1, 1.8, 0.9, 0.9, 0.5, 0, false),
            elapsedNs: [1_800_000]);

        Assert.Equal("50%", timing.KindShares.Single(share => share.Source is not null).ShareText);
        Assert.Equal("1.8 ms", timing.HeadlineTotal);
    }

    [Fact]
    public async Task RuleSharesUseTheSameWholeParseTimeAndCountNoAttempts()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming(NineWords, Kinds, Rules);

        Assert.Equal("SHARE OF 800 ms", timing.RuleShareHeader);
        Assert.Equal([("Subject agreement", "Morphological rules", "288 ms", "36%", "6"),
                ("Vowel harmony", "Phonological rules", "120 ms", "15%", "7")],
            timing.RuleShares.Select(share => (share.Label, share.KindLabel, share.TimeText, share.ShareText,
                share.WordsText)));
        Assert.Equal("288 ms · 36% of these 9 words' 800 ms · recorded in 6 words", timing.RuleSummary);
        Assert.DoesNotContain("attempt", timing.RuleSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WordCardTimingSharesUseWholePercentages()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("36% · 288 ms", new TimingShareViewModel("Subject agreement", 0.36, 288).ShareLabel);
        Assert.Equal("15% · 120 ms", new TimingShareViewModel("Other time", 0.15, 120).ShareLabel);
    }

    [Fact]
    public async Task RuleRowsAreOrderedByTheirShareOfWholeWordTime()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        TimingAggregateRow[] rules = [.. Rules.Reverse()];
        var timing = await LoadedTiming(NineWords, Kinds, rules);

        Assert.Equal(["Subject agreement", "Vowel harmony"], timing.RuleRows.Select(row => row.Share.Label));
    }

    [Fact]
    public async Task TwoRulesWithOneLabelAreChosenApartByTheirKeys()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        TimingAggregateRow[] rules =
        [
            new("guid-1", "Plural", 200, 0.25, 4) { Kind = "morph_rule" },
            new("guid-2", "Plural", 100, 0.125, 2) { Kind = "morph_rule" },
        ];
        var timing = await LoadedTiming(NineWords, Kinds, rules);

        Assert.Equal("guid-1", timing.SelectedRule?.Key);
        await timing.ChooseRuleCommand.ExecuteAsync(rules[1]);

        Assert.Equal("guid-2", timing.SelectedRule?.Key);
        Assert.Equal("Plural", timing.SelectedRuleName);
        Assert.Equal([false, true], timing.RuleRows.Select(row => row.IsChosen));
        Assert.StartsWith("100 ms", timing.RuleSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARulesCostliestWordsSayTheyAreTheRulesTimeNotTheWordsWholeTime()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming(NineWords, Kinds, Rules,
            [new WordRuleTiming("mwalimu", 180, 900) { WordTimeMs = 700 },
                new WordRuleTiming("hawajafika", 34, 200) { WordTimeMs = 48 }]);

        Assert.Equal(["180 ms of its 700 ms", "34 ms of its 48 ms"],
            timing.CostliestRuleWordRows.Select(word => word.TimeText));
    }

    [Fact]
    public async Task CostliestRuleWordsUseTheirExactWholeParseTimeWhenMillisecondsAreRoundedOrMissing()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming([("rounded", 0), ("nanoseconds-only", null)], Kinds, Rules,
            [
                new WordRuleTiming("rounded", 0.4, 1) { WordTimeMs = 0.8 },
                new WordRuleTiming("nanoseconds-only", 0.2, 1) { WordTimeMs = 0.5 },
            ], elapsedNs: [800_000, 500_000]);

        Assert.Equal(["0.4 ms of its 0.8 ms", "0.2 ms of its 0.5 ms"],
            timing.CostliestRuleWordRows.Select(word => word.TimeText));
    }

    [Fact]
    public async Task UnattributedTimeAndOverrunAreShownSeparately()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming([("dogs", 10), ("cats", 20)],
            [new TimingAggregateRow("morph_rule", "morph_rule", 17, 17d / 30, 2)], [],
            attribution: new WordTimeAttribution(2, 30, 17, 15, 0.5, 2, true));

        var notAttributed = timing.KindShares.Single(share => share.Source is null);
        Assert.Equal("15 ms", notAttributed.TimeText);
        Assert.Equal("50%", notAttributed.ShareText);
        Assert.Equal("Not attributed: 15 ms (50%).", timing.NotAttributedText);
        Assert.Equal("Overrun: 2 ms.", timing.OverrunText);
    }

    [Fact]
    public async Task MissingObjectTimingStaysNotRecorded()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming([("dogs", 10)], [], [],
            attribution: new WordTimeAttribution(1, 10, 0, null, null, 0, false));

        var notAttributed = Assert.Single(timing.KindShares);
        Assert.Equal("Not attributed", notAttributed.Label);
        Assert.Equal("Not recorded", notAttributed.TimeText);
        Assert.Equal("Not recorded", notAttributed.ShareText);
        Assert.Equal("Not attributed: Not recorded.", timing.NotAttributedText);
        Assert.Empty(timing.OverrunText);
    }

    [Fact]
    public async Task MissingPercentilesAreNamedAsNotRecorded()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming(NineWords, Kinds, Rules, medianMs: null, percentile95Ms: null);

        Assert.Equal("Median not recorded · 95th percentile not recorded", timing.PercentileSummary);
    }

    [Fact]
    public async Task ShareUsesTheWholeWordTimeDenominatorFromTheResponse()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming([("mwalimu", 800)],
            [new TimingAggregateRow("morph_rule", "morph_rule", 300, 0.375, 1) { Calls = 4 }], [],
            attribution: new WordTimeAttribution(1, 800, 300, 500, 0.625, 0, false));

        Assert.Equal("800 ms", timing.HeadlineTotal);
        Assert.Equal("38%", timing.KindShares.Single(share => share.Source is not null).ShareText);
        Assert.Equal("Not attributed: 500 ms (62%).", timing.NotAttributedText);
    }

    [Fact]
    public async Task RequestedRuleIsResolvedByItsExactKeyAndNotByItsLabel()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        TimingAggregateRow[] rules =
        [
            new("guid-1", "Plural", 200, 0.25, 4) { Kind = "morph_rule" },
            new("guid-2", "Plural", 100, 0.125, 2) { Kind = "morph_rule" },
        ];
        var timing = await LoadedTiming(NineWords, Kinds, rules,
            requestedRule: new TraceTimingKey("morph_rule", "guid-2"));

        Assert.Equal("guid-2", timing.SelectedRule?.Key);
        Assert.Equal("Plural", timing.SelectedRuleName);
        Assert.True(timing.RuleRows.Single(row => row.Share.Source?.Key == "guid-2").IsChosen);
    }

    [Fact]
    public async Task MissingRequestedRuleDoesNotSelectAnotherRow()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        TimingAggregateRow[] rules =
        [
            new("guid-1", "Plural", 200, 0.25, 4) { Kind = "morph_rule" },
            new("guid-2", "Plural", 100, 0.125, 2) { Kind = "morph_rule" },
        ];
        var timing = await LoadedTiming(NineWords, Kinds, rules,
            requestedRule: new TraceTimingKey("morph_rule", "missing-key"));

        Assert.Null(timing.SelectedRuleRow);
        Assert.Equal("No stored timing for this rule.", timing.RuleSummary);
    }

    [Fact]
    public async Task WordsWithoutAParseTimeAreCountedApartFromTheMeasuredOnes()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming([("dogs", 10), ("cats", null), ("birds", 6)], [], []);

        Assert.Equal("for 2 of these 3 words", timing.HeadlineTotalCaption);
        Assert.Equal("Every share is of the 16 ms 2 of these 3 words took to parse; 1 has no parse time.",
            timing.ShareDenominatorText);
    }

    [Fact]
    public async Task DetailedWordStatisticsFollowTheTimingSelection()
    {
        var timing = await LoadedTiming(NineWords, Kinds, Rules);

        Assert.Equal(timing.SelectedWords, timing.Statistics.WordScope);
    }

    [Fact]
    public async Task ARequestedRuleIdentitySelectsOnlyTheMatchingKindAndKey()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        TimingAggregateRow[] rules =
        [
            new("same-key", "Morph rule", 200, 0.25, 4) { Kind = "morph_rule", IdentityQuality = "authored" },
            new("same-key", "Phon rule", 100, 0.125, 2) { Kind = "phon_rule", IdentityQuality = "authored" },
        ];
        var timing = await LoadedTiming(NineWords, Kinds, rules, requestedRule: new TraceTimingKey("phon_rule", "same-key"));

        Assert.Equal("same-key", timing.SelectedRule?.Key);
        Assert.Equal("Phon rule", timing.SelectedRuleName);
        Assert.Equal(new TraceTimingKey("phon_rule", "same-key"), timing.SelectedRuleRow is { } row
            ? new TraceTimingKey(row.Kind, row.Key) : null);
        Assert.Equal([false, true], timing.RuleRows.Select(row => row.IsChosen));
    }

    [Fact]
    public void TheRuleTableNamesItsDenominatorAndShowsNoAttemptsColumn()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var timing = await LoadedTiming(NineWords, Kinds, Rules);
            var window = new Window { Width = 1240, Height = 1400, Content = new TimingPage(timing) };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var shown = window.GetLogicalDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
                Assert.Contains("SHARE OF 800 ms", shown);
                Assert.Contains("TIME", shown);
                Assert.Contains("Not attributed", shown);
                Assert.Contains("Every share is of the 800 ms these 9 words took to parse.", shown);
                Assert.DoesNotContain("ATTEMPTS", shown);
                Assert.DoesNotContain("SHARE OF TIME", shown);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void TheRuleTableHidesKindAndUsesTheSpaceWhenTheInspectorOpens()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var timing = await LoadedTiming(NineWords, Kinds, Rules);
            var window = new Window { Width = 1240, Height = 1400, Content = new TimingPage(timing) };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var header = Assert.Single(window.GetVisualDescendants().OfType<Grid>(), grid =>
                    grid.Classes.Contains("timingTableHeader"));
                var ruleGrids = window.GetVisualDescendants().OfType<Grid>()
                    .Where(grid => grid.Classes.Contains("timingColumns") &&
                        grid.FindAncestorOfType<Button>()?.Classes.Contains("timingRuleRow") == true)
                    .ToArray();
                Assert.NotEmpty(ruleGrids);
                var kindCells = ruleGrids.SelectMany(ruleGrid => ruleGrid.GetVisualDescendants().OfType<TextBlock>())
                    .Where(text => text.Text == "Morphological rules").ToArray();
                Assert.NotEmpty(kindCells);
                var kindHeader = Assert.Single(header.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "KIND");
                var ruleWidth = header.ColumnDefinitions[0].ActualWidth;
                var kindWidth = header.ColumnDefinitions[1].ActualWidth;
                Assert.True(kindHeader.IsEffectivelyVisible);
                Assert.All(kindCells, cell => Assert.True(cell.IsEffectivelyVisible));

                timing.Context.OpenInspector(InspectorSubject.Rule(new TraceTimingKey("morph_rule", "rule")));
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();

                Assert.False(kindHeader.IsEffectivelyVisible);
                Assert.All(kindCells, cell => Assert.False(cell.IsEffectivelyVisible));
                Assert.True(header.ColumnDefinitions[1].ActualWidth < kindWidth);
                Assert.True(header.ColumnDefinitions[0].ActualWidth > ruleWidth);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void TimingControlsWrapWithinPageAndCallsOpenOnRequest()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var timing = await LoadedTiming(NineWords, Kinds, Rules, stepLimitedWord: "mwalimu");
            var window = new Window { Width = 1040, Height = 1400, Content = new TimingPage(timing) };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var strip = window.GetLogicalDescendants().OfType<WrapPanel>()
                    .Single(panel => AutomationProperties.GetName(panel) == "Timing controls");
                var visibleControls = strip.Children.OfType<Control>().Where(control => control.IsEffectivelyVisible).ToArray();
                var rightmost = visibleControls.Max(control =>
                    control.TranslatePoint(default, window)!.Value.X + control.Bounds.Width);
                var page = window.GetLogicalDescendants().OfType<Border>()
                    .Single(border => border.Classes.Contains("timingPage"));
                var pageRight = page.TranslatePoint(default, window)!.Value.X + page.Bounds.Width - page.Padding.Right;
                Assert.True(rightmost <= pageRight,
                    $"Timing controls extend to {rightmost:F1} px past the page's {pageRight:F1} px content edge.");
                Assert.All(visibleControls, control =>
                {
                    var origin = control.TranslatePoint(default, window)!.Value;
                    Assert.True(origin.Y >= strip.TranslatePoint(default, window)!.Value.Y &&
                        origin.Y + control.Bounds.Height <= strip.TranslatePoint(default, window)!.Value.Y + strip.Bounds.Height,
                        $"{AutomationProperties.GetName(control) ?? control.GetType().Name} escapes the Timing control strip.");
                });

                var calls = window.GetLogicalDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == "Show calls per kind");
                Assert.True(calls.IsTabStop);
                Assert.DoesNotContain("420 calls", VisibleText(window));
                calls.Command!.Execute(calls.CommandParameter);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Contains("420 calls", VisibleText(window));
                Assert.Contains("70 calls", VisibleText(window));
                Assert.Contains("Not counted", VisibleText(window));
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void NarrowRuleTableCellsDoNotRunIntoOneAnother()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var timing = await LoadedTiming(NineWords, Kinds, Rules);
            var window = new Window { Width = 720, Height = 1400, Content = new TimingPage(timing) };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var table = window.GetVisualDescendants().OfType<ItemsControl>()
                    .Single(control => AutomationProperties.GetName(control) == "Timing by rule");
                var row = table.GetVisualDescendants().OfType<Button>()
                    .First(button => button.Classes.Contains("timingRuleRow"));
                var grid = row.GetVisualDescendants().OfType<Grid>()
                    .First(candidate => candidate.Classes.Contains("timingColumns"));
                var cells = grid.Children.OfType<Control>()
                    .OrderBy(cell => cell.TranslatePoint(default, window)!.Value.X).ToArray();

                foreach (var (left, right) in cells.Zip(cells.Skip(1)))
                {
                    var leftEdge = left.TranslatePoint(default, window)!.Value.X + left.Bounds.Width;
                    var rightEdge = right.TranslatePoint(default, window)!.Value.X;
                    Assert.True(leftEdge <= rightEdge + 0.5,
                        $"Timing rule cells '{left.GetType().Name}' and '{right.GetType().Name}' overlap by {leftEdge - rightEdge:F1} px.");
                }
                foreach (var cell in cells)
                {
                    var cellRight = cell.TranslatePoint(default, window)!.Value.X + cell.Bounds.Width;
                    foreach (var text in cell.GetVisualDescendants().OfType<TextBlock>()
                                 .Where(text => text.IsEffectivelyVisible && !string.IsNullOrWhiteSpace(text.Text)))
                    {
                        var textRight = text.TranslatePoint(default, window)!.Value.X +
                            text.TextLayout.WidthIncludingTrailingWhitespace;
                        Assert.True(textRight <= cellRight + 0.5,
                            $"'{text.Text}' runs past its Timing rule cell by {textRight - cellRight:F1} px.");
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
    public void AWarningMarkRequiresAnIdentityMatchWithAStoredFinding()
    {
        var finding = SeededGrammarFindings.All().First(warning =>
            warning.Code == "hc-stem-no-grammatical-category");
        var namedEntry = Assert.Single(finding.Subject);
        var matched = new TimingAggregateRow(namedEntry.FieldWorksGuid!, namedEntry.Title!, 10, 0.5, 1)
        {
            Kind = "lex_entry",
            IdentityQuality = "authored",
        };
        var sameNameDifferentIdentity = matched with { Key = "99999999-9999-4999-8999-999999999999" };
        var rootLookup = new TimingAggregateRow("roots", "Root lookup", 10, 0.5, 1)
        {
            Kind = "root_index",
            IdentityQuality = "structural",
        };

        Assert.Equal(finding.Code, TimingPageModel.MatchingWarningCode(matched, [finding]));
        Assert.Null(TimingPageModel.MatchingWarningCode(sameNameDifferentIdentity, [finding]));
        Assert.Null(TimingPageModel.MatchingWarningCode(rootLookup, [finding]));
        var mark = new TimingRuleRow(new TimingShare("Rule", "lex_entry", 10, 0.5, 1, matched), false,
            finding.Code);
        Assert.True(mark.HasGrammarWarning);
        Assert.Equal("A grammar warning names this rule · Open the warning", TimingRuleRow.GrammarWarningTooltip);
    }

    [Fact]
    public async Task OpeningTheTimingWarningActionShowsItsKindOnTheWarningsPage()
    {
        var finding = SeededGrammarFindings.All().First(warning =>
            warning.Code == "hc-stem-no-grammatical-category");
        var fake = new FakeCommandClient();
        fake.StoredGrammarCheckIs(new GrammarCheckResponse([finding], HasBaseline: true));
        var context = WorkspaceContextTests.NewContext(fake);
        var warnings = new WarningsPageModel(context);
        var timing = new TimingPageModel(context);
        await context.OpenProjectAsync(ProjectPath);

        timing.OpenGrammarWarningCommand.Execute(new TimingRuleRow(
            new TimingShare("Stem", "lex_entry", 10, 0.5, 1, null), false, finding.Code));

        var row = Assert.Single(warnings.Grammar.Warnings.Rows.Cast<GrammarWarningRowViewModel>());
        Assert.Equal(WorkspacePage.Warnings, context.CurrentPage);
        Assert.True(row.IsOpen);
    }

    [Fact]
    public async Task ARequestedRuleAddressStaysUnresolvedWhenNoRowHasThatIdentity()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var requestedRule = new TraceTimingKey("morph_rule", "subject-guid");
        var timing = await LoadedTiming(NineWords, Kinds, Rules, requestedRule: requestedRule);

        Assert.Equal(requestedRule, timing.Focus!.Rule);
        Assert.Null(timing.SelectedRuleRow);
        Assert.Equal("No stored timing for this rule.", timing.RuleSummary);
    }

    private static string[] VisibleText(Window window) => window.GetLogicalDescendants().OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? string.Empty).ToArray();

    private static async Task<TimingPageModel> LoadedTiming((string Word, int? Ms)[] words,
        TimingAggregateRow[] kinds, TimingAggregateRow[] rules, WordRuleTiming[]? costliest = null,
        WordTimeAttribution? attribution = null, IReadOnlyList<long?>? elapsedNs = null,
        TraceTimingKey? requestedRule = null, double? medianMs = 9, double? percentile95Ms = 700,
        string? stepLimitedWord = null)
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        var timing = new TimingPageModel(context);
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            new TimingResponse("assessment-parse", request.WordSet, request.By, words.Length, medianMs, percentile95Ms, [],
                request.By == "kind" ? kinds : rules, request.Rule is null ? [] : costliest ?? [])
            {
                Words = words.Select((word, index) => new TimingWordRow(word.Word, word.Ms,
                    word.Word == stepLimitedWord ? TimingCompletion.StepLimit : TimingCompletion.Finished)
                {
                    ElapsedNs = elapsedNs is not null && index < elapsedNs.Count ? elapsedNs[index] : null,
                }).ToArray(),
                Attribution = attribution ?? AttributionFor(words, kinds),
            })));
        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        await context.EvidencePublication;
        if (requestedRule is null)
        {
            await timing.SelectWordSetCommand.ExecuteAsync("slowest");
        }
        else
        {
            context.OpenTiming(words.Select(word => word.Word).ToArray(), requestedRule);
            await timing.LoadFocusedTimingCommand.ExecutionTask!;
        }
        return timing;
    }

    private static WordTimeAttribution AttributionFor((string Word, int? Ms)[] words, TimingAggregateRow[] kinds)
    {
        var wordTime = words.Where(word => word.Ms is not null).Sum(word => word.Ms!.Value);
        var attributed = kinds.Sum(row => row.SelfMs);
        var notAttributed = wordTime - attributed;
        var nonNegativeNotAttributed = Math.Max(0, notAttributed);
        return new WordTimeAttribution(words.Count(word => word.Ms is not null), wordTime, attributed,
            kinds.Length == 0 ? null : nonNegativeNotAttributed,
            kinds.Length > 0 && wordTime > 0 ? nonNegativeNotAttributed / wordTime : null,
            Math.Max(0, -notAttributed), notAttributed < 0);
    }

    private static AssessCommandResponse Assessment() => new(
        new BaselineCaptureResponse(
            new BaselineToken("project-1", "sha256:" + new string('a', 64), "1", "2026-09-05T11:02:00Z",
                "sha256:" + new string('b', 64)),
            ProjectPath, DateTimeOffset.UtcNow, false, false),
        new SelectionProjection([], []), [], "summary")
    {
        InvocationId = "invocation/one",
        Measurements = [new ProducedAssessmentReference("assessment-parse", "ParseTime", "invocation/one")],
    };
}
