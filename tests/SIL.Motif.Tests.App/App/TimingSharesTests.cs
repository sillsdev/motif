using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that every share on the Timing page is of the chosen words' whole parser work, that the work no rule
/// recorded is shown as unattributed, that work is counted in steps and never shown as milliseconds, and that no
/// count is shown without saying what it counts.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "Unit")]
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

    [Fact]
    public void ParserWorkSharesUseCompactPercentSignsInEnglish()
    {
        using var culture = new CultureScope(CultureInfo.InvariantCulture);
        Assert.Equal("90%", TimingShare.FormatPercent(0.9));
        Assert.Equal("<1%", TimingShare.FormatPercent(0.001));
    }

    [Fact]
    public void APairedShareWithOneSideMissingNamesWhichSideInItsTextAndNote()
    {
        var both = new TimingShare("Rule", "morph_rule", 480, 0.6, 9, null) { ProposalShareText = "12%" };
        var noProposalShare = both with { ProposalShareText = string.Empty };
        var noTodayShare = both with { Share = null };

        Assert.Contains("→", both.ShownShareText);
        Assert.Null(both.ShownShareNote);
        Assert.Equal(both.ShareText, noProposalShare.ShownShareText);
        Assert.Equal(string.Format(MotifAppStrings.PairedProposalNotMeasuredNote, both.ShareText),
            noProposalShare.ShownShareNote);
        Assert.Equal("Proposal 12%", noTodayShare.ShownShareText);
        Assert.Equal(string.Format(MotifAppStrings.PairedTodayNotMeasuredNote, "Proposal 12%"),
            noTodayShare.ShownShareNote);
    }

    [Theory]
    [InlineData(795, "0.8 s")]
    [InlineData(1140.942292, "1.1 s")]
    [InlineData(6.080588, "<0.1 s")]
    [InlineData(0.004, "<0.1 s")]
    [InlineData(9999.6, "10.0 s")]
    [InlineData(10000, "10.0 s")]
    [InlineData(10832, "10.8 s")]
    public void RecordedDurationsAreRoundedForReadingNeverShownAsRawFloats(
        double milliseconds, string expected)
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal(expected, TimingShare.FormatDuration(milliseconds));
    }

    // The words' parser work are ten for each recorded millisecond, so a fixture reads the same in either unit.
    private static long? WorkOf(int? milliseconds) => milliseconds * 10L;

    private static readonly TimingWorkAggregate[] KindWork =
    [
        new("morph_rule", "morph_rule", "morph_rule", 4_800, false),
        new("phon_rule", "phon_rule", "phon_rule", 1_600, false),
        new("root_index", "root_index", "root_index", 400, false),
        new("unattributed", "Not attributed", "unattributed", 1_200, true),
    ];

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
    public async Task KindSharesAreOfTheWordsWholeWorkWithOtherWorkApart()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming(NineWords, Kinds, Rules, work: KindWork);

        Assert.Equal(
            [("Morphological rules", "60%", "4,800 parser work"), ("Phonological rules", "20%", "1,600 parser work"),
                ("Root lookup", "5%", "400 parser work"), ("Not attributed", "15%", "1,200 parser work")],
            timing.KindShares.Select(share => (share.Label, share.ShareText, share.WorkText)));
        Assert.Null(timing.KindShares[^1].Source);
        Assert.Equal(1d, timing.KindShares.Sum(share => share.Share!.Value), precision: 6);
        Assert.Equal("Every share is of the 8,000 parser work these 9 words needed to parse.", timing.ShareDenominatorText);
        Assert.Equal("Not attributed: 1,200 parser work (15%).", timing.NotAttributedText);
        Assert.Equal(["420 times", "70 times", "Not counted"], timing.KindCallShares.Select(share => share.CallsText));
    }

    [Fact]
    public void TheHeadlineCountsParserWorkAndNothingOnThePageIsInMilliseconds()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var timing = await LoadedTiming(NineWords, Kinds, Rules, work: KindWork);
            var window = new MotifTestWindow { Width = 1240, Height = 1400, Content = new TimingPage(timing) };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("8,000 parser work", timing.HeadlineTotal);
                Assert.Equal("90 parser work", timing.HeadlineMedian);
                Assert.Equal("Median 90 parser work · 95th percentile 7,000 parser work", timing.PercentileSummary);
                Assert.DoesNotContain(VisibleText(window), text => System.Text.RegularExpressions.Regex.IsMatch(
                    text, @"\d\s?(ms|s)\b|millisecond", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
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

        Assert.Equal("5 counted; some counts unavailable", share.CallsText);
    }

    [Fact]
    public async Task RuleSharesUseTheSameWholeWorkAndCountNoAttempts()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming(NineWords, Kinds, Rules);

        Assert.Equal("SHARE OF 8,000 parser work", timing.RuleShareHeader);
        Assert.Equal([("Subject agreement", "Morphological rules", "2,880", "36%", "6"),
                ("Vowel harmony", "Phonological rules", "1,200", "15%", "7")],
            timing.RuleShares.Select(share => (share.Label, share.KindLabel, share.WorkCountText, share.ShareText,
                share.WordsText)));
        Assert.Equal("2,880 parser work · 36% of these 9 words' 8,000 parser work · recorded in 6 words", timing.RuleSummary);
        Assert.DoesNotContain("attempt", timing.RuleSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WordCardTimingSharesUseWholePercentagesOfParserWork()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("36% · 2,880 parser work", new TimingShareViewModel("Subject agreement", 0.36, 2880).ShareLabel);
        Assert.Equal("15% · 1 parser work", new TimingShareViewModel("Vowel harmony", 0.15, 1).ShareLabel);
        Assert.Equal("<1% · 3 parser work", new TimingShareViewModel("Rare rule", 0.0003, 3).ShareLabel);
    }

    // The milliseconds rows deliberately disagree with the steps, so a share read from time fails these.
    private static TimingResponse WordTiming(int wordCount, WordWorkSummary work,
        IReadOnlyList<TimingWorkAggregate> steps) => new("assessment/one", "all", "rule", wordCount, 10, 10, [],
        [
            new TimingAggregateRow("rule/a", "Subject agreement", 1, 0.1, 1) { Kind = "morph_rule" },
            new TimingAggregateRow("rule/b", "Vowel harmony", 9, 0.9, 1) { Kind = "phon_rule" },
        ], [])
    {
        Work = work,
        ParserWorkAggregates = steps,
        Attribution = new WordTimeAttribution(1, 10, 10, 0, 0, 0, false),
    };

    private static TimingWorkAggregate RuleSteps(string name, long steps, string kind = "morph_rule") =>
        new("rule/" + name, name, kind, steps, false) { IdentityQuality = "authored" };

    private static readonly TimingWorkAggregate Unattributed =
        new("unattributed", "Not attributed", "unattributed", 100, true);

    private static ResultsTokenViewModel Card(TimingResponse? timing, bool responseAvailable = true)
    {
        var card = new ResultsTokenViewModel("Text", 1, new TextToken("hawajafika", "hawajafika", null, null), null);
        card.SetTimingEvidence(timing, responseAvailable);
        return card;
    }

    [Fact]
    public void WordCardSharesAreRuleStepsOverTheWordsTotalSteps()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var card = Card(WordTiming(1, new WordWorkSummary(1, 1000, 1000, 1000),
            [RuleSteps("Subject agreement", 600), RuleSteps("Vowel harmony", 300, "phon_rule"), Unattributed]));

        Assert.Equal([("Subject agreement", "60% · 600 parser work"), ("Vowel harmony", "30% · 300 parser work")],
            card.TimingShares.Select(share => (share.Name, share.ShareLabel)));
        Assert.Equal(0.1, card.NotAttributedShare!.Value, 6);
        Assert.Equal("Not attributed · 10%", card.NotAttributedShareLabel);
        Assert.Equal("Every share is of the 1,000 parser work this word needed to parse.", card.TimingShareBasisLabel);
    }

    [Fact]
    public void WordCardSaysParserWorkAreNotRecordedInsteadOfShowingTimeShares()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var card = Card(WordTiming(1, WordWorkSummary.None, []));

        Assert.True(card.HasTimingEvidence);
        Assert.False(card.HasTimingShares);
        Assert.Null(card.NotAttributedShare);
        Assert.Equal("Parser work not recorded for this word", card.NotAttributedShareLabel);
        Assert.Equal(string.Empty, card.TimingShareBasisLabel);
    }

    [Fact]
    public void WordCardSaysWhenOnlyTheStepsByRuleWereNotKept()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var card = Card(WordTiming(1, new WordWorkSummary(1, 500, 500, 500), []));

        Assert.False(card.HasTimingShares);
        Assert.Equal("Parser work by rule not recorded for this word", card.NotAttributedShareLabel);
    }

    [Fact]
    public void WordCardWithoutAResponseStillSaysTimingIsUnavailable()
    {
        Assert.Equal("Timing unavailable for this word", Card(null, responseAvailable: false).NotAttributedShareLabel);
    }

    [Fact]
    public void WordCardWithStepsForOnlySomeWordsSaysWhatTheSharesAreOver()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var card = Card(WordTiming(3, new WordWorkSummary(2, 2000, 1000, 1000),
            [RuleSteps("Subject agreement", 1500), Unattributed with { ParserWork = 500 }]));

        Assert.Equal("75% · 1,500 parser work", Assert.Single(card.TimingShares).ShareLabel);
        Assert.Equal("Not attributed · 25%", card.NotAttributedShareLabel);
        Assert.Equal("Every share is of the 2,000 parser work 2 of these 3 words needed to parse; 1 has no parser work recorded.",
            card.TimingShareBasisLabel);
    }

    [Fact]
    public void WordCardAddsUpRulesBeyondTheTenLargestAsOtherRules()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        TimingWorkAggregate[] rules = [.. Enumerable.Range(1, 12).Select(index => RuleSteps("Rule " + index, 100 - index))];
        var card = Card(WordTiming(1, new WordWorkSummary(1, 1200, 1200, 1200), [.. rules, Unattributed with { ParserWork = 1200 - rules.Sum(r => r.ParserWork) }]));

        Assert.Equal(11, card.TimingShares.Count);
        Assert.Equal("Other rules", card.TimingShares[^1].Name);
        Assert.Equal((89 + 88) / 1200d, card.TimingShares[^1].Share, 6);
        Assert.InRange(card.TimingShares.Sum(share => share.Share) + card.NotAttributedShare!.Value, 0.999999, 1.000001);
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
        Assert.StartsWith("1,000 parser work", timing.RuleSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARulesCostliestWordsSayTheyAreTheRulesWorkNotTheWordsWholeWork()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming(NineWords, Kinds, Rules,
            [new WordRuleTiming("mwalimu", 180, 900) { WordTimeMs = 700, ParserWork = 1_800, WordParserWork = 7_000 },
                new WordRuleTiming("hawajafika", 34, 200) { WordTimeMs = 48, ParserWork = 340, WordParserWork = 480 }]);

        Assert.Equal(["1,800 parser work of its 7,000 parser work", "340 parser work of its 480 parser work"],
            timing.CostliestRuleWordRows.Select(word => word.TimeText));
    }

    [Fact]
    public async Task ACostliestWordWithNoRuleWorkCountSaysParserWorkNotRecordedAndNeverMilliseconds()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming(NineWords, Kinds, Rules,
            [new WordRuleTiming("mwalimu", 180, 900) { WordTimeMs = 700 }]);

        Assert.Equal(["parser work not recorded"], timing.CostliestRuleWordRows.Select(word => word.TimeText));
    }

    [Fact]
    public async Task UnattributedWorkIsShownApart()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming([("dogs", 10), ("cats", 20)],
            [new TimingAggregateRow("morph_rule", "morph_rule", 17, 17d / 30, 2)], [],
            work: [new("morph_rule", "morph_rule", "morph_rule", 150, false),
                new("unattributed", "Not attributed", "unattributed", 150, true)]);

        var notAttributed = timing.KindShares.Single(share => share.IsNotAttributed);
        Assert.Equal("150 parser work", notAttributed.WorkText);
        Assert.Equal("50%", notAttributed.ShareText);
        Assert.Equal("Not attributed: 150 parser work (50%).", timing.NotAttributedText);
    }

    [Fact]
    public async Task WorkNotKeptByObjectSaysSoInsteadOfInventingShares()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming([("dogs", 10)], [new TimingAggregateRow("morph_rule", "morph_rule", 4, 0.4, 1)],
            [new TimingAggregateRow("Plural", "Plural", 4, 0.4, 1) { Kind = "morph_rule" }], work: []);

        Assert.Equal("100 parser work", timing.HeadlineTotal);
        Assert.Empty(timing.KindShares);
        Assert.Equal("Parser work by kind and by rule was not kept for these words, so no shares are shown. Parse all words again to keep them.",
            timing.ShareDenominatorText);
        Assert.Equal("Not attributed: Not recorded.", timing.NotAttributedText);
        var rule = Assert.Single(timing.RuleRows).Share;
        Assert.Equal("Plural", rule.Label);
        Assert.Equal("Not recorded", rule.WorkCountText);
        Assert.Equal(string.Empty, rule.ShareText);
    }

    [Fact]
    public async Task MissingPercentilesAreNamedAsNotRecorded()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming(NineWords, Kinds, Rules, summary: new WordWorkSummary(9, 8_000, null, null));

        Assert.Equal("Median not recorded · 95th percentile not recorded", timing.PercentileSummary);
    }

    [Fact]
    public async Task ShareUsesTheWholeWorkDenominatorFromTheResponse()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming([("mwalimu", 80)],
            [new TimingAggregateRow("morph_rule", "morph_rule", 300, 0.375, 1) { Calls = 4 }], [],
            work: [new("morph_rule", "morph_rule", "morph_rule", 300, false),
                new("unattributed", "Not attributed", "unattributed", 500, true)]);

        Assert.Equal("800 parser work", timing.HeadlineTotal);
        Assert.Equal("38%", timing.KindShares.Single(share => share.Source is not null).ShareText);
        Assert.Equal("Not attributed: 500 parser work (62%).", timing.NotAttributedText);
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
        Assert.Equal("Every share is of the 160 parser work 2 of these 3 words needed to parse; 1 has no parser work recorded.",
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
            var window = new MotifTestWindow { Width = 1240, Height = 1400, Content = new TimingPage(timing) };
            try
            {
                window.Show();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                var shown = window.GetLogicalDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible).Select(text => text.Text).ToArray();
                Assert.Contains("SHARE OF 8,000 parser work", shown);
                Assert.Contains("PARSER WORK", shown);
                Assert.Contains("Not attributed", shown);
                Assert.Contains("Every share is of the 8,000 parser work these 9 words needed to parse.", shown);
                Assert.DoesNotContain("ATTEMPTS", shown);
                Assert.DoesNotContain("SHARE OF TIME", shown);
                Assert.DoesNotContain("TIME", shown);
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
            var window = new MotifTestWindow { Width = 1240, Height = 1400, Content = new TimingPage(timing) };
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
            var timing = await LoadedTiming(NineWords, Kinds, Rules, parserWorkLimitedWord: "mwalimu");
            var window = new MotifTestWindow { Width = 1040, Height = 1400, Content = new TimingPage(timing) };
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
                    .Single(button => AutomationProperties.GetName(button) == "Show counts per kind");
                Assert.True(calls.IsTabStop);
                Assert.DoesNotContain("420 times", VisibleText(window));
                calls.Command!.Execute(calls.CommandParameter);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Contains("420 times", VisibleText(window));
                Assert.Contains("70 times", VisibleText(window));
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
            var window = new MotifTestWindow { Width = 720, Height = 1400, Content = new TimingPage(timing) };
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

    [Fact]
    public async Task AChosenPartOfALargeRunGivesNoAdviceToParseAgain()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming([("dogs", 10)], [new TimingAggregateRow("morph_rule", "morph_rule", 4, 0.4, 1)],
            [new TimingAggregateRow("Plural", "Plural", 4, 0.4, 1) { Kind = "morph_rule" }], work: [], aggregatesAvailable: false);

        Assert.Equal("Parser work by kind and by rule was not kept for these words, so no shares are shown.",
            timing.ShareDenominatorText);
        Assert.DoesNotContain("Parse all words", timing.ShareDenominatorText);
    }

    private static async Task<TimingPageModel> LoadedTiming((string Word, int? Ms)[] words,
        TimingAggregateRow[] kinds, TimingAggregateRow[] rules, WordRuleTiming[]? costliest = null,
        IReadOnlyList<long?>? elapsedNs = null, TraceTimingKey? requestedRule = null,
        string? parserWorkLimitedWord = null, IReadOnlyList<TimingWorkAggregate>? work = null, WordWorkSummary? summary = null,
        bool aggregatesAvailable = true)
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        var timing = new TimingPageModel(context);
        var wordWork = words.Select(word => WorkOf(word.Ms)).ToArray();
        var wholeWork = wordWork.Sum() ?? 0;
        // By default the parser work by object are the rules given, with the rest of the words' work unattributed.
        work ??= wholeWork == 0 ? [] :
        [
            .. rules.Select(rule => new TimingWorkAggregate(rule.Key, rule.Name, rule.Kind, (long)(rule.SelfMs * 10), false)
                { IdentityQuality = rule.IdentityQuality, Scope = rule.Scope }),
            new TimingWorkAggregate("unattributed", "Not attributed", "unattributed",
                Math.Max(0, wholeWork - rules.Sum(rule => (long)(rule.SelfMs * 10))), true),
        ];
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            new TimingResponse("assessment-parse", request.WordSet, request.By, words.Length, 9, 700, [],
                request.By == "kind" ? kinds : rules, request.Rule is null ? [] : costliest ?? [])
            {
                Words = words.Select((word, index) => new TimingWordRow(word.Word, word.Ms,
                    word.Word == parserWorkLimitedWord ? TimingCompletion.WorkLimit : TimingCompletion.Finished)
                {
                    ElapsedNs = elapsedNs is not null && index < elapsedNs.Count ? elapsedNs[index] : null,
                    ParserWork = wordWork[index],
                }).ToArray(),
                Attribution = AttributionFor(words, kinds),
                ParserWorkAggregates = work,
                ObjectTimingAggregatesAvailable = aggregatesAvailable,
                Work = summary ?? TimingAggregation.SummarizeWork(wordWork),
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
