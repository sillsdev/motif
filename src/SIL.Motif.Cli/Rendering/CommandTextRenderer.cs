using System;
using System.Text;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Jobs;
using ProjectionText = SIL.Motif.Projection.Rendering.CommandTextRenderer;

namespace SIL.Motif.Cli.Rendering;

/// <summary>
/// Turns a Corpus, configuration, Report, comparison, or job command's typed
/// <see cref="CommandOutcome{T}"/> into the same human text and <c>--json</c> shape the pre-typed
/// handlers used to render themselves. A <see cref="Refusal"/> becomes a <see cref="FailureEnvelope"/>
/// only here — a command handler never constructs one.
/// </summary>
public static class CommandTextRenderer
{
    /// <param name="successAsJson">
    /// Whether a successful result renders as JSON. A job just enqueued never grew a JSON success shape
    /// and must keep printing its bare id even when the caller passed <c>--json</c>; a refusal always
    /// follows <paramref name="asJson"/> regardless.
    /// </param>
    public static CommandResult Render<T>(CommandOutcome<T> outcome, bool asJson, bool successAsJson = true)
        where T : class =>
        outcome.Succeeded
            ? RenderSuccess(outcome.Value!, asJson && successAsJson)
            : RenderRefusal(outcome.Refusal!, asJson);

    private static CommandResult RenderSuccess<T>(T value, bool asJson) where T : class
    {
        if (asJson)
            return new CommandResult(0, ProjectionJson.Serialize(value) + Environment.NewLine);

        var text = value switch
        {
            CorpusListProjection p => ProjectionText.Render(p),
            CorpusDetailProjection p => ProjectionText.Render(p),
            ProjectConfigurationProjection p => ProjectionText.Render(p),
            DryRunProjection p => ProjectionText.Render(p),
            CorpusAddedResponse r => RenderCorpusAdded(r),
            CorpusDocumentAddedResponse r => RenderCorpusDocumentAdded(r),
            CorpusBundleAddedResponse r => RenderCorpusBundleAdded(r),
            ReportKindListResponse r => RenderReportKindList(r),
            ReportResponse r => RenderReport(r),
            CompareResponse r => RenderCompare(r),
            JobEnqueuedResponse r => r.JobId + Environment.NewLine,
            JobStatusResponse r => RenderJobStatus(r),
            JobQueueListResponse r => RenderJobQueueList(r),
            JobAssessmentsResponse r => RenderJobAssessments(r),
            BaselineCaptureResponse r => RenderBaselineCaptured(r),
            _ => throw new NotSupportedException($"No text rendering registered for '{typeof(T)}'."),
        };
        return new CommandResult(0, text);
    }

    // Corpus's own refusals never carried an "error: " prefix before they were typed; preserved verbatim.
    private static CommandResult RenderRefusal(Refusal refusal, bool asJson)
    {
        if (asJson)
        {
            var envelope = new FailureEnvelope(
                refusal.Reason, refusal.Message, refusal.Facts.Count > 0 ? refusal.Facts : null, refusal.Code);
            return new CommandResult(
                FailureEnvelope.ExitCodeFor(refusal.Reason),
                ProjectionJson.Serialize(envelope) + Environment.NewLine, refusal.Reason);
        }

        var prefix = refusal.Code.StartsWith("corpus.", StringComparison.Ordinal) ? "" : "error: ";
        return new CommandResult(
            FailureEnvelope.ExitCodeFor(refusal.Reason), prefix + refusal.Message + Environment.NewLine,
            refusal.Reason);
    }

    private static string RenderCorpusAdded(CorpusAddedResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Created corpus '{r.CorpusId}'.");
        sb.AppendLine($"  Origin:       {r.Description}");
        if (!string.IsNullOrWhiteSpace(r.Uri)) sb.AppendLine($"  Location:     {r.Uri}");
        sb.AppendLine($"  Licence:      {r.Licence ?? "(none recorded)"}");
        sb.AppendLine($"  Tokenisation: {r.Tokeniser} {r.TokeniserVersion}");
        sb.AppendLine();
        sb.AppendLine(r.DerivationNote);
        sb.AppendLine();
        sb.AppendLine(
            "Add documents with: motif add-document --corpus " + r.CorpusId + " --doc <id> --source <file-or-url>");
        return sb.ToString();
    }

    private static string RenderCorpusDocumentAdded(CorpusDocumentAddedResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Added document '{r.DocumentId}' to corpus '{r.CorpusId}'.");
        sb.AppendLine($"  Title:      {r.Title}");
        sb.AppendLine($"  Source:     {r.Source}");
        sb.AppendLine($"  Characters: {r.CharacterCount:N0}");
        sb.AppendLine($"  SHA-256:    {r.ContentSha256}");
        if (!string.IsNullOrWhiteSpace(r.Licence)) sb.AppendLine($"  Licence:    {r.Licence}");
        return sb.ToString();
    }

    private static string RenderCorpusBundleAdded(CorpusBundleAddedResponse r)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Ingested corpus '{r.CorpusId}' from bundle: {r.DocumentCount} document(s).");
        sb.AppendLine($"  Origin:  {r.OriginDescription}");
        sb.AppendLine($"  Licence: {r.Licence ?? "(none recorded)"}");
        sb.AppendLine();
        sb.AppendLine(r.DerivationRestrictions);
        sb.AppendLine();
        sb.AppendLine(r.AccuracyClaimsNote);
        return sb.ToString();
    }

    private static string RenderReportKindList(ReportKindListResponse response)
    {
        var text = new StringBuilder();
        if (response.Kinds.Count == 0)
        {
            text.AppendLine("No report kinds registered.");
            return text.ToString();
        }
        foreach (var kind in response.Kinds)
            text.AppendLine(kind.Kind + "  " + kind.Description);
        return text.ToString();
    }

    private static string RenderReport(ReportResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine("Report " + response.ReportId);
        text.AppendLine("  Assessment: " + response.AssessmentId);
        text.AppendLine("  Kind:       " + response.Kind);
        text.AppendLine(response.Text);
        return text.ToString();
    }

    private static string RenderCompare(CompareResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine("Comparison " + response.AssessmentId);
        text.Append(response.Text);
        return text.ToString();
    }

    private static string RenderJobStatus(JobStatusResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine("Job " + response.JobId);
        text.AppendLine("  Kind:    " + response.Kind);
        text.AppendLine("  Status:  " + response.Status);
        text.AppendLine("  Attempt: " + response.Attempt);
        text.AppendLine("  Updated: " + response.UpdatedUtc);
        if (response.QueueOrder is { } queueOrder)
            text.AppendLine("  Queue order: " + queueOrder.ToString("R"));
        return text.ToString();
    }

    private static string RenderJobQueueList(JobQueueListResponse response)
    {
        var text = new StringBuilder();
        if (response.Jobs.Count == 0)
        {
            text.AppendLine("No active jobs.");
            return text.ToString();
        }
        for (var index = 0; index < response.Jobs.Count; index++)
        {
            var job = response.Jobs[index];
            text.AppendLine((index + 1) + ". " + job.JobId + "  " + job.Kind + "  " +
                JobStatusJson.ToWire(job.Status) + "  " + job.ProjectPath);
        }
        return text.ToString();
    }

    private static string RenderBaselineCaptured(BaselineCaptureResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine("Captured Baseline for " + response.FwDataPath);
        text.AppendLine("  Project identity: " + response.Token.ProjectIdentity);
        text.AppendLine("  Bundle digest:    " + response.Token.BundleDigest);
        text.AppendLine("  Captured:         " + response.Token.CapturedUtc);
        text.AppendLine("  Last save:        " + response.SourceLastWriteUtc.ToString("O") +
            "  (as of FieldWorks' last save)");
        text.AppendLine("  FieldWorks:       " + (response.FieldWorksHeldProject
            ? "holds the project open"
            : "does not currently hold the project"));
        text.AppendLine(response.ReusedExistingBytes
            ? "  The saved bytes matched the current Baseline; nothing new was written."
            : "  A new Baseline was captured and published.");
        return text.ToString();
    }

    private static string RenderJobAssessments(JobAssessmentsResponse response)
    {
        var text = new StringBuilder();
        text.AppendLine("Job " + response.JobId);
        if (response.Assessments.Count == 0)
        {
            text.AppendLine("  No Assessments.");
            return text.ToString();
        }
        foreach (var assessment in response.Assessments)
        {
            text.AppendLine("  " + assessment.AssessmentId + "  " + assessment.Assessor + "  " +
                assessment.Kind + "  " + assessment.SavedUtc);
        }
        return text.ToString();
    }
}
