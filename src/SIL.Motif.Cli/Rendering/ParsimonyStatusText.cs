using System.Text;
using SIL.Motif.Contract.Parsimony;

namespace SIL.Motif.Cli.Rendering;

/// <summary>Plain text for a Proposal's Parsimony status.</summary>
internal static class ParsimonyStatusText
{
    internal static string Render(ProposalParsimonyStatus status)
    {
        var text = new StringBuilder();
        text.AppendLine($"Parsimony for {status.ProposalId}: {Wire(status.State)}");
        if (status.StateReason is { } reason) text.AppendLine(reason.Text);
        if (status.Job is { } job)
        {
            text.Append($"  Job {job.JobId}: {job.Status}");
            if (job.Stage is { } stage) text.Append($", {stage.Stage} (step {stage.Step} of {stage.Steps})");
            if (job.QueuedBehind is { } behind) text.Append($", waiting behind a {behind}");
            text.AppendLine();
        }
        if (status.Stale is { } stale)
            text.AppendLine("  Changed since: " + string.Join(", ", stale.Reasons.Select(Wire)));
        if (status.Failure is { } failure) text.AppendLine("  " + failure.Text);
        foreach (var group in status.Groups)
        {
            text.AppendLine($"  {Wire(group.Group)} group: {Wire(group.State)}");
            if (group.Note is { } note) text.AppendLine("    " + note);
            foreach (var change in group.Changes)
                text.AppendLine($"    {change.MeasureId}: {change.NewFindings} new, {change.ResolvedFindings} resolved, " +
                    $"{change.UnchangedFindings} unchanged, {change.EvidenceChangedFindings} with changed evidence");
        }
        text.AppendLine("This is advisory: it never changes the Trial, Readiness or Apply.");
        return text.ToString();
    }

    private static string Wire<T>(T value) where T : struct, Enum =>
        System.Text.Json.JsonSerializer.Serialize(value).Trim('"');
}
