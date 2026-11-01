using SIL.Motif.Projection;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
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

    /// <summary>
    /// Reads one bounded page of every decision evidence record, in Proposal id order, so a caller can
    /// stream a whole export without holding it; pass each page's <c>Next</c> as the following request's <c>After</c>.
    /// A page loads only the Proposals it reads rows from, pinned by `APageLoadsOnlyTheProposalsItReadsRowsFrom`,
    /// and refuses a cursor the store or filter has outgrown, pinned by `ACursorRefusesWhenAProposalWasAppliedAfterItWasIssued`.
    /// </summary>
    public static CommandOutcome<DecisionEvidenceExportResponse> EvidenceExport(EvidenceExportRequest request)
    {
        if (request.Tier is not null && !DecisionEvidence.Tiers.Contains(request.Tier, StringComparer.Ordinal))
            return CommandOutcome<DecisionEvidenceExportResponse>.Refused(new Refusal(
                RefusalCodes.EvidenceUnknownTier, FailureReason.InvalidArgument,
                $"'{request.Tier}' is not a tier. Use one of: {string.Join(", ", DecisionEvidence.Tiers)}."));
        if (request.Limit < 1 || request.Limit > 500)
            return CommandOutcome<DecisionEvidenceExportResponse>.Refused(new Refusal(
                RefusalCodes.EvidenceInvalidPage, FailureReason.InvalidArgument,
                "The limit must be between 1 and 500."));
        ExportCursor? cursor = null;
        if (request.After is not null && !ExportCursor.TryParse(request.After, out cursor))
            return CommandOutcome<DecisionEvidenceExportResponse>.Refused(new Refusal(
                RefusalCodes.EvidenceInvalidCursor, FailureReason.InvalidArgument,
                "'after' must be the 'next' value an earlier page of this export returned."));
        if (cursor is not null && cursor.Tier != request.Tier)
            return CommandOutcome<DecisionEvidenceExportResponse>.Refused(new Refusal(
                RefusalCodes.EvidenceInvalidCursor, FailureReason.InvalidArgument,
                "'after' came from an export with a different tier filter. Start the export again with the same tier."));
        return ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, locator) =>
        {
            string? reading = null;
            try
            {
                var repository = new ProposalRepository(database);
                var applied = AppliedListings(repository);
                if (cursor is not null && cursor.Applied != applied.Length)
                    return CommandOutcome<DecisionEvidenceExportResponse>.Refused(new Refusal(
                        RefusalCodes.EvidenceInvalidCursor, FailureReason.InvalidArgument,
                        "Applied Proposals changed since this export began, so its later pages could miss records. " +
                        "Start the export again."));
                var rows = new List<DecisionEvidenceRow>();
                foreach (var listing in applied)
                {
                    var proposal = listing.ProposalId.Value;
                    if (cursor is not null && string.CompareOrdinal(proposal, cursor.Proposal) < 0) continue;
                    var taken = cursor is not null && cursor.Proposal == proposal ? cursor.Skip : 0;
                    reading = proposal;
                    foreach (var row in RowsOf(repository, listing).Where(row => request.Tier is null || row.Tier == request.Tier)
                                 .Skip(taken))
                    {
                        if (rows.Count == request.Limit)
                            return CommandOutcome<DecisionEvidenceExportResponse>.Success(new DecisionEvidenceExportResponse(
                                DecisionEvidenceExport.Format, rows,
                                new ExportCursor(applied.Length, request.Tier, proposal, taken).ToString()));
                        rows.Add(row);
                        taken++;
                    }
                }
                return CommandOutcome<DecisionEvidenceExportResponse>.Success(
                    new DecisionEvidenceExportResponse(DecisionEvidenceExport.Format, rows, null));
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                var refusal = ProposalLoadRefusal(exception);
                return CommandOutcome<DecisionEvidenceExportResponse>.Refused(reading is null ? refusal
                    : new Refusal(refusal.Code, refusal.Reason, refusal.Message,
                        new Dictionary<string, string> { ["proposalId"] = reading }));
            }
        });
    }

    // The cursor carries the Applied count and tier so a changed store or filter is refused.
    private sealed record ExportCursor(int Applied, string? Tier, string Proposal, int Skip)
    {
        public override string ToString() => string.Join(':', Applied.ToString(CultureInfo.InvariantCulture), Tier ?? "",
            Proposal, Skip.ToString(CultureInfo.InvariantCulture));

        public static bool TryParse(string text, out ExportCursor? cursor)
        {
            cursor = null;
            var first = text.IndexOf(':');
            var second = first < 0 ? -1 : text.IndexOf(':', first + 1);
            var last = text.LastIndexOf(':');
            if (second < 0 || last <= second) return false;
            var tier = text[(first + 1)..second];
            if (!int.TryParse(text[..first], NumberStyles.None, CultureInfo.InvariantCulture, out var applied) ||
                !CanonicalId.TryParse(text[(second + 1)..last], out var proposal) ||
                !int.TryParse(text[(last + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var skip) ||
                tier.Length > 0 && !DecisionEvidence.Tiers.Contains(tier, StringComparer.Ordinal)) return false;
            cursor = new ExportCursor(applied, tier.Length == 0 ? null : tier, proposal.Value, skip);
            return true;
        }
    }

    private static ProposalListing[] AppliedListings(ProposalRepository repository) =>
        repository.ListWithoutContent(new ProposalListFilter(ManifestStatus.Applied, IncludeArchived: true))
            .OrderBy(listing => listing.ProposalId.Value, StringComparer.Ordinal).ToArray();

    internal static DecisionEvidenceRow[] ReadEvidenceRows(MotifDatabase database, out int appliedCount)
    {
        var repository = new ProposalRepository(database);
        var applied = AppliedListings(repository);
        appliedCount = applied.Length;
        return applied.SelectMany(listing => RowsOf(repository, listing)).ToArray();
    }

    private static IEnumerable<DecisionEvidenceRow> RowsOf(ProposalRepository repository, ProposalListing listing)
    {
        var (_, envelope) = repository.GetFinalized(listing.ProposalId);
        foreach (var operation in envelope.Operations)
        {
            var evidence = DecisionEvidence.FromProvenance(operation.Provenance);
            var subject = operation.Target?.Value ?? operation.EntityId?.Value;
            if (evidence is null || subject is null) continue;
            DecisionEvidenceAgent? agent = null;
            if (operation.Kind == SIL.Motif.Runner.Operations.WfiAnalysisAgentEvaluationOperationKinds.Add &&
                operation.After is { } after)
            {
                var payload = SIL.Motif.Runner.Operations.AgentEvaluationPayload.Parse(after);
                agent = new DecisionEvidenceAgent(payload.AgentId.Value, payload.Name, payload.Version,
                    payload.Approves ? "approves" : "disapproves");
            }
            yield return new DecisionEvidenceRow(listing.ProposalId.Value, listing.Label, operation.OperationId.Value,
                operation.Kind, subject, evidence.Tier, evidence.Sources, evidence.Detail, evidence.StagedBy, agent);
        }
    }
}
