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
    public async Task KindSharesAreOfTheWordsWholeParseTimeWithOtherTimeApart()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming(NineWords, Kinds, Rules);

        Assert.Equal(
            [("Morphological rules", "60%", "480 ms"), ("Phonological rules", "20%", "160 ms"),
                ("Root lookup", "5%", "40 ms"), ("Other time", "15%", "120 ms")],
            timing.KindShares.Select(share => (share.Label, share.ShareText, share.TimeText)));
        Assert.True(timing.KindShares[^1].IsOtherTime);
        Assert.Equal(1d, timing.KindShares.Sum(share => share.Share!.Value), precision: 6);
        Assert.Equal("Every share is of the 0.8 s these 9 words took to parse.", timing.ShareDenominatorText);
        Assert.Equal("The parser recorded 85% of that time against rules and lookups. Other time is the rest of " +
            "its work, which it records against no rule.", timing.OtherTimeText);
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

        Assert.Equal("guid-1", timing.SelectedRule);
        await timing.ChooseRuleCommand.ExecuteAsync(rules[1]);

        Assert.Equal("guid-2", timing.SelectedRule);
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
            timing.CostliestRuleWordTimes.Select(word => word.TimeText));
    }

    [Fact]
    public async Task RecordedTimeOverTheWordsTimeIsSaidRatherThanHiddenAsNoOtherTime()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var timing = await LoadedTiming([("dogs", 10), ("cats", 2)],
            [new TimingAggregateRow("morph_rule", "morph_rule", 12.6, 1, 2)], []);

        Assert.DoesNotContain(timing.KindShares, share => share.IsOtherTime);
        Assert.Equal("105%", timing.KindShares.Single().ShareText);
        Assert.Equal("The parser recorded 0.6 ms more against rules and lookups than the words' whole parse " +
            "time, because each word's time is kept to the whole millisecond.", timing.OtherTimeText);
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
                Assert.Contains("Other time", shown);
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
        TimingAggregateRow[] kinds, TimingAggregateRow[] rules, WordRuleTiming[]? costliest = null)
    {
        var fake = new FakeCommandClient();
        var context = WorkspaceContextTests.NewContext(fake);
        var timing = new TimingPageModel(context);
        fake.OnTiming((request, _) => Task.FromResult(CommandOutcome<TimingResponse>.Success(
            new TimingResponse("assessment-parse", request.WordSet, request.By, words.Length, 9, 700, [],
                request.By == "kind" ? kinds : rules, request.Rule is null ? [] : costliest ?? [])
            {
                Words = words.Select(word => new TimingWordRow(word.Word, word.Ms, TimingCompletion.Finished))
                    .ToArray(),
            })));
        await context.OpenProjectAsync(ProjectPath);
        context.PublishEvidence(new WorkspaceEvidence(Assessment(), DateTimeOffset.Now, WasRerun: false));
        await context.EvidencePublication;
        await timing.SelectWordSetCommand.ExecuteAsync("slowest");
        return timing;
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
