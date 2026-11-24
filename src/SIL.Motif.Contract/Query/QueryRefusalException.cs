using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Contract.Query;

/// <summary>A query was refused with a typed code; the commands that run a query turn it into that refusal unchanged.</summary>
/// <param name="refusal">The refusal the caller reports.</param>
public sealed class QueryRefusalException(Refusal refusal) : ArgumentException(refusal.Message)
{
    /// <summary>The refusal to report.</summary>
    public Refusal Refusal { get; } = refusal;

    internal static QueryRefusalException OpTypeMismatch(string field, string op, string columnType) => new(new Refusal(
        RefusalCodes.QueryOpTypeMismatch, FailureReason.InvalidArgument,
        $"The {op} op does not fit {field}, which is a {columnType} column.",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["field"] = field, ["op"] = op, ["columnType"] = columnType }));

    internal static QueryRefusalException NotYetAvailable(string kind) => new(new Refusal(
        RefusalCodes.QueryNotYetAvailable, FailureReason.Refused,
        $"The {kind} predicate is not yet available; this release runs and, or, not and field predicates.",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["kind"] = kind }));

    internal static QueryRefusalException DocumentInvalid(string message, string? path = null) => new(new Refusal(
        RefusalCodes.QueryDocumentInvalid, FailureReason.InvalidArgument, message,
        path is null ? null : new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = path }));
}
