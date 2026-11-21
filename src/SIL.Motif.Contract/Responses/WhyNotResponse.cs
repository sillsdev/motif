using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>The fixed bounds of a why-not response.</summary>
public static class WhyNotLimits
{
    /// <summary>The most candidates one call judges.</summary>
    public const int MaxCandidates = 16;

    /// <summary>The most morphs listed for one answer; the rest are counted, not listed.</summary>
    public const int MaxMorphs = 16;

    /// <summary>The most feature clashes listed for one rejection; the rest are counted.</summary>
    public const int MaxFeatures = 8;

    /// <summary>The longest sentence kept from the parser, in characters.</summary>
    public const int MaxSentence = 400;

    /// <summary>The most characters of each side of an environment's context kept.</summary>
    public const int MaxContext = 40;
}

/// <summary>A morph in an expected or derived analysis, named as the grammar names it.</summary>
/// <param name="Morpheme">The <c>grammar.xml</c> id of the lexical entry or affix rule.</param>
/// <param name="Allomorph">The <c>grammar.xml</c> allomorph id, when the parser reported one.</param>
/// <param name="EntryGuid">The lexical entry's FieldWorks guid, or null for a grammar with none.</param>
/// <param name="MsaGuid">The morphosyntactic analysis's FieldWorks guid, or null.</param>
/// <param name="FormGuid">The allomorph's FieldWorks MoForm guid, or null.</param>
public sealed record WhyNotMorph(
    string Morpheme, string? Allomorph, string? EntryGuid = null, string? MsaGuid = null, string? FormGuid = null);

/// <summary>One environment of an allomorph that failed, when several did.</summary>
/// <param name="Index">The environment's 0-based place in the allomorph's list, or null.</param>
/// <param name="FieldWorksGuid">The environment's FieldWorks guid, or null.</param>
/// <param name="Text">The authored environment text such as <c>/ _ #</c>, or null.</param>
public sealed record WhyNotEnvironmentChoice(int? Index, string? FieldWorksGuid, string? Text);

/// <summary>The grammar object a fix would touch, with its FieldWorks identity when the parser supplies one.</summary>
/// <param name="Type">The object's kind, such as <c>affix</c>, <c>allomorph</c> or <c>environment</c>.</param>
/// <param name="Id">The <c>grammar.xml</c> id, or null when the grammar gives the object none.</param>
/// <param name="FieldWorksGuid">The FieldWorks guid, or null when PanGloss has not supplied one yet.</param>
/// <param name="FieldWorksTarget">The guid, or the sentence <c>no FieldWorks target yet</c>, so a reader never guesses.</param>
public sealed record WhyNotTarget(string Type, string? Id, string? FieldWorksGuid, string FieldWorksTarget)
{
    /// <summary>What is said in place of a guid the parser did not supply.</summary>
    public const string NoFieldWorksTarget = "no FieldWorks target yet";
}

/// <summary>The environment an allomorph's context failed.</summary>
/// <param name="AllomorphId">The allomorph the environment belongs to.</param>
/// <param name="Index">The environment's 0-based place in that allomorph's list, or null when the parser did not know it.</param>
/// <param name="Left">The text left of the allomorph in the form the parser checked, shortened, or null.</param>
/// <param name="Right">The text right of the allomorph, shortened, or null.</param>
/// <param name="FieldWorksGuid">The environment's FieldWorks guid, or null when PanGloss has not supplied one yet.</param>
/// <param name="FieldWorksTarget">The guid, or the sentence <c>no FieldWorks target yet</c>.</param>
/// <param name="Text">The authored environment text, or null when PanGloss supplied none.</param>
/// <param name="Choices">Every failed environment when the allomorph's environments are any-of and several failed; empty otherwise.</param>
public sealed record WhyNotEnvironment(
    string AllomorphId, int? Index, string? Left, string? Right, string? FieldWorksGuid, string FieldWorksTarget,
    string? Text = null, IReadOnlyList<WhyNotEnvironmentChoice>? Choices = null);

/// <summary>One feature path that did not match.</summary>
/// <param name="Path">The feature path.</param>
/// <param name="Required">What the constraint required.</param>
/// <param name="Actual">What the word had.</param>
public sealed record WhyNotFeature(string Path, string Required, string Actual);

/// <summary>The first failing constraint of a rejected candidate.</summary>
/// <param name="Kind">The failure kind, such as <c>no_morph_match</c>.</param>
/// <param name="Plain">One English sentence from PanGloss, shortened to <see cref="WhyNotLimits.MaxSentence"/> characters.</param>
/// <param name="Position">The 0-based position in the candidate, or null when the failure is about the whole word.</param>
/// <param name="Provenance"><c>hc_reported</c> when HermitCrab raised it, <c>derived</c> when PanGloss inferred it.</param>
/// <param name="Morphs">The morphs involved, at most <see cref="WhyNotLimits.MaxMorphs"/>.</param>
/// <param name="MorphsOmitted">How many involved morphs the cap left out.</param>
/// <param name="Target">The grammar object a fix would touch, or null when no single object applies.</param>
/// <param name="Environment">The environment, for <c>environment_unmet</c> only.</param>
/// <param name="StemName">The stem name's id, for <c>stem_name</c> only.</param>
/// <param name="Features">The feature clashes, for <c>feature_clash</c> only, at most <see cref="WhyNotLimits.MaxFeatures"/>.</param>
/// <param name="FeaturesOmitted">How many feature clashes the cap left out.</param>
/// <param name="Alternatives">How many other derivations of the same morph sequence also fail, or null when none.</param>
/// <param name="Members">Every morph a co-occurrence prohibition names, primary first, at most <see cref="WhyNotLimits.MaxMorphs"/>; empty otherwise.</param>
/// <param name="MembersOmitted">How many prohibition members the cap left out.</param>
public sealed record WhyNotRejection(
    string Kind,
    string Plain,
    int? Position,
    string Provenance,
    IReadOnlyList<WhyNotMorph> Morphs,
    int MorphsOmitted,
    WhyNotTarget? Target,
    WhyNotEnvironment? Environment,
    string? StemName,
    IReadOnlyList<WhyNotFeature> Features,
    int FeaturesOmitted,
    int? Alternatives,
    IReadOnlyList<WhyNotMorph>? Members = null,
    int MembersOmitted = 0);

/// <summary>The derivation that parsed an accepted candidate.</summary>
/// <param name="Morphs">The word's morphs in surface order, at most <see cref="WhyNotLimits.MaxMorphs"/>.</param>
/// <param name="MorphsOmitted">How many morphs the cap left out.</param>
/// <param name="Category">The part of speech's <c>grammar.xml</c> id, or null.</param>
public sealed record WhyNotAcceptance(IReadOnlyList<WhyNotMorph> Morphs, int MorphsOmitted, string? Category);

/// <summary>The answer for one candidate.</summary>
/// <param name="Index">The candidate's 0-based place in the request.</param>
/// <param name="Candidate">The candidate as Motif sent it: <c>,</c> between positions, <c>|</c> between choices.</param>
/// <param name="Result"><c>accepted</c>, <c>rejected</c> or <c>unknown</c>.</param>
/// <param name="Acceptance">The derivation, when accepted.</param>
/// <param name="Rejection">The first failing constraint, when rejected.</param>
/// <param name="Stop">Why PanGloss could not answer, when unknown: a stable reason such as <c>work_cap</c>.</param>
/// <param name="StopNote">One sentence saying what was reached, when unknown.</param>
public sealed record WhyNotCandidateResult(
    int Index,
    string Candidate,
    string Result,
    WhyNotAcceptance? Acceptance,
    WhyNotRejection? Rejection,
    string? Stop,
    string? StopNote);

/// <summary>What the caps left out of a why-not response.</summary>
/// <param name="CandidatesShown">How many candidates are answered here.</param>
/// <param name="MorphsOmitted">Morphs across all answers that a cap left out.</param>
/// <param name="SentencesShortened">Parser sentences cut to <see cref="WhyNotLimits.MaxSentence"/> characters.</param>
public sealed record WhyNotBound(int CandidatesShown, int MorphsOmitted, int SentencesShortened);

/// <summary>
/// Why a word did not parse as the morphs a person expected, as PanGloss answers for each candidate: accepted,
/// rejected with the first failing constraint, or unknown with the reason it could not tell. Motif asks the parser
/// that parses and never infers a cause it did not report.
/// </summary>
/// <param name="Word">The word, exactly as given.</param>
/// <param name="TracedAgainst"><c>baseline</c>, or <c>proposal &lt;id&gt;</c> for that Proposal's latest Trial copy.</param>
/// <param name="Schema">The report schema PanGloss wrote, <c>pangloss.whynot.v1</c>.</param>
/// <param name="AnswerableKinds">The failure kinds the installed PanGloss declares it answers; every other failure is unknown.</param>
/// <param name="Candidates">One answer per candidate, in the order asked.</param>
/// <param name="Best">The index of the first accepted candidate, else the rejected one that got furthest, else null.</param>
/// <param name="Bound">What the caps left out.</param>
public sealed record WhyNotResponse(
    string Word,
    string TracedAgainst,
    string Schema,
    IReadOnlyList<string> AnswerableKinds,
    IReadOnlyList<WhyNotCandidateResult> Candidates,
    int? Best,
    WhyNotBound Bound);
