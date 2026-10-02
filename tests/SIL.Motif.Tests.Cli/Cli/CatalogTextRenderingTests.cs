using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class CatalogTextRenderingTests
{
    [Fact]
    public void TimingTextCarriesTheKindAlongsideTheKey()
    {
        var response = new TimingResponse("assessment", "all", "rule", 1, 1, 1, [],
            [new TimingAggregateRow("rule-key", "Plural", 1, 1, 1) { Kind = "morph_rule" }], []);
        var text = CommandTextRenderer.Render(CommandOutcome<TimingResponse>.Success(response), asJson: false).Output;
        Assert.Contains("[morph_rule:rule-key]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewTextShowsSelectionSourcesCoverageAccuracyAndTiming()
    {
        var response = new OverviewResponse(
            "Aweti", DateTimeOffset.Parse("2026-09-24T12:00:00Z"), DateTimeOffset.Parse("2026-09-24T11:00:00Z"),
            135, 1, 4, 555, 300, 12, 100, "assessment/1", DateTimeOffset.Parse("2026-09-24T11:30:00Z"),
            120, "sha256:grammar", "sha256:selection",
            new OverviewTextCoverage(43, 20, 71, 1, 555, 300)
            {
                OccurrenceCoveragePercent = 12.4,
            },
            new OverviewAccuracy(33, 115, 18, 63, 2, 9, 4, 14) { RejectedWordsInMatchCell = 1 },
            new OverviewTiming(8, 400, [new SlowWordTiming("Akjulule", 1007)], 71),
            Warnings: null)
        {
            SelectionResolved = true,
            WordCoveragePercent = 71.6,
        };

        var rendered = CommandTextRenderer.Render(CommandOutcome<OverviewResponse>.Success(response), asJson: false);

        Assert.Contains("Selection default: 1 text + 4 words = 135 words, 555 occurrences", rendered.Output);
        Assert.Contains("43/135 parse (72%)", rendered.Output);
        Assert.Contains("300/555 occurrences covered (12%)", rendered.Output);
        Assert.DoesNotContain("43/135 parse (32%)", rendered.Output);
        Assert.Contains("33/115 approved kept  18 violations  63 unknown", rendered.Output);
        Assert.Contains("rejected analyses rebuilt 2  rejected words matched 1/9", rendered.Output);
        Assert.Contains("median 8 ms  p95 400 ms  slowest Akjulule 1,007 ms", rendered.Output);
    }

    [Fact]
    public void OverviewTextCountsTheWordsWarningsTouch()
    {
        var response = new OverviewResponse(
            "Aweti", DateTimeOffset.Parse("2026-09-24T12:00:00Z"), DateTimeOffset.Parse("2026-09-24T11:00:00Z"),
            135, 1, 4, 555, 300, 12, 100, "assessment/1", DateTimeOffset.Parse("2026-09-24T11:30:00Z"),
            120, "sha256:grammar", "sha256:selection",
            new OverviewTextCoverage(43, 20, 71, 1, 555, 300),
            new OverviewAccuracy(33, 115, 18, 63, 2, 9, 4, 14),
            new OverviewTiming(8, 400, [new SlowWordTiming("Akjulule", 1007)], 71),
            Warnings: new OverviewWarningsSummary(24, 24, "Environment couldn't be read", 12)
            {
                ErrorCount = 0,
                WarningCount = 24,
                InformationCount = 0,
                YourWords = new WarningWordsTouched(14, 7, []) { BySpellingOnly = 3 },
            });

        var rendered = CommandTextRenderer.Render(CommandOutcome<OverviewResponse>.Success(response), asJson: false);

        Assert.Contains("Warnings   24 findings (0 errors, 24 warnings, 0 information)", rendered.Output, StringComparison.Ordinal);
        Assert.Contains("           14 of your words use something a finding names (7 don't parse)", rendered.Output,
            StringComparison.Ordinal);
        Assert.Contains("           Not counted: 3 spelling candidates; not confirmed uses of the phoneme", rendered.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewHeaderShowsProjectFreshnessAndCallsIncompleteWordsLimits()
    {
        var baseline = new DateTimeOffset(2026, 9, 24, 11, 2, 0, TimeSpan.Zero);
        var saved = new DateTimeOffset(2026, 9, 24, 10, 58, 0, TimeSpan.Zero);
        var response = new OverviewResponse(
            "Aweti", baseline, saved, 135, 1, 4, 555, 300, 12, 100, "assessment/1", baseline,
            120, "sha256:grammar", "sha256:selection",
            new OverviewTextCoverage(43, 20, 71, 1, 555, 300),
            new OverviewAccuracy(33, 115, 18, 63, 2, 9, 4, 14),
            new OverviewTiming(8, 400, [new SlowWordTiming("Akjulule", 1007)], 71),
            Warnings: null)
        {
            SelectionResolved = true,
        };
        var payload = JsonSerializer.SerializeToNode(response)!.AsObject();
        payload["ProjectFileName"] = "Aweti.fwdata";
        payload["BaselineCapturedUtc"] = baseline;
        payload["BaselineSourceLastWriteUtc"] = saved;
        payload["IsStale"] = false;
        var populated = payload.Deserialize<OverviewResponse>()!;

        var rendered = CommandTextRenderer.Render(
            CommandOutcome<OverviewResponse>.Success(populated), asJson: false);

        Assert.Contains($"Aweti  Aweti.fwdata  Current (Baseline {baseline.ToLocalTime():HH:mm}, " +
            $"FieldWorks saved {saved.ToLocalTime():HH:mm})", rendered.Output);
        Assert.Contains("71 limit", rendered.Output);
        Assert.DoesNotContain("opened", rendered.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Warnings   not stored", rendered.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewTextShowsUnresolvedSelectionAndDoesNotInventNullPercentages()
    {
        var response = new OverviewResponse(
            "Aweti", DateTimeOffset.Parse("2026-09-24T12:00:00Z"), DateTimeOffset.Parse("2026-09-24T11:00:00Z"),
            0, 0, 0, 0, 300, 12, 100, null, null,
            null, "sha256:grammar", null,
            new OverviewTextCoverage(0, 0, 0, 0, 0, 0),
            new OverviewAccuracy(0, 0, 0, 0, 0, 0, 0, 0),
            new OverviewTiming(null, null, [], 0),
            Warnings: null)
        {
            SelectionResolved = false,
            WordCoveragePercent = null,
        };

        var rendered = CommandTextRenderer.Render(CommandOutcome<OverviewResponse>.Success(response), asJson: false);

        Assert.Contains("Selection default: not resolved against this Baseline", rendered.Output);
        Assert.DoesNotContain("0 text + 0 words", rendered.Output);
        Assert.Contains("0/0 parse (not recorded)", rendered.Output);
        Assert.Contains("0/0 occurrences covered (not recorded)", rendered.Output);
        Assert.DoesNotContain("0%", rendered.Output);
    }

    [Theory]
    [InlineData(TimingEvidenceRelation.Historical, false, "Historical Baseline")]
    [InlineData(TimingEvidenceRelation.Historical, true, "Historical Baseline")]
    [InlineData(TimingEvidenceRelation.Current, false, "Current Baseline")]
    [InlineData(TimingEvidenceRelation.SavedSince, true, "FieldWorks saved since the measured Baseline")]
    [InlineData(TimingEvidenceRelation.Unknown, false, "Baseline relationship unknown")]
    public void TimingTextKeepsSelectedProvenanceSeparateFromCurrentFreshness(
        TimingEvidenceRelation relation, bool currentStale, string expected)
    {
        var response = new TimingResponse("assessment/1", "all", "rule", 1, 8, 400, [], [], [])
        {
            EvidenceRelation = relation,
            CurrentProjectIsStale = currentStale,
            IsStale = relation is TimingEvidenceRelation.Historical or TimingEvidenceRelation.SavedSince,
            SourceLastWriteUtc = DateTimeOffset.Parse("2026-09-24T12:00:00Z"),
        };
        var output = CommandTextRenderer.Render(CommandOutcome<TimingResponse>.Success(response), asJson: false).Output;
        Assert.Contains(expected, output, StringComparison.Ordinal);
        Assert.Contains("2026-09-24T12:00:00", output, StringComparison.Ordinal);
        Assert.Equal(currentStale, output.Contains("FieldWorks has changed since the current Baseline", StringComparison.Ordinal));
        if (relation == TimingEvidenceRelation.Historical)
            Assert.DoesNotContain("FieldWorks saved since the measured Baseline", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TimingTextWarnsWhenTheProjectIsStale()
    {
        var response = new TimingResponse("assessment/1", "all", "rule", 1, 8, 400, [], [], [])
        {
            IsStale = true,
            EvidenceRelation = TimingEvidenceRelation.SavedSince,
            CurrentProjectIsStale = true,
        };

        var rendered = CommandTextRenderer.Render(CommandOutcome<TimingResponse>.Success(response), asJson: false);

        Assert.Contains("FieldWorks has changed since the current Baseline", rendered.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewTextSplitsTotalWordTimeByKindWithNotAttributedBeside()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var response = new OverviewResponse(
            "Aweti", DateTimeOffset.Parse("2026-09-24T12:00:00Z"), null, 2, 1, 0, 2, 2, 0, 0, "assessment/1",
            DateTimeOffset.Parse("2026-09-24T11:30:00Z"), 0.8, null, null,
            new OverviewTextCoverage(2, 0, 0, 0, 2, 2), new OverviewAccuracy(0, 0, 0, 0, 0, 0, 0, 0),
            new OverviewTiming(400, 500, [], 0)
            {
                MeasuredWordCount = 2,
                Kinds = [new TimingAggregateRow("morph_rule", "morph_rule", 600, 0.75, 2) { Kind = "morph_rule" }],
                Attribution = new WordTimeAttribution(2, 800, 600, 200, 0.25, 0, false),
            },
            null);

        var output = CommandTextRenderer.Render(CommandOutcome<OverviewResponse>.Success(response), asJson: false).Output;

        Assert.Contains("2 words, 0.8 s total word time: morph_rule 75.0%, not attributed 25.0%", output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TimingTextSharesEveryRowOfTotalWordTimeAndCountsCallsPerKind()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var response = new TimingResponse("assessment/1", "all", "rule", 2, 8, 400, [],
            [
                new TimingAggregateRow("guid-1", "Plural", 300, 0.375, 2) { Kind = "morph_rule", Calls = 40 },
                new TimingAggregateRow("guid-2", "Plural", 200, 0.25, 1) { Kind = "lex_entry" },
            ], [])
        {
            Attribution = new WordTimeAttribution(2, 800, 500, 302, 0.3775, 2, true),
        };

        var output = CommandTextRenderer.Render(CommandOutcome<TimingResponse>.Success(response), asJson: false).Output;

        Assert.Contains("Total word time: 800.00 ms for 2 measured word(s)", output, StringComparison.Ordinal);
        Assert.Contains("Plural [morph_rule:guid-1]: 300.00 ms (37.5%), 40 morph_rule calls, 2 words", output, StringComparison.Ordinal);
        Assert.Contains("Plural [lex_entry:guid-2]: 200.00 ms (25.0%), calls not counted, 1 words", output, StringComparison.Ordinal);
        Assert.Contains("Not attributed: 302.00 ms (37.8%)", output, StringComparison.Ordinal);
        Assert.Contains("recorded 2.00 ms more than their words' own time", output, StringComparison.Ordinal);
        Assert.DoesNotContain("attempts", output, StringComparison.Ordinal);
    }

    [Fact]
    public void GrammarCheckTextAndJsonIdentifyErrorsSeparatelyFromWarnings()
    {
        var response = new GrammarCheckResponse(
            [new GrammarWarning(GrammarDiagnosticLevel.Error, "Feature system", [],
                [new GrammarWarningPart("A feature system could not be loaded.", GrammarWarningPartRole.Text)],
                "error: hc-invalid-feature-system: A feature system could not be loaded.")],
            HasBaseline: true);

        var text = CommandTextRenderer.Render(CommandOutcome<GrammarCheckResponse>.Success(response), asJson: false);
        var json = CommandTextRenderer.Render(CommandOutcome<GrammarCheckResponse>.Success(response), asJson: true);

        Assert.Contains("(1 error, 0 warnings, 0 information)", text.Output);
        Assert.Contains("error: hc-invalid-feature-system", text.Output);
        Assert.Contains("\"severity\": \"error\"", json.Output);
    }

    [Fact]
    public void WarningsTextAndJsonIdentifyErrorCountsSeparately()
    {
        var finding = new GrammarWarning(GrammarDiagnosticLevel.Error, "Feature system", [], [],
            "error: hc-invalid-feature-system: A feature system could not be loaded.");
        var response = new WarningsResponse(true, true, [finding], [], 0, 0) { ErrorCount = 1 };

        var text = CommandTextRenderer.Render(CommandOutcome<WarningsResponse>.Success(response), asJson: false);
        var json = CommandTextRenderer.Render(CommandOutcome<WarningsResponse>.Success(response), asJson: true);

        Assert.Contains("Grammar findings: 1 (1 error, 0 warnings, 0 information)", text.Output);
        Assert.Contains("\"errorCount\": 1", json.Output);
        Assert.Contains("\"severity\": \"error\"", json.Output);
    }

    [Fact]
    public void WarningsTextSaysWhichOfYourWordsEachFindingTouchesAndHowTheyWereFound()
    {
        static ObjectUseWord Word(string word, string meaning) =>
            new(new WordRow(word, WordRowOutcome.NoParse, meaning, WordRowTone.Problem));
        GrammarWarning Finding(string code, WarningWords words) =>
            new(GrammarDiagnosticLevel.Warning, code, [], [], $"warning: {code}: described") { Code = code, YourWords = words };
        var lost = new ObjectUseMeaning("Lost", WordRowTone.Problem, 2);
        var findings = new[]
        {
            Finding("hc-unsegmentable", new WarningWords(WarningWordsMatch.Identity,
                [Word("walikata", "Lost"), Word("anakata", "Lost")], [lost]) { Paths = [WarningWordsPath.Uses] }),
            Finding("hc-undeclared-segment", new WarningWords(WarningWordsMatch.Spelling,
                [Word("ngozi", "Lost")], [lost with { Words = 1 }]) { Paths = [WarningWordsPath.Spelling] }),
            Finding("hc-bad-environment", new WarningWords(WarningWordsMatch.Identity, [], [])
                { Paths = [WarningWordsPath.ThroughAllomorphs] }),
            Finding("fwdata.no-usable-allomorphs", new WarningWords(WarningWordsMatch.UnresolvedIdentity, [], [])
                { Reason = WarningAttributionReason.NoSubject }),
        };
        var response = new WarningsResponse(true, true, findings,
            [new GrammarWarningSummary("hc-unsegmentable", "Allomorph can't be split", GrammarDiagnosticLevel.Warning, 1)
                { YourWords = 2, BySpellingOnly = 1 }], 4, 0)
        {
            YourWords = new WarningWordsTouched(2, 2, [lost]) { BySpellingOnly = 1 },
        };

        var text = CommandTextRenderer.Render(CommandOutcome<WarningsResponse>.Success(response), asJson: false).Output;
        var json = CommandTextRenderer.Render(CommandOutcome<WarningsResponse>.Success(response), asJson: true).Output;

        Assert.Contains("2 of your words use something a finding names (2 don't parse)", text,
            StringComparison.Ordinal);
        Assert.Contains("Not counted: 1 spelling candidates; not confirmed uses of the phoneme", text, StringComparison.Ordinal);
        Assert.Contains("  hc-unsegmentable: 1 warning, 2 of your words", text, StringComparison.Ordinal);
        Assert.Contains("    Your words: walikata (Lost), anakata (Lost)", text, StringComparison.Ordinal);
        Assert.Contains("    Spelling candidates; not confirmed uses of the phoneme: ngozi (Lost)", text, StringComparison.Ordinal);
        Assert.Contains("    Your words: none in the Selection", text, StringComparison.Ordinal);
        Assert.Contains("    Your words: unresolved identity; PanGloss names no subject", text, StringComparison.Ordinal);
        Assert.Contains("\"match\": \"spelling\"", json, StringComparison.Ordinal);
        Assert.Contains("\"reason\": \"no_subject\"", json, StringComparison.Ordinal);
        Assert.Contains("\"state\": \"unresolved_identity\"", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(WarningWordsMatch.ProjectWide, WarningAttributionReason.NoWordAttribution,
        "Your words: project-wide; no word attribution")]
    [InlineData(WarningWordsMatch.MissingObject, WarningAttributionReason.StaleGuid,
        "Your words: missing object; its GUID is absent from the checked Baseline")]
    [InlineData(WarningWordsMatch.MissingObject, WarningAttributionReason.WrongClass,
        "Your words: missing object; its GUID belongs to a different FieldWorks class")]
    [InlineData(WarningWordsMatch.UnresolvedIdentity, WarningAttributionReason.NamedWithoutProjectGuid,
        "Your words: unresolved identity; the named subject has no project GUID")]
    [InlineData(WarningWordsMatch.UnresolvedIdentity, WarningAttributionReason.UnsupportedKind,
        "Your words: unresolved identity; the named class has no supported route to words")]
    public void WarningsTextExplainsEachUnattributedState(WarningWordsMatch match,
        WarningAttributionReason reason, string expected)
    {
        var finding = new GrammarWarning(GrammarDiagnosticLevel.Warning, "finding", [], [], "finding")
            { YourWords = new WarningWords(match, [], []) { Reason = reason } };
        var response = new WarningsResponse(true, true, [finding], [], 1, 0);
        var text = CommandTextRenderer.Render(CommandOutcome<WarningsResponse>.Success(response), asJson: false).Output;
        Assert.Contains(expected, text, StringComparison.Ordinal);
    }

    [Fact]
    public void WarningsTextShowsWeakerCandidatesBesideExactUsesAndUnavailableEvidence()
    {
        static ObjectUseWord Word(string word) =>
            new(new WordRow(word, WordRowOutcome.NoParse, "Lost", WordRowTone.Problem));
        var finding = new GrammarWarning(GrammarDiagnosticLevel.Warning, "mixed", [], [], "mixed")
        {
            YourWords = new WarningWords(WarningWordsMatch.Identity, [Word("exact")], [])
            {
                MembershipCandidates = [Word("member")], SpellingCandidates = [Word("spelled")],
            },
        };
        var unavailable = new GrammarWarning(GrammarDiagnosticLevel.Warning, "unavailable",
            [new GrammarWarningPart("form", GrammarWarningPartRole.Object, "id", "MoForm")
                { Reach = new WarningReach(WarningWordsPath.Uses) }], [], "unavailable");
        var response = new WarningsResponse(true, true, [finding, unavailable], [], 2, 0)
            { YourWords = new WarningWordsTouched(1, 1, []) { ByMembershipOnly = 1, BySpellingOnly = 1 } };
        var text = CommandTextRenderer.Render(CommandOutcome<WarningsResponse>.Success(response), asJson: false).Output;
        Assert.Contains("Not counted: 1 membership candidates; 1 spelling candidates; not confirmed uses of the phoneme", text);
        Assert.Contains("Your words: exact (Lost)", text);
        Assert.Contains("Membership candidates; not confirmed uses of the named object: member (Lost)", text);
        Assert.Contains("Spelling candidates; not confirmed uses of the phoneme: spelled (Lost)", text);
        Assert.Contains("Your words: evidence unavailable; no usable stored Parse all words", text);
    }
}
