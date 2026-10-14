using System;
using System.Collections.Generic;
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
        var stored = StoredGrammarCheckQuery.Query(new GrammarCheckRequest(request.ProjectPath, request.ProposalId));
        if (!stored.Succeeded) return CommandOutcome<WarningsResponse>.Refused(stored.Refusal!);
        if (stored.Value!.Check is not { } check)
            return CommandOutcome<WarningsResponse>.Refused(NotRunRefusal(request.ProposalId is null
                ? "The current Baseline has not been checked. Run: motif grammar check --project <fwdata>"
                : "The latest Trial for this Proposal has no grammar check to read.", stored.Value.LastParserRefusal));
        return CommandOutcome<WarningsResponse>.Success(FromCheck(check, request.Kind, request.LeftOut) with
        { LastParserRefusal = stored.Value.LastParserRefusal, Provenance = stored.Value.Provenance });
    }

    internal static WarningsResponse FromCheck(GrammarCheckResponse? check, string? kind = null, bool leftOut = false)
    {
        var findings = (check?.Findings ?? [])
            .Where(finding => (kind is null || string.Equals(finding.Code, kind, StringComparison.OrdinalIgnoreCase)) &&
                (!leftOut || finding.Severity == GrammarDiagnosticLevel.Warning))
            .ToArray();
        var byKind = findings
            .GroupBy(finding => (finding.Code, finding.Group, finding.Severity))
            .Select(group =>
            {
                var touched = WarningWordsQuery.TouchedNamed(group.ToArray());
                return new GrammarWarningSummary(group.Key.Code ?? string.Empty, group.Key.Group,
                    group.Key.Severity, group.Count())
                {
                    YourWords = touched?.Words,
                    WordAttributionComplete = touched?.IsComplete,
                    AttributionLimits = touched?.AttributionLimits ?? group.SelectMany(finding => finding.AttributionLimits).Distinct().ToArray(),
                    ByMembershipOnly = touched?.ByMembershipOnly,
                    BySpellingOnly = touched?.BySpellingOnly,
                };
            })
            .OrderByDescending(row => row.Count)
            .ThenBy(row => row.Code, StringComparer.Ordinal)
            .ToArray();
        return new WarningsResponse(check?.HasBaseline ?? true, check is not null, findings, byKind,
            findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Warning),
            findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Information))
        {
            ErrorCount = findings.Count(finding => finding.Severity == GrammarDiagnosticLevel.Error),
            YourWords = check is null ? null : WarningWordsQuery.TouchedNamed(findings),
            CheckedLexemeCount = check?.CheckedLexemeCount,
            CheckedRuleCount = check?.CheckedRuleCount,
            Advisories = check?.Advisories ?? [],
        };
    }

    private static Refusal NotRunRefusal(string message, Refusal? lastParserRefusal)
    {
        IReadOnlyDictionary<string, string>? facts = null;
        if (lastParserRefusal is { } parserRefusal)
        {
            var copiedFacts = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [RefusalFactNames.LastParserRefusalCode] = parserRefusal.Code,
                [RefusalFactNames.LastParserRefusalReason] = parserRefusal.Reason.ToString(),
                [RefusalFactNames.LastParserRefusalMessage] = parserRefusal.Message,
            };
            foreach (var (name, value) in parserRefusal.Facts)
                copiedFacts[RefusalFactNames.LastParserRefusalFactPrefix + name] = value;
            facts = copiedFacts;
            message += $" The last parser attempt was refused ({parserRefusal.Code}): {parserRefusal.Message}";
        }

        return new Refusal("grammarcheck.not-run", FailureReason.Refused, message, facts,
            lastParserRefusal?.ParserDiagnostic);
    }
}
