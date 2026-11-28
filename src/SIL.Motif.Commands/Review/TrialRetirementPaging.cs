using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Review;

namespace SIL.Motif.Commands.Review;

/// <summary>
/// Bounds the signature rows a read of a saved Trial returns. The saved report keeps every row, so a gate can always
/// reach the whole multiset; a read returns rows up to the limit or the byte bound, whichever comes first, and the rest
/// are reached with <c>--cursor</c>. Nothing is refused for its size.
/// </summary>
public static class TrialRetirementPaging
{
    /// <summary>The signature rows a page holds when the caller gives no limit.</summary>
    public const int DefaultLimit = 50;

    /// <summary>The serialized size at which a page of signature rows is full. A single larger row is still returned whole.</summary>
    public const int PageBytes = 16 * 1024;

    /// <summary>Returns the page of signature rows <paramref name="cursor"/> and <paramref name="limit"/> name, or a registered refusal for a cursor this API did not issue.</summary>
    public static CommandOutcome<TrialReviewResult> Apply(CommandOutcome<TrialReviewResult> read, string? cursor = null, int? limit = null)
    {
        if (!read.Succeeded || read.Value is not AvailableTrialResult { Report.Losses.RetirementAttestation: { } attestation } result)
            return read;
        if (!ResultPaging.TryOffset(cursor, out var offset) || !ResultPaging.IsValid(offset, limit ?? DefaultLimit))
            return CommandOutcome<TrialReviewResult>.Refused(new Refusal(RefusalCodes.PagingInvalid, FailureReason.InvalidArgument,
                "Use the nextCursor a previous page returned and a limit from 1 to " + ResultPaging.MaxLimit + "."));
        var losses = result.Report.Losses!;
        var assertionRows = losses.NewlyLostReadings.Where(row => row.Signature is null).ToArray();
        var signed = losses.NewlyLostReadings.Where(row => row.Signature is not null).ToArray();
        var shown = new List<TrialLostReading>();
        var size = 0;
        foreach (var row in signed.Skip(offset).Take(limit ?? DefaultLimit))
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(row, ReviewJson.Options).Length;
            if (shown.Count > 0 && size + bytes > PageBytes) break;
            shown.Add(row);
            size += bytes;
        }
        var rows = assertionRows.Concat(shown)
            .OrderBy(row => row.Word, StringComparer.Ordinal).ThenBy(row => row.ReadingIdentity, StringComparer.Ordinal)
            .ThenBy(row => row.CaseKey, StringComparer.Ordinal).ToArray();
        var projected = losses with
        {
            NewlyLostReadings = rows,
            RetirementAttestation = attestation with { NextCursor = ResultPaging.NextCursor(offset, shown.Count, signed.Length) },
        };
        return CommandOutcome<TrialReviewResult>.Success(result with { Report = result.Report with { Losses = projected } });
    }
}
