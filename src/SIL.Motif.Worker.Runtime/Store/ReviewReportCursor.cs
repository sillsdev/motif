using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Review;

namespace SIL.Motif.Worker.Store;

/// <summary>A continuation does not belong to the requested report, section, or filters.</summary>
public sealed class ReviewCursorInvalidException() : ArgumentException("review.cursor-invalid");

/// <summary>A report remains readable but its indexed detail is unavailable.</summary>
public sealed class ReviewDetailUnavailableException : InvalidOperationException
{
    /// <summary>Creates an unavailable-detail refusal and its retained summary target.</summary>
    public ReviewDetailUnavailableException(string reason = "not-retained", string? summaryUri = null)
        : base("review.detail-unavailable") { Reason = reason; SummaryUri = summaryUri; }

    /// <summary>The reason the indexed detail cannot be read.</summary>
    public string Reason { get; }

    /// <summary>The saved report summary, when its project and report are known.</summary>
    public string? SummaryUri { get; }
}

internal sealed record ReviewCursorEnvelope(int Version, string Project, string Report, string Section,
    string FilterDigest, int SortVersion, string Last, string? ChildKind, string? Parent);

internal static class ReviewReportCursor
{
    internal static string FilterDigest(ReviewPageRequest request)
    {
        var value = JsonSerializer.Serialize(new { request.Set, request.Outcome, request.Flag, request.Filter }, ReviewJson.Options);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }

    internal static string Encode(ReviewCursorEnvelope cursor) =>
        Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static ReviewCursorEnvelope Decode(string value, string project, string report, string section,
        string filterDigest, string? childKind = null, string? parent = null)
    {
        try
        {
            if (value.Length > 8192) throw new ReviewCursorInvalidException();
            var text = value.Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(text.PadRight((text.Length + 3) / 4 * 4, '='));
            var cursor = JsonSerializer.Deserialize<ReviewCursorEnvelope>(bytes) ?? throw new ReviewCursorInvalidException();
            if (cursor.Version != 1 || cursor.SortVersion != 1 || cursor.Project != project || cursor.Report != report ||
                cursor.Section != section || cursor.FilterDigest != filterDigest || cursor.ChildKind != childKind ||
                cursor.Parent != parent || string.IsNullOrEmpty(cursor.Last)) throw new ReviewCursorInvalidException();
            return cursor;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or ArgumentOutOfRangeException)
        {
            throw new ReviewCursorInvalidException();
        }
    }
}
