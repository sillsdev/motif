using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>How many distinct words and Text occurrences a category holds.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalDifferenceBucketCount(
    [property: JsonRequired]
    [property: JsonConverter(typeof(StrictStringEnumConverter<ProposalDifferenceBucket>))]
    ProposalDifferenceBucket Bucket,
    [property: JsonRequired] int Words,
    [property: JsonRequired] int Occurrences);

/// <summary>How many words moved from one approval state to another.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalDifferenceMatrixCell(
    [property: JsonRequired] string BeforeState,
    [property: JsonRequired] string AfterState,
    [property: JsonRequired] int Words,
    [property: JsonRequired] int Occurrences);

/// <summary>How many words, and Text occurrences of them, a search limit or refusal touched.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalDifferenceLimitationCount(
    [property: JsonRequired]
    [property: JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementLimitation>))]
    HumanAgreementLimitation Limitation,
    [property: JsonRequired] int Words,
    [property: JsonRequired] int Occurrences);

/// <summary>What one side of the comparison was measured with, copied by value from its Assessment.</summary>
/// <param name="ParserVersion">The version the parser reported, or null when the Assessment recorded none.</param>
/// <param name="ParserSha256">The hash of the parser executable, or null when the Assessment recorded none.</param>
/// <param name="GrammarSourceSha256">The hash of the grammar source that was parsed with.</param>
/// <param name="SelectionName">The name of the word Selection the Assessment measured.</param>
/// <param name="SelectionSha256">The hash of that Selection's words.</param>
/// <param name="TokeniserName">The tokeniser that produced the Selection's words.</param>
/// <param name="TokeniserVersion">The tokeniser's version.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalDifferenceSidePin(
    [property: JsonRequired] string? ParserVersion,
    [property: JsonRequired] string? ParserSha256,
    [property: JsonRequired] string GrammarSourceSha256,
    [property: JsonRequired] string SelectionName,
    [property: JsonRequired] string SelectionSha256,
    [property: JsonRequired] string TokeniserName,
    [property: JsonRequired] string TokeniserVersion) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(ProposalDifferenceSidePin), (nameof(GrammarSourceSha256), GrammarSourceSha256),
            (nameof(SelectionName), SelectionName), (nameof(SelectionSha256), SelectionSha256),
            (nameof(TokeniserName), TokeniserName), (nameof(TokeniserVersion), TokeniserVersion));
}

/// <summary>The totals of one saved Difference and the decisions it was read against.</summary>
/// <remarks>
/// <see cref="Reference"/> says whether those decisions were a person's (<c>human</c>) or a named non-human agent's
/// (<c>source-validated</c>); the two are never merged.
/// Everything the Difference needs from its Trial's Assessments is copied here by value, so it stays readable
/// after those Assessments are removed. It holds only bounded data: no list in it grows with the number of words.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalDifferenceSummary(
    [property: JsonRequired] string DifferenceId,
    [property: JsonRequired] string ProposalId,
    [property: JsonRequired] string TrialJobId,
    [property: JsonRequired] string BeforeAssessmentId,
    [property: JsonRequired] string AfterAssessmentId,
    [property: JsonRequired] string TextId,
    [property: JsonRequired] string TextTitle,
    [property: JsonRequired] string BaselineToken,
    [property: JsonRequired] ProposalDifferencePolicy Policy,
    [property: JsonRequired] int Words,
    [property: JsonRequired] int Occurrences,
    [property: JsonRequired] int UnboundOccurrences,
    [property: JsonRequired] int WordsWithApprovedDecisions,
    [property: JsonRequired] int CouldBeBadWords,
    [property: JsonRequired] int ImprovedWords,
    [property: JsonRequired] IReadOnlyList<ProposalDifferenceBucketCount> Buckets,
    [property: JsonRequired] IReadOnlyList<ProposalDifferenceMatrixCell> Matrix,
    [property: JsonRequired] string Assessor,
    [property: JsonRequired] string? ProposalIntentDigest,
    [property: JsonRequired] ProposalDifferenceSidePin Before,
    [property: JsonRequired] ProposalDifferenceSidePin After,
    [property: JsonRequired] IReadOnlyList<string> Warnings,
    [property: JsonRequired] int MeasuredWordsOutsideText,
    [property: JsonRequired] IReadOnlyList<ProposalDifferenceLimitationCount> Limitations,
    [property: JsonRequired] string CreatedUtc,
    string Reference = "human",
    string? DecisionsDigest = null) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(ProposalDifferenceSummary), (nameof(DifferenceId), DifferenceId),
            (nameof(ProposalId), ProposalId), (nameof(TrialJobId), TrialJobId),
            (nameof(BeforeAssessmentId), BeforeAssessmentId), (nameof(AfterAssessmentId), AfterAssessmentId),
            (nameof(TextId), TextId), (nameof(TextTitle), TextTitle), (nameof(BaselineToken), BaselineToken),
            (nameof(Policy), Policy), (nameof(Buckets), Buckets), (nameof(Matrix), Matrix),
            (nameof(Assessor), Assessor), (nameof(Before), Before), (nameof(After), After),
            (nameof(Warnings), Warnings), (nameof(Limitations), Limitations), (nameof(CreatedUtc), CreatedUtc),
            (nameof(Reference), Reference));
}

/// <summary>
/// Why a named non-human agent decided about one wordform or analysis a word of a source-validated Difference
/// leans on.
/// </summary>
/// <param name="Subject">The wordform or analysis the evidence is about.</param>
/// <param name="Level"><c>wordform</c> or <c>analysis</c>.</param>
/// <param name="Tier">The evidence tier, such as <c>source-approved</c>.</param>
/// <param name="Agent">The agent's name, when the record carried a decision.</param>
/// <param name="Opinion"><c>approves</c> or <c>disapproves</c>, when the record carried a decision.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalDifferenceWordEvidence(
    [property: JsonRequired] string Subject,
    [property: JsonRequired] string Level,
    [property: JsonRequired] string Tier,
    string? Agent = null,
    string? Opinion = null) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(ProposalDifferenceWordEvidence), (nameof(Subject), Subject),
            (nameof(Level), Level), (nameof(Tier), Tier));
}

/// <summary>An Applied Proposal whose staged decisions could not be read, so they are not counted or shown.</summary>
/// <param name="ProposalId">The Applied Proposal.</param>
/// <param name="Label">Its label, when it has one.</param>
/// <param name="Reason">Why it could not be read.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalDifferenceUnreadableEvidence(
    [property: JsonRequired] string ProposalId,
    string? Label,
    [property: JsonRequired] string Reason) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(ProposalDifferenceUnreadableEvidence), (nameof(ProposalId), ProposalId),
            (nameof(Reason), Reason));
}

/// <summary>One word of a saved Difference with its states, categories and Text occurrence count.</summary>
/// <remarks>
/// <see cref="Evidence"/> is read from the Applied Proposals when the page is read and is never stored, so it adds
/// nothing to the kept record.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalDifferenceWord(
    [property: JsonRequired] string Word,
    [property: JsonRequired] int Occurrences,
    [property: JsonRequired] string BeforeState,
    [property: JsonRequired] string AfterState,
    [property: JsonRequired]
    [property: JsonConverter(typeof(StrictStringEnumListConverter<ProposalDifferenceChange>))]
    IReadOnlyList<ProposalDifferenceChange> Changes,
    [property: JsonRequired]
    [property: JsonConverter(typeof(StrictStringEnumListConverter<ProposalDifferenceBucket>))]
    IReadOnlyList<ProposalDifferenceBucket> Buckets,
    [property: JsonRequired] ProposalDifferenceCaseFacts Facts,
    IReadOnlyList<ProposalDifferenceWordEvidence>? Evidence = null) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(ProposalDifferenceWord), (nameof(Word), Word),
            (nameof(BeforeState), BeforeState), (nameof(AfterState), AfterState), (nameof(Changes), Changes),
            (nameof(Buckets), Buckets), (nameof(Facts), Facts));
}

/// <summary>One place in a Text where a compared word occurs.</summary>
/// <param name="TextId">The GUID of the Text.</param>
/// <param name="SegmentId">The GUID of the Text's line (segment) holding the word.</param>
/// <param name="OccurrenceIndex">The word's position among the tokens of that line.</param>
/// <param name="LineNumber">The line's number in the Text, counted from one.</param>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalDifferenceOccurrencePlace(
    [property: JsonRequired] string TextId,
    [property: JsonRequired] string SegmentId,
    [property: JsonRequired] int OccurrenceIndex,
    [property: JsonRequired] int LineNumber) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(ProposalDifferenceOccurrencePlace), (nameof(TextId), TextId),
            (nameof(SegmentId), SegmentId));
}

/// <summary>One page of the places in the Text where a single word occurs.</summary>
/// <param name="Word">The word whose places are listed.</param>
/// <param name="Places">The requested page, in Text order (line, then position in the line).</param>
/// <param name="Total">How many places the word has, before paging.</param>
/// <param name="NextOffset">The offset of the next page, or null at the end.</param>
public sealed record ProposalDifferenceOccurrencePage(
    string Word, IReadOnlyList<ProposalDifferenceOccurrencePlace> Places, int Total, int? NextOffset);

/// <summary>A saved Difference's summary and one page of its words.</summary>
/// <param name="Summary">The totals, matrix and category counts, always first.</param>
/// <param name="Words">The requested page of matching words, ordered by word.</param>
/// <param name="MatchingWords">How many words match the filter, before paging.</param>
/// <param name="NextOffset">The offset of the next page, or null at the end.</param>
/// <param name="WordDetailKept">
/// False once the Proposal has been decided: the summary stays, the word detail is gone, and <paramref name="Words"/> is empty.
/// </param>
/// <param name="Occurrences">
/// The requested word's Text places, only when a word was asked for and the word detail is kept; otherwise null.
/// </param>
/// <param name="UnreadableEvidence">
/// For a source-validated Difference, the Applied Proposals that could not be read just now; their decisions are
/// not counted or shown. It is read each time and never stored.
/// </param>
public sealed record ProposalDifferenceResponse(
    ProposalDifferenceSummary Summary, IReadOnlyList<ProposalDifferenceWord> Words, int MatchingWords,
    int? NextOffset, bool WordDetailKept, ProposalDifferenceOccurrencePage? Occurrences = null,
    IReadOnlyList<ProposalDifferenceUnreadableEvidence>? UnreadableEvidence = null);
