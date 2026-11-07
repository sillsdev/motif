namespace SIL.Motif.Contract.Review;

/// <summary>A stable identity for one explicitly produced review report.</summary>
public readonly record struct ReviewReportId
{
    /// <summary>Creates an opaque report identity.</summary>
    public ReviewReportId(string value) => Value = !string.IsNullOrWhiteSpace(value)
        ? value : throw new ArgumentException("A report id is required.", nameof(value));

    /// <summary>The report identity as stored.</summary>
    public string Value { get; }
}

/// <summary>The stored report to read and the requested disclosure and selectors.</summary>
public sealed record ReviewReadRequest(
    string ProjectKey, string ProposalId, string? JobId = null, string? ReportId = null,
    DetailLevel? Detail = null, string? Section = null, string? Set = null, string? Outcome = null,
    ReviewFlagId? Flag = null, string? Filter = null, string? Word = null, string? Case = null,
    bool Trace = false, Side? Side = null, string? Cursor = null, int? Limit = null)
{
    /// <summary>Returns an argument problem before a stored read or diagnostic can do work.</summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(ProjectKey) || string.IsNullOrWhiteSpace(ProposalId)) return "project and proposal are required";
        if (new[] { JobId, ReportId, Section, Set, Outcome, Filter, Word, Case, Cursor }
            .Any(value => value is not null && string.IsNullOrWhiteSpace(value))) return "selectors cannot be blank";
        if ((Detail.HasValue && !Enum.IsDefined(Detail.Value)) || (Flag.HasValue && !Enum.IsDefined(Flag.Value)) ||
            (Side.HasValue && !Enum.IsDefined(Side.Value))) return "unknown enum value";
        if (Section is not null and not ("wordforms" or "sampled-tokens" or "paradigm-cells" or "controls" or
            "negative-controls" or "parser-work" or "warnings" or "parsimony" or "incomplete" or "effects" or "evidence"))
            return "unknown section";
        if (JobId is not null && ReportId is not null) return "job and report are exclusive";
        if (Word is not null && Case is not null) return "word and case are exclusive";
        if (Trace && Word is null && Case is null) return "trace requires a word or case";
        if ((Word is not null || Case is not null || Trace) && Detail is not null and not DetailLevel.Full)
            return "word, case and trace require full detail";
        if ((Cursor is not null || Limit is not null || Filter is not null || Outcome is not null || Flag is not null) &&
            EffectiveDetail != DetailLevel.Full) return "cursor and list filters require full detail";
        if (Limit is < 1 or > 200) return "limit must be between 1 and 200";
        if (Filter is not null and not ("all" or "changed" or "unchanged" or "review" or "incomplete"))
            return "unknown filter";
        return null;
    }

    /// <summary>The disclosure selected by explicit detail or the request's selectors.</summary>
    public DetailLevel EffectiveDetail => Detail ?? (Word is not null || Case is not null || Trace
        ? DetailLevel.Full : Section is not null ? DetailLevel.Failures : DetailLevel.Summary);

    /// <summary>The trace side defaults to the candidate when no side was selected.</summary>
    public Side? EffectiveSide => Trace && Side is null ? global::SIL.Motif.Contract.Review.Side.After : Side;
}

/// <summary>An explicit production of a report from a Trial's retained evidence.</summary>
public sealed record ReviewProduceRequest(string ProjectKey, string ProposalId, string? JobId, string? ReportId)
{
    /// <summary>Returns a selection problem before production starts.</summary>
    public string? Validate() => string.IsNullOrWhiteSpace(ProjectKey) || string.IsNullOrWhiteSpace(ProposalId)
        ? "project and proposal are required" : JobId is not null && ReportId is not null
            ? "job and report are exclusive" : null;
}

/// <summary>One indexed page in an immutable report.</summary>
public sealed record ReviewPageRequest(
    string ProjectKey, ReviewReportId ReportId, string Section, string? Set = null, string? Outcome = null,
    ReviewFlagId? Flag = null, string? Filter = null, string? Cursor = null, int Limit = 50)
{
    /// <summary>Returns a selection problem before a page is read.</summary>
    public string? Validate() => string.IsNullOrWhiteSpace(ProjectKey) || string.IsNullOrWhiteSpace(ReportId.Value) ||
        string.IsNullOrWhiteSpace(Section) ? "project, report and section are required" : Limit is < 1 or > 200
        ? "limit must be between 1 and 200" : Flag.HasValue && !Enum.IsDefined(Flag.Value)
        ? "unknown flag" : Filter is not null and not ("all" or "changed" or "unchanged" or "review" or "incomplete")
        ? "unknown filter" : null;
}

/// <summary>One explicit diagnostic trace bound to a report and case.</summary>
public sealed record ReviewTraceRequest(string ProjectKey, ReviewReportId ReportId, string CaseKey, Side Side,
    string? Cursor = null, int Limit = 50)
{
    /// <summary>Returns a selection problem before a trace is read or produced.</summary>
    public string? Validate() => string.IsNullOrWhiteSpace(ProjectKey) || string.IsNullOrWhiteSpace(ReportId.Value) ||
        string.IsNullOrWhiteSpace(CaseKey) ? "project, report and case are required" : !Enum.IsDefined(Side)
        ? "unknown side" : Limit is < 1 or > 200 ? "limit must be between 1 and 200" : null;
}
