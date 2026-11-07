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
