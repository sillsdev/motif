using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>The most words one matrix cell, category or limitation lists; the rest are counted and not listed.</summary>
public static class ProposalDifferenceListing
{
    /// <summary>
    /// The most words kept for each matrix cell, category and limitation, highest place count first and spelling as the
    /// tie-break. It bounds what a Difference stores however many words a Text has.
    /// </summary>
    public const int MostWordsPerGroup = 100;

    /// <summary>
    /// The most places stored for one listed word, in reading order (line, then position in the line). A word with more
    /// places is counted in full and lists only these.
    /// </summary>
    public const int MostPlacesPerWord = 10;
}

/// <summary>How many distinct words and Text occurrences a category holds.</summary>
/// <remarks>
/// <c>Listed</c> is how many of those words the Difference lists; null means all of them, as a summary saved before
/// the bound listed every word.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalDifferenceBucketCount(
    [property: JsonRequired]
    [property: JsonConverter(typeof(StrictStringEnumConverter<ProposalDifferenceBucket>))]
    ProposalDifferenceBucket Bucket,
    [property: JsonRequired] int Words,
    [property: JsonRequired] int Occurrences,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Listed = null)
{
    /// <summary>How many of the words the Difference lists.</summary>
    [JsonIgnore]
    public int WordsListed => Listed ?? Words;
}

/// <summary>How many words moved from one approval state to another.</summary>
/// <remarks>
/// <c>Listed</c> is how many of those words the Difference lists; null means all of them, as a summary saved before
/// the bound listed every word.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalDifferenceMatrixCell(
    [property: JsonRequired] string BeforeState,
    [property: JsonRequired] string AfterState,
    [property: JsonRequired] int Words,
    [property: JsonRequired] int Occurrences,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Listed = null)
{
    /// <summary>How many of the words the Difference lists.</summary>
    [JsonIgnore]
    public int WordsListed => Listed ?? Words;
}

/// <summary>How many words, and Text occurrences of them, a search limit or refusal touched.</summary>
/// <remarks>
/// <c>Listed</c> is how many of those words the Difference lists; null means all of them, as a summary saved before
/// the bound listed every word.
/// </remarks>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ProposalDifferenceLimitationCount(
    [property: JsonRequired]
    [property: JsonConverter(typeof(StrictStringEnumConverter<HumanAgreementLimitation>))]
    HumanAgreementLimitation Limitation,
    [property: JsonRequired] int Words,
    [property: JsonRequired] int Occurrences,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Listed = null)
{
    /// <summary>How many of the words the Difference lists.</summary>
    [JsonIgnore]
    public int WordsListed => Listed ?? Words;
}

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
    /// <summary>The grammar staging contract, or null for older evidence.</summary>
    public GrammarExportFormat? GrammarExportFormat { get; init; }

    void IJsonOnDeserialized.OnDeserialized() =>
        ClosedShape.RequireNonNull(nameof(ProposalDifferenceSidePin), (nameof(GrammarSourceSha256), GrammarSourceSha256),
            (nameof(SelectionName), SelectionName), (nameof(SelectionSha256), SelectionSha256),
            (nameof(TokeniserName), TokeniserName), (nameof(TokeniserVersion), TokeniserVersion));
}

/// <summary>The totals of one saved Difference and the decisions it was read against.</summary>
/// <remarks>
/// <see cref="Reference"/> says whether those decisions were a person's (<c>human</c>) or a named non-human agent's
/// (<c>source-validated</c>); the two are never merged.
/// The counts cover every compared word, but only the words that changed or fall in a category are listed, at most
/// <see cref="ProposalDifferenceListing.MostWordsPerGroup"/> for each matrix cell and category; <see cref="WordsListed"/>
/// says how many are, and null means every word is (a summary saved before the bound).
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
    string? DecisionsDigest = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? WordsListed = null) : IJsonOnDeserialized
{
    /// <summary>How many compared words the Difference lists; every word when it was saved before the bound.</summary>
    [JsonIgnore]
    public int ListedWordCount => WordsListed ?? Words;

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
/// <param name="Total">How many places the word has in the Text, whether or not they are all listed.</param>
/// <param name="NextOffset">The offset of the next page, or null at the end of the places that are stored.</param>
/// <param name="Listed">How many of the places are stored and can be paged; null means all of them, as before the bound.</param>
public sealed record ProposalDifferenceOccurrencePage(
    string Word, IReadOnlyList<ProposalDifferenceOccurrencePlace> Places, int Total, int? NextOffset,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? Listed = null)
{
    /// <summary>How many places can be paged; the rest of <see cref="Total"/> are counted but not listed.</summary>
    [JsonIgnore]
    public int PlacesListed => Listed ?? Total;
}

/// <summary>
/// One of a Proposal's saved Differences, named by the Text and reference it belongs to; a Proposal keeps one per
/// Text and reference, so several sit side by side.
/// </summary>
/// <param name="DifferenceId">The id that reads it.</param>
/// <param name="TextId">The GUID of the Text it compares.</param>
/// <param name="TextTitle">The Text's title when it was made.</param>
/// <param name="Reference"><c>human</c> or <c>source-validated</c>.</param>
/// <param name="CreatedUtc">When it was made.</param>
/// <param name="WordDetailKept">False once its word detail was dropped and only the summary is left.</param>
public sealed record ProposalDifferenceSaved(
    string DifferenceId, string TextId, string TextTitle, string Reference, string CreatedUtc, bool WordDetailKept);

/// <summary>
/// A saved Difference whose stored summary could not be read, named from the columns that stay readable when the
/// summary does not, so the one that is damaged can be told from the others and made again.
/// </summary>
/// <param name="DifferenceId">The id of the damaged Difference.</param>
/// <param name="TextId">The GUID of the Text it compared.</param>
/// <param name="Reference"><c>human</c> or <c>source-validated</c>.</param>
/// <param name="CreatedUtc">When it was made.</param>
public sealed record ProposalDifferenceDamaged(string DifferenceId, string TextId, string Reference, string CreatedUtc);

/// <summary>Carries the unreadable Differences through a refusal's facts, so a window can name them as a response does.</summary>
public static class ProposalDifferenceDamagedFacts
{
    private const string CountKey = "damaged-count";

    /// <summary>The facts a refusal carries for these unreadable Differences.</summary>
    public static IReadOnlyDictionary<string, string> ToFacts(string proposalId, IReadOnlyList<ProposalDifferenceDamaged> damaged)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["proposalId"] = proposalId,
            [CountKey] = damaged.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
        for (var index = 0; index < damaged.Count; index++)
        {
            facts[$"damaged.{index}.id"] = damaged[index].DifferenceId;
            facts[$"damaged.{index}.text"] = damaged[index].TextId;
            facts[$"damaged.{index}.reference"] = damaged[index].Reference;
            facts[$"damaged.{index}.created"] = damaged[index].CreatedUtc;
        }
        return facts;
    }

    /// <summary>The unreadable Differences a refusal's facts name; none when the facts name none.</summary>
    public static IReadOnlyList<ProposalDifferenceDamaged> FromFacts(IReadOnlyDictionary<string, string> facts)
    {
        if (!facts.TryGetValue(CountKey, out var text) ||
            !int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count))
            return [];
        var damaged = new List<ProposalDifferenceDamaged>();
        for (var index = 0; index < count; index++)
            if (facts.TryGetValue($"damaged.{index}.id", out var id) && facts.TryGetValue($"damaged.{index}.text", out var textId) &&
                facts.TryGetValue($"damaged.{index}.reference", out var reference) &&
                facts.TryGetValue($"damaged.{index}.created", out var created))
                damaged.Add(new ProposalDifferenceDamaged(id, textId, reference, created));
        return damaged;
    }
}

/// <summary>A saved Difference's summary and one page of its words.</summary>
/// <param name="Summary">The totals, matrix and category counts, always first.</param>
/// <param name="Words">The requested page of matching words, ordered by word.</param>
/// <param name="MatchingWords">How many words match the filter, before paging.</param>
/// <param name="NextOffset">The offset of the next page, or null at the end.</param>
/// <param name="WordDetailKept">
/// False once the Proposal has been decided or deferred: the summary stays, the word detail is gone, and
/// <paramref name="Words"/> is empty. A deferred Proposal can make the Difference again to see its words.
/// </param>
/// <param name="Occurrences">
/// The requested word's Text places, only when a word was asked for and the word detail is kept; otherwise null.
/// </param>
/// <param name="UnreadableEvidence">
/// For a source-validated Difference, the Applied Proposals that could not be read just now; their decisions are
/// not counted or shown. It is read each time and never stored.
/// </param>
/// <param name="Saved">
/// Every Difference the Proposal has saved, newest first and bounded to the first 50, each named by its Text and
/// reference, so the one shown can be told from the others. Null when the response was not read for a Proposal.
/// </param>
/// <param name="SavedTotal">How many Differences the Proposal has saved, before the bound on <paramref name="Saved"/>.</param>
/// <param name="SavedUnreadable">
/// How many of the listed Differences could not be read and are left out of <paramref name="Saved"/>; the one shown
/// is never among them.
/// </param>
/// <param name="SavedDamaged">
/// Those unreadable Differences by Text and reference, so a person can tell which one to make again; null when the
/// response was not read for a Proposal. Without an id, a read shows the newest Difference that can be read.
/// </param>
/// <param name="WordsListed">
/// How many words the Difference holds as rows right now, which is every word the summary says it lists while the
/// word detail is kept and none once it is gone; null when the rows could not be read.
/// </param>
/// <param name="WordDetailUnreadable">
/// True when the summary is readable but the word rows are damaged: <paramref name="Words"/> is empty, the word detail
/// is not kept, and making the Difference again replaces it.
/// </param>
public sealed record ProposalDifferenceResponse(
    ProposalDifferenceSummary Summary, IReadOnlyList<ProposalDifferenceWord> Words, int MatchingWords,
    int? NextOffset, bool WordDetailKept, ProposalDifferenceOccurrencePage? Occurrences = null,
    IReadOnlyList<ProposalDifferenceUnreadableEvidence>? UnreadableEvidence = null,
    IReadOnlyList<ProposalDifferenceSaved>? Saved = null, int SavedTotal = 0, int SavedUnreadable = 0,
    IReadOnlyList<ProposalDifferenceDamaged>? SavedDamaged = null, int? WordsListed = null,
    bool WordDetailUnreadable = false);
