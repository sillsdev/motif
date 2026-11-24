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

    /// <summary>A <c>phon</c> or <c>env</c> predicate ran with no grammar; corpus mode names <c>--grammar</c>.</summary>
    public static QueryRefusalException PhonNeedsGrammar() => new(new Refusal(
        RefusalCodes.QueryPhonNeedsGrammar, FailureReason.InvalidArgument,
        "phon and env segment words and name natural classes with a grammar, and this query has none. " +
        "Pass --grammar <path> (the FieldWorks project or snapshot PanGloss loads), or run the query against a project.",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["flag"] = "--grammar" }));

    /// <summary>The installed PanGloss cannot match patterns.</summary>
    public static QueryRefusalException PhonUnavailable(string needed, string detail) => new(new Refusal(
        RefusalCodes.QueryPhonUnavailable, FailureReason.Refused,
        $"phon and env need {needed}; the installed PanGloss cannot match patterns: {detail}.",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["needs"] = needed, ["detail"] = detail }));

    /// <summary>PanGloss refused a pattern with a typed code; its own fields ride along as facts.</summary>
    public static QueryRefusalException PhonPatternRefused(string message, IReadOnlyDictionary<string, string> facts) => new(new Refusal(
        RefusalCodes.QueryPhonPatternRefused, FailureReason.InvalidArgument, message, facts));

    /// <summary>The resident PanGloss failed or sent something unreadable.</summary>
    public static QueryRefusalException PhonFailed(string message) => new(new Refusal(
        RefusalCodes.QueryPhonFailed, FailureReason.Refused, message));
}
