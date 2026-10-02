using System.Globalization;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
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
/// rule recorded is shown apart as other time, and that no count is shown without saying what it counts.
/// </summary>
[Collection(AvaloniaHeadlessCollection.Name)]
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
        new("morph_rule", "morph_rule", 480, 480d / 800, 9),
        new("phon_rule", "phon_rule", 160, 160d / 800, 7),
        new("root_index", "root_index", 40, 40d / 800, 9),
    ];

    private static readonly TimingAggregateRow[] Rules =
    [
        new("Subject agreement", "Subject agreement", 288, 288d / 800, 6) { Kind = "morph_rule" },
        new("Vowel harmony", "Vowel harmony", 120, 120d / 800, 7) { Kind = "phon_rule" },
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
        Assert.Equal("Every share is of the 0.8 s these 9 words took to parse.", timing.ShareDenominatorText);
        Assert.Equal("120 ms was not attributed to rules or lookups.", timing.OtherTimeText);
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
        Assert.Equal("2 ms", timing.HeadlineTotal);
    }

    [Fact]
    public async Task RuleSharesUseTheSameWholeParseTimeAndCountNoAttempts()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming(NineWords, Kinds, Rules);

        Assert.Equal("SHARE OF 0.8 s", timing.RuleShareHeader);
        Assert.Equal([("Subject agreement", "Morphological rules", "288 ms", "36%", "6"),
                ("Vowel harmony", "Phonological rules", "120 ms", "15%", "7")],
            timing.RuleShares.Select(share => (share.Label, share.KindLabel, share.TimeText, share.ShareText,
                share.WordsText)));
        Assert.Equal("288 ms · 36% of these 9 words' 0.8 s · recorded in 6 words", timing.RuleSummary);
        Assert.DoesNotContain("attempt", timing.RuleSummary, StringComparison.OrdinalIgnoreCase);
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
            [new WordRuleTiming("mwalimu", 180, 900), new WordRuleTiming("hawajafika", 34, 200)]);

        Assert.Equal(["180 ms of its 700 ms", "34 ms of its 48 ms"],
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
        Assert.Equal("15 ms was not attributed to rules or lookups. Object time exceeded word time by 2 ms " +
            "across the words.", timing.OtherTimeText);
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
        Assert.Equal("Not attributed time was not recorded for these words.", timing.OtherTimeText);
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
        var timing = await LoadedTiming(NineWords, Kinds, rules, requestedRule: "guid-2");

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
        var timing = await LoadedTiming(NineWords, Kinds, rules, requestedRule: "missing-key");

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
                Assert.Contains("SHARE OF 0.8 s", shown);
                Assert.Contains("TIME", shown);
                Assert.Contains("Not attributed", shown);
                Assert.Contains("Every share is of the 0.8 s these 9 words took to parse.", shown);
                Assert.DoesNotContain("ATTEMPTS", shown);
                Assert.DoesNotContain("SHARE OF TIME", shown);
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromSeconds(10));
    }

    private static async Task<TimingPageModel> LoadedTiming((string Word, int? Ms)[] words,
        TimingAggregateRow[] kinds, TimingAggregateRow[] rules, WordRuleTiming[]? costliest = null,
        WordTimeAttribution? attribution = null, IReadOnlyList<long?>? elapsedNs = null, string? requestedRule = null)
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        var timing = new TimingPageModel(context);
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            new TimingResponse("assessment-parse", request.WordSet, request.By, words.Length, 9, 700, [],
                request.By == "kind" ? kinds : rules, request.Rule is null ? [] : costliest ?? [])
            {
                Words = words.Select((word, index) => new TimingWordRow(word.Word, word.Ms, TimingCompletion.Finished)
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
            context.OpenTiming(words.Select(word => word.Word).ToArray(), new TraceTimingKey(rules.FirstOrDefault(row => row.Key == requestedRule)?.Kind ?? "morph_rule", requestedRule));
            await timing.LoadFocusedTimingCommand.ExecutionTask!;
        }
        return timing;
    }

    private static WordTimeAttribution AttributionFor((string Word, int? Ms)[] words, TimingAggregateRow[] kinds)
    {
        var wordTime = words.Where(word => word.Ms is not null).Sum(word => word.Ms!.Value);
        var attributed = kinds.Sum(row => row.SelfMs);
        var notAttributed = wordTime - attributed;
        return new WordTimeAttribution(words.Count(word => word.Ms is not null), wordTime, attributed,
            kinds.Length == 0 ? null : Math.Max(0, notAttributed),
            kinds.Length > 0 && wordTime > 0 ? Math.Max(0, notAttributed) / wordTime : null,
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
