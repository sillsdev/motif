using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;

namespace SIL.Motif.Commands.Review;

/// <summary>
/// Bounds the signature rows a read of a saved Trial returns. The saved report keeps every row, so a gate can always
/// reach the whole multiset; a read returns one page filled to a byte bound and the rest are reached with <c>--page</c>.
/// Nothing is refused for its size.
/// </summary>
public static class TrialRetirementPaging
{
    /// <summary>The serialized size at which a page of signature rows is full. A single larger row is still returned whole.</summary>
    public const int PageBytes = 16 * 1024;

    /// <summary>Returns the requested page of signature rows, or a registered refusal when the page does not exist.</summary>
    public static CommandOutcome<TrialReviewResult> Apply(CommandOutcome<TrialReviewResult> read, int? requested)
    {
        if (!read.Succeeded || read.Value is not AvailableTrialResult { Report.Losses.RetirementAttestation: { } attestation } result)
            return read;
        var losses = result.Report.Losses!;
        var assertionRows = losses.NewlyLostReadings.Where(row => row.Signature is null).ToArray();
        var pages = new List<List<TrialLostReading>>();
        var size = 0;
        foreach (var row in losses.NewlyLostReadings.Where(row => row.Signature is not null))
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(row, ReviewJson.Options).Length;
            if (pages.Count == 0 || size + bytes > PageBytes && pages[^1].Count > 0) { pages.Add([]); size = 0; }
            pages[^1].Add(row);
            size += bytes;
        }
        var pageCount = Math.Max(1, pages.Count);
        var page = requested ?? 1;
        if (page > pageCount)
            return CommandOutcome<TrialReviewResult>.Refused(new Refusal(RefusalCodes.DifferenceInvalidPage,
                FailureReason.InvalidArgument, "The retirement rows have " + pageCount + " page(s); page " + page + " does not exist."));
        var rows = assertionRows.Concat(pages.Count == 0 ? [] : pages[page - 1])
            .OrderBy(row => row.Word, StringComparer.Ordinal).ThenBy(row => row.ReadingIdentity, StringComparer.Ordinal)
            .ThenBy(row => row.CaseKey, StringComparer.Ordinal).ToArray();
        var projected = losses with
        {
            NewlyLostReadings = rows,
            RetirementAttestation = attestation with { Page = page, PageCount = pageCount },
        };
        return CommandOutcome<TrialReviewResult>.Success(result with { Report = result.Report with { Losses = projected } });
    }
}
