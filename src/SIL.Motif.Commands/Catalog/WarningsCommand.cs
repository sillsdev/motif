using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Catalog;

/// <summary>Reads grammar findings already stored for the current Baseline.</summary>
public static class WarningsCommand
{
    /// <summary>Returns stored findings without running the parser.</summary>
    public static CommandOutcome<WarningsResponse> Warnings(WarningsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var stored = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(request.ProjectPath));
        return stored.Succeeded
            ? CommandOutcome<WarningsResponse>.Success(FromCheck(stored.Value!.Check, request.Kind, request.LeftOut))
            : CommandOutcome<WarningsResponse>.Refused(stored.Refusal!);
    }

    internal static WarningsResponse FromCheck(GrammarCheckResponse? check, string? kind = null, bool leftOut = false)
    {
        var findings = (check?.Findings ?? [])
            .Where(finding => (kind is null || string.Equals(finding.Code, kind, StringComparison.OrdinalIgnoreCase)) &&
                (!leftOut || finding.Severity == GrammarDiagnosticLevel.Warning))
            .ToArray();
        var byKind = findings
            .GroupBy(finding => (finding.Code, finding.Group, finding.Severity))
            .Select(group => new GrammarWarningSummary(group.Key.Code ?? string.Empty, group.Key.Group,
                group.Key.Severity, group.Count()))
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.Code, StringComparer.Ordinal)
            .ToArray();
        return new WarningsResponse(check?.HasBaseline ?? true, check is not null, findings, byKind,
            findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Warning),
            findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Information));
    }
}
