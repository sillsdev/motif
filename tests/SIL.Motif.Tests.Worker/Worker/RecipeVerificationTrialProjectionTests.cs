using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Parsimony;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class RecipeVerificationTrialProjectionTests
{
    private static readonly string Digest = "sha256:" + new string('a', 64);
    private static readonly BaselineToken Baseline = new("project", Digest, "projection/v1",
        "2026-10-07T12:00:00Z", Digest);

    [Fact]
    public void ProjectionRetainsTheBaselineAndEffectiveParserOptionsFromTheStoredTrial()
    {
        var record = Assessment(invocation: Invocation());

        var projected = RecipeVerificationTrialProjection.From(record);

        Assert.Equal(Baseline, projected.Baseline);
        Assert.Equal(1000, projected.Options!.PerWordTimeoutMs);
        Assert.Equal(200_000, projected.Options.PerWordStepLimit.Steps);
        Assert.Equal(1, projected.Options.Threads);
        Assert.Equal("sha256:" + new string('b', 64), projected.Options.ParserSha256);
        Assert.Equal(new[] { "ara" }, projected.SelectionWords);
        Assert.Single(projected.Assessment.Words);
        Assert.Equal("ara", projected.Assessment.Words[0].Morphology!.Word);
        Assert.Empty(projected.Unavailable);
    }

    [Fact]
    public void MissingInvocationIsReportedAsUnavailableInsteadOfAnUnboundedRun()
    {
        var projected = RecipeVerificationTrialProjection.From(Assessment(invocation: null));

        Assert.Null(projected.Options);
        Assert.Contains(projected.Unavailable, reason => reason.Contains("invocation limits", StringComparison.Ordinal));
    }

    private static AssessmentRecord Assessment(BatchInvocationEvidence? invocation) => new(
        "assessment-id", null, null, "pangloss", "ParseTime", "{}", Digest, "pangloss", "v7",
        JsonSerializer.Serialize(Baseline, MotifJson.CreateOptions()), Selection.Create("verify", ["ara"]),
        null, null, Digest, "fingerprint", "batch", 0, "2026-10-07T12:00:00Z",
        Words: [new AssessedWord("ara", "analysed", [], 5)
        {
            Morphology = new ParseWordEvidence("fieldworks-parse-analysis/v1", 0, "ara", 5,
                false, false, false, [], []),
        }])
    {
        Invocation = invocation,
    };

    private static BatchInvocationEvidence Invocation() => new(
        "invocation-id", "/source.fwdata", Digest, "sha256:" + new string('b', 64),
        "/words.txt", Digest, "/analyses.jsonl", Digest, "/stderr.txt", Digest,
        1000, new StepCap(200_000), 1, true)
    {
        AnalysesPath = "/analyses.jsonl",
        AnalysesSha256 = Digest,
    };
}
