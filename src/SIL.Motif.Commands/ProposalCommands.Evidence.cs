using SIL.Motif.Projection;
using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Host.Store;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Projection.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
    /// <summary>
    /// Reads every decision evidence record staged for one wordform or analysis from Motif's store; the FieldWorks
    /// project is not opened.
    /// </summary>
    public static CommandOutcome<DecisionEvidenceShowResponse> EvidenceShow(EvidenceShowRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, locator) =>
        {
            try
            {
                var rows = ReadEvidenceRows(database, out _)
                    .Where(row => string.Equals(row.Subject, request.Subject, StringComparison.Ordinal)).ToArray();
                return CommandOutcome<DecisionEvidenceShowResponse>.Success(
                    new DecisionEvidenceShowResponse(request.Subject, rows));
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<DecisionEvidenceShowResponse>.Refused(ProposalLoadRefusal(exception));
            }
        });

    /// <summary>Lists decision evidence with counts per tier first, then one page of rows.</summary>
    public static CommandOutcome<DecisionEvidenceListResponse> EvidenceList(EvidenceListRequest request)
    {
        if (request.Tier is not null && !DecisionEvidence.Tiers.Contains(request.Tier, StringComparer.Ordinal))
            return CommandOutcome<DecisionEvidenceListResponse>.Refused(new Refusal(
                "evidence.unknown-tier", FailureReason.InvalidArgument,
                $"'{request.Tier}' is not a tier. Use one of: {string.Join(", ", DecisionEvidence.Tiers)}."));
        if (request.Offset < 0 || request.Limit < 1 || request.Limit > 500)
            return CommandOutcome<DecisionEvidenceListResponse>.Refused(new Refusal(
                "evidence.invalid-page", FailureReason.InvalidArgument,
                "The offset must be at least 0 and the limit between 1 and 500."));
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, locator) =>
        {
            try
            {
                var all = ReadEvidenceRows(database, out var applied);
                var matching = request.Tier is null ? all : all.Where(row => row.Tier == request.Tier).ToArray();
                var counts = matching.GroupBy(row => row.Tier).OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count());
                return CommandOutcome<DecisionEvidenceListResponse>.Success(new DecisionEvidenceListResponse(
                    applied, matching.Length, counts, request.Offset, request.Limit,
                    matching.Skip(request.Offset).Take(request.Limit).ToArray()));
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<DecisionEvidenceListResponse>.Refused(ProposalLoadRefusal(exception));
            }
        });
    }

    private static DecisionEvidenceRow[] ReadEvidenceRows(MotifDatabase database, out int appliedCount)
    {
        var repository = new ProposalRepository(database);
        var rows = new List<DecisionEvidenceRow>();
        var applied = repository.List(new ProposalListFilter(ManifestStatus.Applied, IncludeArchived: true))
            .OrderBy(record => record.ProposalId.Value, StringComparer.Ordinal).ToArray();
        appliedCount = applied.Length;
        foreach (var record in applied)
        {
            var (_, envelope) = repository.GetFinalized(record.ProposalId);
            foreach (var operation in envelope.Operations)
            {
                var evidence = DecisionEvidence.FromProvenance(operation.Provenance);
                var subject = operation.Target?.Value ?? operation.EntityId?.Value;
                if (evidence is null || subject is null) continue;
                rows.Add(new DecisionEvidenceRow(record.ProposalId.Value, record.Label, operation.OperationId.Value,
                    operation.Kind, subject, evidence.Tier, evidence.Sources, evidence.Detail, evidence.StagedBy));
            }
        }
        return rows.ToArray();
    }
}
