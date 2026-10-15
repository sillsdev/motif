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

/// <summary>The totals of one saved Difference and the human decisions it was read against.</summary>
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
    [property: JsonRequired] string CreatedUtc) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(ProposalDifferenceSummary), (nameof(DifferenceId), DifferenceId),
            (nameof(ProposalId), ProposalId), (nameof(TrialJobId), TrialJobId),
            (nameof(BeforeAssessmentId), BeforeAssessmentId), (nameof(AfterAssessmentId), AfterAssessmentId),
            (nameof(TextId), TextId), (nameof(TextTitle), TextTitle), (nameof(BaselineToken), BaselineToken),
            (nameof(Policy), Policy), (nameof(Buckets), Buckets), (nameof(Matrix), Matrix),
            (nameof(CreatedUtc), CreatedUtc));
}

/// <summary>One word of a saved Difference with its states, categories and Text occurrence count.</summary>
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
    [property: JsonRequired] ProposalDifferenceCaseFacts Facts) : IJsonOnDeserialized
{
    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(ProposalDifferenceWord), (nameof(Word), Word),
            (nameof(BeforeState), BeforeState), (nameof(AfterState), AfterState), (nameof(Changes), Changes),
            (nameof(Buckets), Buckets), (nameof(Facts), Facts));
}

/// <summary>A saved Difference's summary and one page of its words.</summary>
/// <param name="Summary">The totals, matrix and category counts, always first.</param>
/// <param name="Words">The requested page of matching words, ordered by word.</param>
/// <param name="MatchingWords">How many words match the filter, before paging.</param>
/// <param name="NextOffset">The offset of the next page, or null at the end.</param>
public sealed record ProposalDifferenceResponse(
    ProposalDifferenceSummary Summary, IReadOnlyList<ProposalDifferenceWord> Words, int MatchingWords,
    int? NextOffset);
