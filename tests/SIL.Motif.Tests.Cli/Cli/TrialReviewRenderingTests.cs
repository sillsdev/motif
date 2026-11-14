using System.Reflection;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Review;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class TrialReviewRenderingTests
{
    [Fact]
    public void EmptyFlagsSaysNotBadNeverGoodAndIncompleteIsNotFailed()
    {
        var report = (ReportView)Create(typeof(ReportView));
        report = report with { Summary = report.Summary with
        {
            Notice = "No red flags means only 'not bad', never 'good'.",
            Flags = [], Incomplete = report.Summary.Incomplete with { RequiredCases = 7, AffectedWords = 7 }
        } };
        var text = TrialReviewTextRenderer.Render(new AvailableTrialResult(report));
        Assert.StartsWith("No red flags observed.", text, StringComparison.Ordinal);
        Assert.Contains("not bad", text, StringComparison.Ordinal);
        Assert.Contains("Incomplete: 7", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Failed", text, StringComparison.OrdinalIgnoreCase);
        Assert.True(text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length <= 24);
    }

    [Fact]
    public void GainsShowAttestedAndGeneratedSeparatelyWithTheSizeDeltaAndGate()
    {
        var report = (ReportView)Create(typeof(ReportView));
        var size = GrammarSizeDelta.None with { Entries = 40, Variants = 6 };
        var gains = GainSummary.Create(95, 924, size, new GainGatePolicy(GainGateMode.Hold, 1.5), incomplete: false);
        var text = TrialReviewTextRenderer.Render(new AvailableTrialResult(report with
            { Summary = report.Summary with { Flags = [], Gains = gains } }));
        Assert.Contains("Attested gained: 95; generated gained: 924", text, StringComparison.Ordinal);
        Assert.Contains("Grammar size delta: 46 objects (entries 40", text, StringComparison.Ordinal);
        Assert.Contains("gain gate hold: WithinThreshold", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitTraceShowsNodesAndContinuationWithoutRawCost()
    {
        var report = (ReportView)Create(typeof(ReportView));
        var trace = new TracePage("trace", "report", "case", Side.After, TraceOrigin.DiagnosticRerun,
            "parser", "grammar", "scope", "schema", "complete", false, "capture-limit", ReadingAgreement.Different,
            [new TraceNode("1", null, "rule", "A rule", "reason", [], null)], "next", []);
        var text = TrialReviewTextRenderer.Render(new AvailableTrialResult(report with { Trace = trace }));
        Assert.Contains("A rule", text, StringComparison.Ordinal);
        Assert.Contains("Next cursor: next", text, StringComparison.Ordinal);
        Assert.Contains("truncated", text, StringComparison.Ordinal);
        Assert.Contains("differ", text, StringComparison.Ordinal);
        Assert.DoesNotContain("elapsed", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailurePositionExcerptIsLabeledInText()
    {
        var report = (ReportView)Create(typeof(ReportView));
        var word = (WordRow)Create(typeof(WordRow));
        var token = new TokenRow("case", "word", "position", 1, "source [excerpt]", word.Before, word.After, []);
        word = word with { SamplePositions = [token], SamplePositionsTotal = 7 };
        var section = (SectionView)Create(typeof(SectionView));
        var group = new CauseGroup("coverage", 1, "forms", [word], 0,
            new ReadTarget(TargetKind.Resource, "memberships", "motif://memberships", null, null));
        var text = TrialReviewTextRenderer.Render(new AvailableTrialResult(report with
        {
            Sections = [section with { Groups = [group] }]
        }));
        Assert.Contains("7 positions: position source [excerpt]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WordDetailRendersReadingsExpectationsAndContinuation()
    {
        var report = (ReportView)Create(typeof(ReportView));
        var row = (WordRow)Create(typeof(WordRow));
        row = row with { Before = row.Before with { ApprovedUnknown = 1 }, After = row.After with { ApprovedUnknown = 1 } };
        var reading = new ReadingRow("reading-id", Side.Before, IdentityState.Exact,
            [new Morph("form-id", "msa-id", [], "cat", "gloss")], true, ["approved"], ["source"]);
        var expectation = new ExpectationRow("approved", ExpectationAssertion.CorrectReading, ReferenceAuthority.Human,
            "actor", "revision", "reading-id", ["source"], ExpectationOutcome.Matched, ExpectationOutcome.Missing);
        ReviewPage Page(string section, IReadOnlyList<ReviewRow> items, string? cursor = null) =>
            new(section, "filter", "rows", items.Count, items.Count, items.Count, 10, cursor, ListAvailability.Complete, items);
        var word = new WordView(row, Page("memberships", []), Page("expectations", [expectation]),
            Page("readings", [reading], "next"), Page("sources", [new SourceRow("source", "citation", "line", null, false, "actor")]), null);
        var result = new AvailableTrialResult(report with { Word = word });
        var text = TrialReviewTextRenderer.Render(result);
        Assert.Contains("reading-id", text, StringComparison.Ordinal);
        Assert.Contains("form-id", text, StringComparison.Ordinal);
        Assert.Contains("Matched before; Missing after", text, StringComparison.Ordinal);
        Assert.Contains("unknown 1", text, StringComparison.Ordinal);
        Assert.Contains("actor", text, StringComparison.Ordinal);
        Assert.Contains("citation", text, StringComparison.Ordinal);
        Assert.Contains("readings next cursor: next", text, StringComparison.Ordinal);
        Assert.Contains("reading-id", System.Text.Json.JsonSerializer.Serialize<ReviewRow>(reading, ReviewJson.Options), StringComparison.Ordinal);
    }

    private static object Create(Type type)
    {
        if (type == typeof(BaselineToken)) return new BaselineToken("project", "sha256:" + new string('a', 64), "v1",
            "2026-01-01T00:00:00Z", "sha256:" + new string('b', 64));
        if (type == typeof(string)) return "test";
        if (type == typeof(long)) return 1L;
        if (type == typeof(int)) return 1;
        if (type == typeof(double)) return 1d;
        if (type == typeof(bool)) return false;
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0)!;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            return Array.CreateInstance(type.GetGenericArguments()[0], 0);
        if (type == typeof(ReadTarget)) return new ReadTarget(TargetKind.Resource, "test", "motif://test", null, null);
        if (type == typeof(EvidencePin)) return Create(typeof(SetEvidencePin));
        if (type == typeof(FlagFacts) || type == typeof(WorkChange)) return new WorkChange(null, 0, "test", WorkComparison.NotComparable, []);
        var constructor = type.GetConstructors().Single();
        return constructor.Invoke(constructor.GetParameters().Select(parameter =>
            Nullable.GetUnderlyingType(parameter.ParameterType) is not null ||
            new NullabilityInfoContext().Create(parameter).ReadState == NullabilityState.Nullable
                ? null : Create(parameter.ParameterType)).ToArray());
    }
}
