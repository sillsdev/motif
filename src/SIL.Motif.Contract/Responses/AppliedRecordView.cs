namespace SIL.Motif.Contract.Responses;

/// <summary>One field an Apply changed, as the Apply read it back from the project.</summary>
/// <param name="Target">The portable identity of the entity that holds the field.</param>
/// <param name="Field">The canonical field name, such as <c>lexical/sense/gloss</c>.</param>
/// <param name="Before">The value before the Apply, as alternatives keyed by writing-system tag; empty when it held nothing.</param>
/// <param name="After">The value the Apply left, in the same form.</param>
public sealed record AppliedEffectView(
    string Target, string Field, IReadOnlyDictionary<string, string> Before, IReadOnlyDictionary<string, string> After);

/// <summary>The counts of one saved Difference, frozen when the Proposal was decided.</summary>
/// <param name="DifferenceId">The Difference's identity.</param>
/// <param name="TextId">The Text it compared.</param>
/// <param name="TextTitle">The Text's title.</param>
/// <param name="Words">How many distinct words it compared.</param>
/// <param name="Occurrences">How many Text occurrences of those words.</param>
/// <param name="ImprovedWords">How many words came out better after the change.</param>
/// <param name="CouldBeBadWords">How many words could be worse after the change.</param>
/// <param name="CreatedUtc">When it was made.</param>
public sealed record AppliedEvidenceView(
    string DifferenceId, string TextId, string TextTitle, int Words, int Occurrences, int ImprovedWords,
    int CouldBeBadWords, string CreatedUtc);

/// <summary>What Apply recorded for a Proposal, so the permanent record shows what changed and what was known.</summary>
/// <param name="AppliedUtc">When the Apply was recorded in the project's applied-change log.</param>
/// <param name="Mode">How it was applied: <c>person</c> or <c>autonomous</c>.</param>
/// <param name="Message">The message recorded with the Apply.</param>
/// <param name="User">Who applied it.</param>
/// <param name="IntentDigest">The digest of the revision that was applied.</param>
/// <param name="Effects">Every field change, before and after, in the order the Apply made them.</param>
/// <param name="Evidence">The summary of each Difference saved for the Proposal; the word lists were dropped at the decision.</param>
public sealed record AppliedRecordView(
    string AppliedUtc, string Mode, string Message, string User, string IntentDigest,
    IReadOnlyList<AppliedEffectView> Effects, IReadOnlyList<AppliedEvidenceView> Evidence);
