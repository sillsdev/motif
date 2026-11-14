using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class CatalogTextRenderingTests
{
    [Fact]
    public void ParsimonyJsonCarriesEachFindingOnceAsAFullObject()
    {
        var token = new BaselineToken("project", "sha256:" + new string('a', 64), "1",
            "2026-10-05T00:00:00Z", "sha256:" + new string('b', 64));
        var inputs = new ParsimonyReportInputs("bundle-1", token, "baseline", null, "fingerprint-1",
            new ParsimonyArtifactDigest(4, new string('c', 64)),
            new ParsimonyArtifactDigest(1, new string('d', 64)), null, null, []);
        using var arguments = JsonDocument.Parse("{}");
        var finding = new ParsimonyFinding("finding/one", "P-adhoc-duplicate", ParsimonyAxis.Parsimony,
            ParsimonyTier.Static,
            new ParsimonyFindingAttachment(ParsimonyAttachmentKind.AuthoredObject,
                "18f47b40-0c5f-4d08-a9b4-9988f2e47702", ParsimonyAuthoredObjectKind.AdhocProhibition),
            "group/one", new ParsimonyMeasureNumber(2, 3, "prohibitions"),
            new ParsimonyMeasureThreshold(ParsimonyThresholdOperator.GreaterThan, 1, "1"), new string('a', 64),
            [new ParsimonyEvidenceReference("bundle/one", "adhoc-context", arguments.RootElement.Clone())],
            "guide:parsimony/recipes/p-adhoc-duplicate", [], ParsimonyVerification.NotRun);
        var row = new ParsimonyFindingDispositionViewRow("report/one", "bundle-1", "active", finding,
            null, null, null, null, null, null);
        var projection = new ParsimonyDispositionProjection("bundle-1", "report/one", null, "project", "digest",
            1, 0, 0, 0, [row], [], []);
        var response = new ParsimonyReportResponse("report/one", inputs, [finding], "text")
        {
            Notes = [],
            DispositionProjection = projection,
        };

        foreach (object payload in new object[] { response, new ParsimonyReportSetResponse("job/one", "bundle-1", [response]) })
        {
            var json = payload is ParsimonyReportResponse single
                ? CommandTextRenderer.Render(CommandOutcome<ParsimonyReportResponse>.Success(single), asJson: true)
                : CommandTextRenderer.Render(CommandOutcome<ParsimonyReportSetResponse>.Success(
                    (ParsimonyReportSetResponse)payload), asJson: true);
            var root = JsonNode.Parse(json.Output)!;
            var report = root["reports"]?[0] ?? root;
            var rowNode = report["dispositionProjection"]!["findings"]![0]!;
            Assert.Equal("finding/one", (string?)rowNode["findingId"]);
            Assert.Null(rowNode["finding"]);
            var full = report["findings"]!.AsArray().Where(f => (string?)f!["findingId"] == "finding/one").ToList();
            Assert.Single(full);
            Assert.Equal(1, CountOccurrences(json.Output, "\"evidenceDigest\""));
        }
    }

    private static int CountOccurrences(string text, string needle) =>
        (text.Length - text.Replace(needle, string.Empty, StringComparison.Ordinal).Length) / needle.Length;

    [Fact]
    public void AParsimonyNoteShowsAsAnInformationLineNotAFinding()
    {
        var token = new BaselineToken("project", "sha256:" + new string('a', 64), "1",
            "2026-10-05T00:00:00Z", "sha256:" + new string('b', 64));
        var inputs = new ParsimonyReportInputs("bundle-1", token, "baseline", null, "fingerprint-1",
            new ParsimonyArtifactDigest(4, new string('c', 64)),
            new ParsimonyArtifactDigest(1, new string('d', 64)), null, null, []);
        var note = new ParsimonyNote("not-checked", "R-slot-blocking",
            "Not checked: A required slot blocks a form — the Default Selection has no Approved words.", null);
        var response = new ParsimonyReportResponse("report/note", inputs, [], "Parsimony advisory report\n" + note.Text)
        {
            Notes = [note],
        };

        var rendered = CommandTextRenderer.Render(CommandOutcome<ParsimonyReportResponse>.Success(response),
            asJson: false);

        Assert.Contains(note.Text, rendered.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Finding", rendered.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Recommendation", rendered.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0/", rendered.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0, "No known word matches")]
    [InlineData(2, "At least 2 of your words")]
    public void WarningTextQualifiesIncompleteAggregateAndKindCounts(int known, string qualifier)
    {
        var response = new WarningsResponse(true, true, [],
            [new GrammarWarningSummary("mixed", "Mixed", GrammarDiagnosticLevel.Warning, 1)
                { YourWords = known, WordAttributionComplete = false }], 1, 0)
        {
            YourWords = new WarningWordsTouched(known, 0, [])
                { IsComplete = false, AttributionLimits = [WarningAttributionReason.UnsupportedKind] },
        };
        var text = CommandTextRenderer.Render(CommandOutcome<WarningsResponse>.Success(response), false).Output;
        Assert.Contains(qualifier, text);
        Assert.Contains("some named connections could not be followed", text);
        Assert.Contains(known == 0 ? "no known matches; incomplete" : "at least 2 of your words", text);
    }

    [Fact]
    public void AssessmentTextAndJsonCarryTheSharedComparisonHeadlineAndQualification()
    {
        var comparison = new WordComparison(ProjectStanding.Candidate, WordRowOutcome.Different,
            "disapproved-rebuilt", "Rebuilt an analysis you Disapproved", WordRowTone.Problem)
        {
            Detail = "Your undecided analysis wasn't built",
        };
        var word = new AssessmentWordResult("mixed", "analysed", false, "Search completed", 1, null)
            { Comparison = comparison };
        var response = new AssessCommandResponse(new BaselineCaptureResponse(
            new("project", "sha256:" + new string('a', 64), "version", "2026-10-01T00:00:00.0000000Z",
                "sha256:" + new string('b', 64)), "project.fwdata", DateTimeOffset.UtcNow, false, true),
            new SelectionProjection(["mixed"], []), ["assessment"], "Search completed") { Words = [word] };

        var outcome = CommandOutcome<AssessCommandResponse>.Success(response);
        var text = CommandTextRenderer.Render(outcome, asJson: false).Output;
        var json = CommandTextRenderer.Render(outcome, asJson: true).Output;

        Assert.Contains("mixed: " + comparison.Headline, text, StringComparison.Ordinal);
        Assert.Contains(comparison.Detail, text, StringComparison.Ordinal);
        Assert.Contains("disapproved-rebuilt", json, StringComparison.Ordinal);
        Assert.DoesNotContain("PanGloss confirms", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AssessTextAndJsonShowCountsWithGuessOnlyApartFromAccepted()
    {
        static AssessmentWordResult Row(string word, string outcome, string? guess) =>
            new(word, outcome, false, "Search completed", 1, null)
            {
                Morphology = new ParseWordEvidence("s", 0, word, 1, false, false, false,
                    outcome == "analysed" ? [new ParseAnalysis([new ParseMorph("a", null, null, guess)])] : [], []),
            };
        var response = new AssessCommandResponse(new BaselineCaptureResponse(
            new("project", "sha256:" + new string('a', 64), "version", "2026-10-01T00:00:00.0000000Z",
                "sha256:" + new string('b', 64)), "project.fwdata", DateTimeOffset.UtcNow, false, true),
            new SelectionProjection(["a", "b", "c"], []), ["assessment"], "Search completed")
        { Words = [Row("a", "analysed", null), Row("b", "analysed", "?baga"), Row("c", "no-analysis", null)] };
        var outcome = CommandOutcome<AssessCommandResponse>.Success(response);

        var text = CommandTextRenderer.Render(outcome, asJson: false).Output;
        var json = CommandTextRenderer.Render(outcome, asJson: true).Output;

        Assert.Contains("3 word(s); 2 analysed (1 accepted, 1 guess-only); 1 no-analysis", text, StringComparison.Ordinal);
        Assert.Contains("0 stopped at a parser limit; 0 stopped at the time safety net; 0 skipped", text, StringComparison.Ordinal);
        Assert.Contains("\"guessOnly\": 1", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WarningTextRetainsProducerAdvicePlacesAndMissingReferenceState()
    {
        var finding = new GrammarWarning(GrammarDiagnosticLevel.Warning, "Producer title",
            [new("Missing slot", GrammarWarningPartRole.Missing, "missing", "MoInflAffixSlot")
            {
                Status = GrammarSubjectStatus.UnresolvedReference, Field = "SuffixSlots",
                LinkStatus = FieldWorksLinkStatus.Unavailable, LinkReason = FieldWorksLinkReason.UnresolvedReference,
            }], [], "producer description")
        {
            Explanation = "Producer explanation", Guidance = "Producer guidance",
            FieldWorksPlaces = [new("posEdit", "Affix Templates")],
        };
        var text = CommandTextRenderer.Render(CommandOutcome<WarningsResponse>.Success(
            new(true, true, [finding], [], 1, 0)), asJson: false).Output;
        Assert.Contains("Producer explanation", text);
        Assert.Contains("Producer guidance", text);
        Assert.Contains("Affix Templates", text);
        Assert.Contains("unresolved reference", text);
        Assert.Contains("SuffixSlots", text);
    }

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
        Assert.Contains("33/115 approved kept  18 violations  63 unknown", rendered.Output);
        Assert.Contains("rejected analyses rebuilt 2  rejected words matched 1/9", rendered.Output);
        Assert.Contains("comparison unavailable", rendered.Output);
        Assert.DoesNotContain(" ms", rendered.Output);

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

    [Theory]
    [InlineData(20_000_000L, "Work limit:      20,000,000 parser work")]
    [InlineData(null, "Work limit:      none")]
    public void SelectionShowNamesTheWorkLimitAndTheTimeSafetyNetApart(long? steps, string expected)
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var limits = new SelectionParsingLimits(new ParserWorkLimit(steps), SelectionTimeLimitMode.Estimated, null);
        var selection = new NamedSelectionProjection("Default", [], [], "2026-10-31T00:00:00Z", "2026-10-31T00:00:00Z",
            limits, "1");

        var text = CommandTextRenderer.Render(
            CommandOutcome<DefaultSelectionResponse>.Success(new DefaultSelectionResponse(selection)), asJson: false).Output;

        Assert.Contains(expected, text, StringComparison.Ordinal);
        Assert.Contains("Time safety net: ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Parser work limit", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OverviewTextDoesNotExposeUnpairedWorkOrTime()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var response = new OverviewResponse(
            "Aweti", DateTimeOffset.Parse("2026-09-24T12:00:00Z"), null, 2, 1, 0, 2, 2, 0, 0, "assessment/1",
            DateTimeOffset.Parse("2026-09-24T11:30:00Z"), 0.8, null, null,
            new OverviewTextCoverage(2, 0, 0, 0, 2, 2), new OverviewAccuracy(0, 0, 0, 0, 0, 0, 0, 0),
            new OverviewTiming(400, 500, [new SlowWordTiming("Akjulule", 1007) { ParserWork = 7000 }], 0)
            {
                MeasuredWordCount = 2,
                Work = new WordWorkSummary(2, 8000, 4000, 7000),
                ParserWorkAggregates =
                [
                    new TimingWorkAggregate("morph_rule", "morph_rule", "morph_rule", 6000, false),
                    new TimingWorkAggregate("unattributed", "unattributed", "unattributed", 2000, true),
                ],
                Attribution = new WordTimeAttribution(2, 800, 600, 200, 0.25, 0, false),
            },
            null);

        var output = CommandTextRenderer.Render(CommandOutcome<OverviewResponse>.Success(response), asJson: false).Output;

        Assert.Contains("Work       comparison unavailable", output, StringComparison.Ordinal);
        Assert.DoesNotContain("8,000", output, StringComparison.Ordinal);
        Assert.DoesNotContain(" ms", output, StringComparison.Ordinal);

    }

    [Fact]
    public void OverviewTextSaysWhenWorkWasNotRecordedInsteadOfShowingATimeInItsPlace()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var response = new OverviewResponse(
            "Aweti", DateTimeOffset.Parse("2026-09-24T12:00:00Z"), null, 2, 1, 0, 2, 2, 0, 0, "assessment/1",
            DateTimeOffset.Parse("2026-09-24T11:30:00Z"), 0.8, null, null,
            new OverviewTextCoverage(2, 0, 0, 0, 2, 2), new OverviewAccuracy(0, 0, 0, 0, 0, 0, 0, 0),
            new OverviewTiming(400, 500, [new SlowWordTiming("Akjulule", 1007)], 0) { MeasuredWordCount = 1 },
            null);

        var output = CommandTextRenderer.Render(CommandOutcome<OverviewResponse>.Success(response), asJson: false).Output;

        Assert.Contains("Work       comparison unavailable", output, StringComparison.Ordinal);
        Assert.DoesNotContain("1,007", output, StringComparison.Ordinal);

    }

    [Fact]
    public void TimingTextListsObjectsWithoutUnpairedWorkOrTime()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var response = new TimingResponse("assessment/1", "all", "rule", 2, 8, 400, [],
            [
                new TimingAggregateRow("guid-1", "Plural", 300, 0.375, 2) { Kind = "morph_rule", Calls = 40 },
                new TimingAggregateRow("guid-2", "Plural", 200, 0.25, 1) { Kind = "lex_entry" },
            ], [])
        {
            Attribution = new WordTimeAttribution(2, 800, 500, 302, 0.3775, 2, true),
            Work = new WordWorkSummary(2, 1000, 500, 600),
            ParserWorkAggregates =
            [
                new TimingWorkAggregate("guid-1", "Plural", "morph_rule", 600, false),
                new TimingWorkAggregate("guid-2", "Plural", "lex_entry", 300, false),
                new TimingWorkAggregate("unattributed", "unattributed", "unattributed", 100, true),
            ],
        };

        var output = CommandTextRenderer.Render(CommandOutcome<TimingResponse>.Success(response), asJson: false).Output;

        Assert.Contains("Parser work comparison for 2 word(s)", output, StringComparison.Ordinal);
        Assert.Contains("Plural [morph_rule:guid-1]: comparison unavailable", output, StringComparison.Ordinal);
        Assert.Contains("Plural [lex_entry:guid-2]: comparison unavailable", output, StringComparison.Ordinal);
        Assert.DoesNotContain("1,000", output, StringComparison.Ordinal);
        Assert.DoesNotContain(" ms", output, StringComparison.Ordinal);
        Assert.DoesNotContain("60.0%", output, StringComparison.Ordinal);

    }

    [Fact]
    public void TrialTextShowsPercentAndStopsWithoutRawWorkOrTime()
    {
        var work = new TrialWork(3, 1000, 1180, 18,
            [new TrialWorkIncrease("mwalimu", 100, 150, 50), new TrialWorkIncrease("kitabu", 200, 220, 10)], 1, 2)
        {
            WorkLimitWords = ["hawajafika"],
            TimeSafetyNetWords = ["a", "b"],
            BaselineTimeMs = 4200,
            TrialTimeMs = 4500,
        };
        var trial = new TrialRecord("job/1", "p/1", "sha256:x", new BaselineToken("project", "sha256:" + new string('a', 64), "1",
                "2026-10-05T00:00:00Z", "sha256:" + new string('b', 64)),
            null, new Dictionary<string, string>(), [], false, null, JobStatus.Completed, null, TrialFreshness.Current)
        { Work = work };

        var output = CommandTextRenderer.Render(CommandOutcome<TrialRecord>.Success(trial), asJson: false).Output;

        Assert.Contains("These words took 18% more work to parse.", output, StringComparison.Ordinal);
        Assert.DoesNotContain("Warning: parser work increased", output, StringComparison.Ordinal);
        Assert.Contains("mwalimu: 50% more work", output, StringComparison.Ordinal);
        Assert.Contains("kitabu: 10% more work", output, StringComparison.Ordinal);
        Assert.Contains("Stopped: 1 at a parser limit; 2 at the time safety net.", output, StringComparison.Ordinal);
        Assert.Contains("Parser limit: hawajafika", output, StringComparison.Ordinal);
        Assert.Contains("Time safety net: a, b", output, StringComparison.Ordinal);
        Assert.DoesNotContain("4,200 ms", output, StringComparison.Ordinal);

    }

    [Fact]
    public void TrialTextPrintsTheReadingIdentityProofOfAFeatureRemoval()
    {
        var identity = new ReadingIdentityResult(ReadingIdentityStatus.Differs, 3, 2, 5, 0, 1,
            [new ReadingIdentityDifference("kaun", [], ["kaun/m/i/g"])], ["tulog"], []);
        var trial = new TrialRecord("job/1", "p/1", "sha256:x", new BaselineToken("project", "sha256:" + new string('a', 64), "1",
                "2026-10-05T00:00:00Z", "sha256:" + new string('b', 64)),
            null, new Dictionary<string, string>(), [], false, null, JobStatus.Completed, null, TrialFreshness.Current)
        { ReadingIdentity = identity };

        var output = CommandTextRenderer.Render(CommandOutcome<TrialRecord>.Success(trial), asJson: false).Output;

        Assert.Contains("readings of affected words: Differs; 2 of 3 words identical; 0 lost, 1 new of 5 before", output, StringComparison.Ordinal);
        Assert.Contains("kaun: lost [] new [kaun/m/i/g]", output, StringComparison.Ordinal);
        Assert.Contains("incomplete: tulog", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TrialTextNamesDroppedGrammarTheProposalIntroducedAndIsNotClean()
    {
        var finding = new DroppedGrammarFinding("grammar.environment.invalid",
            [new DroppedGrammarObject("env-id", "PhEnvironment", "after a vowel", null)], "dropped");

        var output = RenderTrial(new DroppedGrammar([finding], 2) { OmittedFindings = 3 });

        Assert.Contains("NOT clean", output, StringComparison.Ordinal);
        Assert.Contains("grammar.environment.invalid on PhEnvironment after a vowel (env-id)", output, StringComparison.Ordinal);
        Assert.Contains("and 3 more.", output, StringComparison.Ordinal);
        Assert.Contains("2 dropped finding(s) were already in the Baseline's grammar.", output, StringComparison.Ordinal);
    }

    [Fact]
    public void TrialTextSaysCleanOnlyWhenTheCheckRanAndIntroducedNothing()
    {
        Assert.Contains("Clean: the parser dropped no grammar", RenderTrial(new DroppedGrammar([], 0)), StringComparison.Ordinal);
        var unchecked_ = RenderTrial(DroppedGrammar.NotChecked("no parser"));
        Assert.Contains("was not checked, so this Trial is not clean: no parser", unchecked_, StringComparison.Ordinal);
        Assert.DoesNotContain("Clean: the parser dropped no grammar", unchecked_, StringComparison.Ordinal);
    }

    [Fact]
    public void ATrialReadWithWordResultsListsEachWordAndItsReadings()
    {
        var reading = new ParserReading([new ParserReadingMorph("a", "3SG", "Pro", null, false, null),
            new ParserReadingMorph("penda", "love", "V", null, false, null)]);
        var trial = new TrialRecord("job/1", "p/1", "sha256:x",
            new BaselineToken("project", "sha256:" + new string('a', 64), "1", "2026-10-05T00:00:00Z",
                "sha256:" + new string('b', 64)),
            null, new Dictionary<string, string>(), [], false, null, JobStatus.Completed, null, TrialFreshness.Current)
        {
            WordResults = [new AssessmentWordResult("apenda", "Parsed", false, "complete", 3, null) { Readings = [reading] },
                new AssessmentWordResult("zzz", "NoParse", false, "complete", 1, null)],
        };

        var output = CommandTextRenderer.Render(CommandOutcome<TrialRecord>.Success(trial), asJson: false).Output;

        Assert.Contains("Words (2):", output, StringComparison.Ordinal);
        Assert.Contains("  apenda: Parsed", output, StringComparison.Ordinal);
        Assert.Contains("    a (3SG) + penda (love)", output, StringComparison.Ordinal);
        Assert.Contains("  zzz: NoParse", output, StringComparison.Ordinal);
    }

    private static string RenderTrial(DroppedGrammar? dropped) => CommandTextRenderer.Render(
        CommandOutcome<TrialRecord>.Success(new TrialRecord("job/1", "p/1", "sha256:x",
            new BaselineToken("project", "sha256:" + new string('a', 64), "1", "2026-10-05T00:00:00Z",
                "sha256:" + new string('b', 64)),
            null, new Dictionary<string, string>(), [], false, null, JobStatus.Completed, null, TrialFreshness.Current)
        { DroppedGrammar = dropped }), asJson: false).Output;

    [Fact]
    public void TimingTextNamesUnpairedObjectsWithoutInventingWork()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        const string authored = "AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA";
        const string other = "BBBBBBBB-BBBB-BBBB-BBBB-BBBBBBBBBBBB";
        var response = new TimingResponse("assessment/1", "all", "rule", 2, 8, 400, [],
            [
                new TimingAggregateRow(authored, "Plural", 300, 0.375, 2) { Kind = "morph_rule", IdentityQuality = "authored" },
                new TimingAggregateRow(other, "Past", 200, 0.25, 1) { Kind = "morph_rule", IdentityQuality = "authored" },
            ], [])
        {
            Work = new WordWorkSummary(2, 1000, 500, 600),
            ParserWorkAggregates =
            [
                new TimingWorkAggregate(authored.ToLowerInvariant(), "Plural", "morph_rule", 600, false)
                    { IdentityQuality = "authored" },
                new TimingWorkAggregate("unattributed", "unattributed", "unattributed", 400, true),
            ],
        };

        var output = CommandTextRenderer.Render(CommandOutcome<TimingResponse>.Success(response), asJson: false).Output;

        Assert.Contains("Plural [morph_rule:" + authored + "]: comparison unavailable", output, StringComparison.Ordinal);
        Assert.Contains("Past [morph_rule:" + other + "]: comparison unavailable", output, StringComparison.Ordinal);
        Assert.DoesNotContain("600 parser work", output, StringComparison.Ordinal);

    }

    [Fact]
    public void TimingTextDoesNotPresentWorkSharesAsChanges()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var response = new TimingResponse("assessment/1", "all", "rule", 1, 8, 400, [],
            [
                new TimingAggregateRow("parser:strata", "parser", 50, 0.5, 1)
                    { Kind = "parser", IdentityQuality = "synthetic" },
            ], [])
        {
            Work = new WordWorkSummary(1, 100, 100, 100),
            ParserWorkAggregates =
            [
                new TimingWorkAggregate("parser:strata", "parser", "parser", 30, false) { IdentityQuality = "synthetic" },
                new TimingWorkAggregate("rule-r", "R", "morph_rule", 60, false) { IdentityQuality = "authored" },
                new TimingWorkAggregate("unattributed", "unattributed", "unattributed", 10, true),
            ],
        };

        var output = CommandTextRenderer.Render(CommandOutcome<TimingResponse>.Success(response), asJson: false).Output;

        Assert.Contains("parser [parser:parser:strata]: comparison unavailable", output, StringComparison.Ordinal);
        Assert.DoesNotContain("30.0%", output, StringComparison.Ordinal);
        Assert.DoesNotContain("10 parser work", output, StringComparison.Ordinal);

    }

    [Fact]
    public void AssessSummaryTextNamesTheLimitWithoutRawWork()
    {
        var page = new System.Text.Json.Nodes.JsonObject
        {
            ["counts"] = JsonSerializer.SerializeToNode(new AssessmentCounts(1, 0, 0, 0, 1, 0, 0, 0)),
            ["assessment"] = "a/1",
            ["showing"] = "showing 1-1 of 1",
            ["words"] = new System.Text.Json.Nodes.JsonArray(new System.Text.Json.Nodes.JsonObject
                { ["word"] = "big", ["outcome"] = "capped", ["parserWork"] = 20000, ["stoppedBy"] = "work-limit" }),
        };

        var text = CommandTextRenderer.RenderAssessSummary(page, asJson: false).Output;

        Assert.Contains("big: capped, stopped by work-limit", text, StringComparison.Ordinal);
        Assert.DoesNotContain("20000", text, StringComparison.Ordinal);

    }

    [Fact]
    public void TimingTextShowsNoPerRowWorkWhenItWasNotRecordedForEveryWord()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var response = new TimingResponse("assessment/1", "all", "kind", 1, 8, 400, [],
            [new TimingAggregateRow("morph_rule", "morph_rule", 300, 1, 1) { Kind = "morph_rule" }], [])
        {
            Attribution = new WordTimeAttribution(1, 300, 300, 0, 0, 0, false),
        };

        var output = CommandTextRenderer.Render(CommandOutcome<TimingResponse>.Success(response), asJson: false).Output;

        Assert.Contains("comparison unavailable", output, StringComparison.Ordinal);
        Assert.Contains("morph_rule [morph_rule:morph_rule]", output, StringComparison.Ordinal);
        Assert.DoesNotContain("300", output, StringComparison.Ordinal);

    }

    [Fact]
    public void TimingTextDoesNotExposeCostliestWordCounters()
    {
        using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
        var response = new TimingResponse("assessment/1", "all", "rule", 3, 8, 400, [], [],
            [
                new WordRuleTiming("mwalimu", 5, 3) { ParserWork = 7000, WordParserWork = 9000, WordTimeMs = 50, ShareOfWordTime = 0.1 },
                new WordRuleTiming("kitabu", 40, 2) { ParserWork = 900, WordParserWork = 900, WordTimeMs = 80, ShareOfWordTime = 0.5 },
                new WordRuleTiming("hawajafika", 60, 1) { WordTimeMs = 100, ShareOfWordTime = 0.6 },
                new WordRuleTiming("sikilizeni", 7, 4) { ParserWork = 12 },
                new WordRuleTiming("tupu", 1, 1) { ParserWork = 0, WordParserWork = 0 },
            ]);

        var output = CommandTextRenderer.Render(CommandOutcome<TimingResponse>.Success(response), asJson: false).Output;

        Assert.Contains("comparison unavailable", output, StringComparison.Ordinal);
        Assert.DoesNotContain("7,000", output, StringComparison.Ordinal);
        Assert.DoesNotContain(" ms", output, StringComparison.Ordinal);

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
                { YourWords = 2, BySpellingOnly = 1, WordAttributionComplete = true }], 4, 0)
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
    [InlineData(WarningWordsMatch.UnresolvedIdentity, WarningAttributionReason.UnresolvedEnvironmentNotation,
        "Your words: unresolved identity; environment notation does not identify a natural-class object")]
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
