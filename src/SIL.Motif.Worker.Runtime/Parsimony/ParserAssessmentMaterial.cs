using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Parsimony;

internal static class ParserAssessmentMaterial
{
    public static ParserAssessmentSourceArtifact? ReadSourceArtifact(MotifDatabase database,
        IReadOnlyList<string> assessmentIds)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(assessmentIds);
        if (assessmentIds.Count == 0) return null;

        var repository = new AssessmentRepository(database);
        ParserAssessmentSourceArtifact? artifact = null;
        foreach (var assessmentId in assessmentIds)
        {
            AssessmentRecord assessment;
            try
            {
                assessment = repository.Get(assessmentId);
            }
            catch (KeyNotFoundException exception)
            {
                throw new InvalidDataException($"Parser Assessment '{assessmentId}' was not found.", exception);
            }
            var invocation = assessment.Invocation;
            if (invocation is null || string.IsNullOrWhiteSpace(invocation.SourcePath))
                throw new InvalidDataException(
                    $"Parser Assessment '{assessmentId}' has no retained candidate source; rerun its Trial.");
            AssessmentSourceArtifactStore.Verify(invocation.SourcePath, invocation.SourceBytesSha256);
            var next = new ParserAssessmentSourceArtifact(invocation.SourcePath, invocation.SourceBytesSha256);
            if (artifact is not null && artifact != next)
                throw new InvalidDataException("Parser Assessments do not share one retained candidate source.");
            artifact = next;
        }
        return artifact;
    }

    public static IReadOnlyList<ParserAssessmentSource> Read(MotifDatabase database,
        IReadOnlyList<string> assessmentIds, BaselineToken baselineToken, string sourceSha256,
        string parserSha256, out IReadOnlyList<ReportableAssessment> reportableAssessments,
        string? proposalId = null, string? proposalIntentDigest = null)
    {
        ArgumentNullException.ThrowIfNull(database);
        var repository = new AssessmentRepository(database);
        var result = new List<ParserAssessmentSource>(assessmentIds.Count);
        var reportable = new List<ReportableAssessment>(assessmentIds.Count);
        foreach (var assessmentId in assessmentIds)
        {
            AssessmentRecord assessment;
            try
            {
                assessment = repository.Get(assessmentId);
            }
            catch (KeyNotFoundException exception)
            {
                throw new InvalidDataException($"Parser Assessment '{assessmentId}' was not found.", exception);
            }
            BaselineToken? recordedToken;
            try
            {
                recordedToken = JsonSerializer.Deserialize<BaselineToken>(assessment.BaselineToken,
                    MotifJson.CreateOptions());
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException($"Parser Assessment '{assessmentId}' has an invalid Baseline binding.", exception);
            }
            var invocation = assessment.Invocation;
            if (recordedToken != baselineToken)
                throw new InvalidDataException(
                    $"Parser Assessment '{assessmentId}' does not match the candidate Baseline.");
            if (assessment.Assessor != "pangloss" || assessment.Kind != AssessmentKinds.ParseTime ||
                assessment.ProposalId?.Value != proposalId || assessment.ProposalIntentDigest != proposalIntentDigest)
                throw new InvalidDataException(
                    $"Parser Assessment '{assessmentId}' does not match the candidate Proposal.");
            if (invocation is null || assessment.GrammarSourceSha256 != sourceSha256 ||
                invocation.SourceBytesSha256 != sourceSha256)
                throw new InvalidDataException(
                    $"Parser Assessment '{assessmentId}' does not match the candidate source bytes.");
            if (invocation.ExecutableBytesSha256 != "sha256:" + parserSha256)
                throw new InvalidDataException(
                    $"Parser Assessment '{assessmentId}' does not match the PanGloss executable.");

            result.Add(new ParserAssessmentSource(assessment.AssessmentId, assessment.Assessor, assessment.Kind,
                assessment.ProposalId?.Value, assessment.ProposalIntentDigest, invocation.SourceBytesSha256,
                invocation.ExecutableBytesSha256, assessment.ModelFingerprint, invocation.InvocationId,
                invocation.SourcePath, invocation.AnalysesPath, invocation.AnalysesSha256,
                invocation.PerWordTimeoutMs, invocation.PerWordStepLimit.ToArgument(), invocation.Threads,
                assessment.Words?.Select(item => item.Morphology).ToArray() ?? []));
            reportable.Add(assessment.ToReportable());
        }
        reportableAssessments = Array.AsReadOnly(reportable.ToArray());
        return Array.AsReadOnly(result.ToArray());
    }
}

internal sealed record ParserAssessmentSourceArtifact(string SourcePath, string SourceBytesSha256);
