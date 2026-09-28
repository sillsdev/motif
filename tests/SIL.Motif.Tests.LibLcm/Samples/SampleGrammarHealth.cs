using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Samples;

internal static class SampleGrammarHealth
{
    public static SampleGrammarHealthSnapshot AssertFixedProjectHasNoErrors(string projectPath, string root)
    {
        var snapshot = Read(projectPath, root);
        Assert.True(snapshot.Errors == 0,
            "The fixed sample has error-level grammar-health findings: " +
            string.Join("; ", snapshot.Findings.Where(finding => finding.Level == "error")
                .Select(finding => finding.Code + ": " + finding.Description)));
        return snapshot;
    }

    public static SampleGrammarHealthSnapshot Read(string projectPath, string root)
    {
        var capture = BaselineCaptureCommand.Capture(
            new BaselineCaptureRequest(projectPath), Path.Combine(root, "motif-root"));
        Assert.True(capture.Succeeded, capture.Refusal?.Message);

        var outcome = GrammarCheckQuery.Query(new GrammarCheckRequest(projectPath));
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.True(outcome.Value!.HasBaseline, "Grammar-health did not find the captured Baseline.");

        var findings = outcome.Value.Findings
            .Select(finding => new SampleGrammarFinding(
                finding.Code ?? throw new InvalidOperationException("A grammar-health finding has no code."),
                finding.Severity.ToWireValue(),
                finding.Description ?? throw new InvalidOperationException("A grammar-health finding has no description.")))
            .OrderBy(finding => finding.Code, StringComparer.Ordinal)
            .ThenBy(finding => finding.Level, StringComparer.Ordinal)
            .ThenBy(finding => finding.Description, StringComparer.Ordinal)
            .ToArray();
        return new SampleGrammarHealthSnapshot(
            findings.Count(finding => finding.Level == "error"),
            findings.Count(finding => finding.Level == "warning"),
            findings.Count(finding => finding.Level == "info"),
            findings);
    }
}

internal sealed record SampleGrammarHealthSnapshot(
    int Errors, int Warnings, int Info, SampleGrammarFinding[] Findings);

internal sealed record SampleGrammarFinding(string Code, string Level, string Description);
