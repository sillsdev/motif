using SIL.Motif.Projection;
using System;
using System.Collections.Generic;
using System.Linq;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Projection.Store;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

public static partial class ProposalCommands
{
    /// <summary>
    /// Lists every operation of every Applied Proposal with the source it cites, read from Motif's store; the
    /// FieldWorks project is not opened.
    /// </summary>
    public static CommandOutcome<CitationTraceResponse> Citations(CitationsRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (database, _) =>
        {
            try
            {
                var repository = new ProposalRepository(database);
                var rows = new List<CitedOperationRow>();
                var applied = repository.List(new ProposalListFilter(ManifestStatus.Applied, IncludeArchived: true))
                    .OrderBy(record => record.ProposalId.Value, StringComparer.Ordinal).ToArray();
                foreach (var record in applied)
                {
                    var (_, envelope) = repository.GetFinalized(record.ProposalId);
                    rows.AddRange(envelope.Operations.Select(operation => new CitedOperationRow(
                        record.ProposalId.Value, record.Label, operation.OperationId.Value, operation.Kind,
                        operation.Target?.Value, operation.EntityId?.Value, CitationView.Of(operation))));
                }
                var cited = rows.Count(row => row.Citation is not null);
                var shown = request.UncitedOnly ? rows.Where(row => row.Citation is null).ToArray() : rows.ToArray();
                return CommandOutcome<CitationTraceResponse>.Success(
                    new CitationTraceResponse(applied.Length, rows.Count, cited, shown));
            }
            catch (Exception exception) when (exception is not ProjectWritingSystemsUnavailableException)
            {
                return CommandOutcome<CitationTraceResponse>.Refused(ProposalLoadRefusal(exception));
            }
        });
}
