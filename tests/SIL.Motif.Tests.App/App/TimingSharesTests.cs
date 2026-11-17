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
/// Pins that every share on the Timing page is of the chosen words' whole parser steps, that the work no rule
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

    // The words' parser steps are ten for each recorded millisecond, so a fixture reads the same in either unit.
    private static long? WorkOf(int? milliseconds) => milliseconds * 10L;

    private static readonly TimingWorkAggregate[] KindWork =
    [
        new("morph_rule", "morph_rule", "morph_rule", 4_800, false),
        new("phon_rule", "phon_rule", "phon_rule", 1_600, false),
        new("root_index", "root_index", "root_index", 400, false),
        new("unattributed", "Not attributed", "unattributed", 1_200, true),
    ];

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
    public void WordCardTimingSharesUseWholePercentagesOfParserWork()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("36% · 2,880 parser steps", new TimingShareViewModel("Subject agreement", 0.36, 2880).ShareLabel);
        Assert.Equal("15% · 1 parser step", new TimingShareViewModel("Vowel harmony", 0.15, 1).ShareLabel);
        Assert.Equal("<1% · 3 parser steps", new TimingShareViewModel("Rare rule", 0.0003, 3).ShareLabel);
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

        Assert.Equal([("Subject agreement", "60% · 600 parser steps"), ("Vowel harmony", "30% · 300 parser steps")],
            card.TimingShares.Select(share => (share.Name, share.ShareLabel)));
        Assert.Equal(0.1, card.NotAttributedShare!.Value, 6);
        Assert.Equal("Not attributed · 10%", card.NotAttributedShareLabel);
        Assert.Equal("Every share is of the 1,000 parser steps this word needed to parse.", card.TimingShareBasisLabel);
    }

    [Fact]
    public void WordCardSaysParserWorkAreNotRecordedInsteadOfShowingTimeShares()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var card = Card(WordTiming(1, WordWorkSummary.None, []));

        Assert.True(card.HasTimingEvidence);
        Assert.False(card.HasTimingShares);
        Assert.Null(card.NotAttributedShare);
        Assert.Equal("Parser steps not recorded for this word", card.NotAttributedShareLabel);
        Assert.Equal(string.Empty, card.TimingShareBasisLabel);
    }

    [Fact]
    public void WordCardSaysWhenOnlyTheStepsByRuleWereNotKept()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var card = Card(WordTiming(1, new WordWorkSummary(1, 500, 500, 500), []));

        Assert.False(card.HasTimingShares);
        Assert.Equal("Parser steps by rule not recorded for this word", card.NotAttributedShareLabel);
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

        Assert.Equal("75% · 1,500 parser steps", Assert.Single(card.TimingShares).ShareLabel);
        Assert.Equal("Not attributed · 25%", card.NotAttributedShareLabel);
        Assert.Equal("Every share is of the 2,000 parser steps 2 of these 3 words needed to parse; 1 has no parser steps recorded.",
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
        var mark = new TimingRuleRow(new TimingShare("Rule", "lex_entry", 10, 0.5, 1, matched), false,
            finding.Code);
        Assert.True(mark.HasGrammarWarning);
        Assert.Equal("A grammar warning names this rule · Open the warning", TimingRuleRow.GrammarWarningTooltip);
    }

    private static string[] VisibleText(Window window) => window.GetLogicalDescendants().OfType<TextBlock>()
        .Where(text => text.IsEffectivelyVisible).Select(text => text.Text ?? string.Empty).ToArray();

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
