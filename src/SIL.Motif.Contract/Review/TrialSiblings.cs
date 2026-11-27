using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Review;

/// <summary>
/// The sibling words a Trial measured beside its own list, and how they fared: corpus words that share a form the
/// Proposal touches, which a short gold list can miss. The Trial's expectation state already judges them like any
/// measured word; this block says which words they were and what became of them, for diagnosis.
/// </summary>
/// <param name="Pattern">The pattern the candidates matched, or <c>per-form</c> when each touched form drew its own group.</param>
/// <param name="Seed">The seed of the shuffle, derived from the Proposal revision and the touched forms.</param>
/// <param name="Max">The cap on the sample.</param>
/// <param name="TouchedForms">The forms the sample was drawn for, in order.</param>
/// <param name="CorpusTypesSha256">The digest of the corpus-types file the candidates came from.</param>
/// <param name="Candidates">Corpus types that matched and were not already in the word list.</param>
/// <param name="ParsedCandidates">Those the corpus-types file marks as parsed.</param>
/// <param name="Sampled">Siblings added to the Trial's word list.</param>
/// <param name="Measured">Sampled siblings the Trial measured on both sides.</param>
/// <param name="Lost">Sampled siblings that parsed before and no longer parse.</param>
/// <param name="LostSample">Up to twenty of them, ordered by spelling.</param>
/// <param name="Doubled">Sampled siblings that kept every old Reading and gained more.</param>
/// <param name="DoubledSample">Up to twenty of them, ordered by spelling.</param>
/// <param name="Warning">What the sample could not use, such as existing-entry forms when the project had no Baseline; absent when nothing was dropped.</param>
public sealed record TrialSiblings(string Pattern, long Seed, int Max, IReadOnlyList<string> TouchedForms,
    string CorpusTypesSha256, int Candidates, int ParsedCandidates, int Sampled, long Measured, long Lost,
    IReadOnlyList<string> LostSample, long Doubled, IReadOnlyList<string> DoubledSample,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Warning = null)
{
    /// <summary>How many forms of referring entries were drawn on; absent when none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ReferrerForms { get; init; }

    /// <summary>The edited grammar objects that entries use; absent when none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TrialReferrer>? Referrers { get; init; }

    /// <summary>The forms left out because they would select the corpus at large, or kept only as the sole form of an object, with why; absent when none.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<TrialSkippedForm>? SkippedReferrerForms { get; init; }
}

/// <summary>A form that was not used, or was kept only on a cap, when selecting siblings.</summary>
/// <param name="Form">The form as written, with its affix mark.</param>
/// <param name="Reason">Why: shorter than two characters, or matching over 5% of the corpus types; or <c>kept: only form</c> when it is the only form of an edited object and was used anyway, on a quarter of the cap when over 5%.</param>
/// <param name="Kind"><c>touched</c> for a form the Proposal writes or an edited entry holds, <c>referrer</c> for a form of an entry that uses an edited grammar object.</param>
public sealed record TrialSkippedForm(string Form, string Reason, string Kind = "referrer");

/// <summary>A grammar object a Proposal edits and the entries that use it, whose forms the siblings are drawn from.</summary>
/// <param name="Target">The object's canonical id.</param>
/// <param name="Class">The object's LCM class, such as <c>MoInflClass</c>.</param>
/// <param name="Entries">How many lexical entries use it (an inflection class counts its subclasses; a template, its slots).</param>
public sealed record TrialReferrer(string Target, string Class, int Entries);
