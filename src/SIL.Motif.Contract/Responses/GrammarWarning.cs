using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>A grammar-health diagnostic with the description and subjects supplied by PanGloss.</summary>
/// <param name="Severity">The diagnostic level: <c>warning</c> or <c>info</c>.</param>
/// <param name="CodeLabel">A readable name for the diagnostic code.</param>
/// <param name="Subject">The named FieldWorks subjects, including their reported link state.</param>
/// <param name="Problem">The report description, represented as display parts.</param>
/// <param name="Text">A readable form of the level, code, and description.</param>
public sealed record GrammarWarning(
    GrammarDiagnosticLevel Severity,
    [property: JsonPropertyName("kind")] string CodeLabel,
    IReadOnlyList<GrammarWarningPart> Subject,
    IReadOnlyList<GrammarWarningPart> Problem,
    string Text)
{
    /// <summary>The report's human-readable name for this diagnostic code.</summary>
    public string? Group { get; init; }

    /// <summary>The report's stable diagnostic code.</summary>
    public string? Code { get; init; }

    /// <summary>The complete description supplied by PanGloss.</summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>What a person can do in FieldWorks to address the diagnostic.</summary>
    public string? Guidance { get; init; }

    /// <summary>Whether the finding came from checking the grammar or importing it.</summary>
    public GrammarFindingOrigin Origin { get; init; }

    /// <summary>
    /// The Selection's words that use what the finding names, read from the stored Parse all words when the
    /// finding is read; <see langword="null"/> when no stored Parse all words matches the current Baseline and
    /// Selection, or when the finding was stored without what its subjects reach.
    /// </summary>
    public WarningWords? YourWords { get; init; }

    /// <summary>
    /// Attribution even without stored word evidence. A resolvable route without an Assessment means evidence
    /// unavailable, never zero words; missing subjects and identities remain explicit.
    /// </summary>
    public WarningAttributionState AttributionState => YourWords?.State ??
        (Subject.Count == 0 ? WarningAttributionState.UnresolvedIdentity :
            Subject.Any(part => part.Reach?.Path == WarningWordsPath.MissingObject) ? WarningAttributionState.MissingObject :
            Subject.All(part => part.Reach?.Path == WarningWordsPath.ProjectWide) ? WarningAttributionState.ProjectWide :
            Subject.All(part => part.Reach?.Path == WarningWordsPath.UnresolvedIdentity) ? WarningAttributionState.UnresolvedIdentity :
            WarningAttributionState.EvidenceUnavailable);

    /// <summary>The specific identity or attribution limit, when there is one.</summary>
    public WarningAttributionReason? AttributionReason => YourWords?.Reason ??
        (Subject.Count == 0 ? WarningAttributionReason.NoSubject :
            Subject.Select(part => part.Reach?.Reason).FirstOrDefault(reason => reason is not null));
}

/// <summary>One summary row grouping diagnostics by their stable code.</summary>
/// <param name="Code">The diagnostic code used to match this row to its findings.</param>
/// <param name="GroupName">The name shown for this kind of diagnostic.</param>
/// <param name="Level">The report level for this kind of diagnostic.</param>
/// <param name="Count">How many diagnostics of this kind the report contains.</param>
public sealed record GrammarWarningSummary(string Code, string? GroupName, GrammarDiagnosticLevel Level, int Count)
{
    /// <summary>
    /// How many of the Selection's words this kind's findings touch, each counted once; <see langword="null"/> when
    /// not known, as for the parser's own summary rows or with no stored Parse all words.
    /// </summary>
    public int? YourWords { get; init; }
}

/// <summary>A named subject in a grammar-health diagnostic.</summary>
/// <param name="Text">The human-readable title and subtitle shown for the subject.</param>
/// <param name="Role">Whether the part is prose, a value, or a named FieldWorks subject.</param>
/// <param name="ObjectId">The subject GUID or internal identity.</param>
/// <param name="FieldWorksKind">The FieldWorks class name reported by PanGloss.</param>
/// <param name="FieldWorksLink">The report's FieldWorks URL when one is available.</param>
public sealed record GrammarWarningPart(
    string Text,
    GrammarWarningPartRole Role,
    string? ObjectId = null,
    [property: JsonPropertyName("kind")] string? FieldWorksKind = null,
    string? FieldWorksLink = null)
{
    /// <summary>The subject's title exactly as PanGloss reported it.</summary>
    public string? Title { get; init; }

    /// <summary>The subject's subtitle exactly as PanGloss reported it.</summary>
    public string? Subtitle { get; init; }

    /// <summary>The subject's FieldWorks GUID, if PanGloss recorded one.</summary>
    public string? SubjectGuid { get; init; }

    /// <summary>The report's internal identity for the subject, when available.</summary>
    public string? InternalId { get; init; }

    /// <summary>The GUID targeted by the FieldWorks link.</summary>
    public string? FieldWorksGuid { get; init; }

    /// <summary>The FieldWorks link state reported by PanGloss.</summary>
    public FieldWorksLinkStatus? LinkStatus { get; init; }

    /// <summary>The reason PanGloss gave when the link is unavailable.</summary>
    public FieldWorksLinkReason? LinkReason { get; init; }

    /// <summary>The FieldWorks tool identifier reported for an available link.</summary>
    public string? FieldWorksTool { get; init; }

    /// <summary>
    /// What the subject leads to in the grammar it was checked against, read from FieldWorks when the grammar was
    /// checked; <see langword="null"/> for a part that names no object.
    /// </summary>
    public WarningReach? Reach { get; init; }
}

/// <summary>How Motif finds the words a grammar finding's subject touches.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WarningWordsPath>))]
public enum WarningWordsPath
{
    /// <summary>Words use members of the resource; this does not prove a parse selected that resource.</summary>
    [JsonStringEnumMemberName("membership")]
    Membership,

    /// <summary>Feature specifications lead to their lexical or rule owners by references and ownership.</summary>
    [JsonStringEnumMemberName("through_feature_owners")]
    ThroughFeatureOwners,

    /// <summary>A project resource with no word attribution.</summary>
    [JsonStringEnumMemberName("project_wide")]
    ProjectWide,

    /// <summary>The GUID is absent or identifies a different class; Reason distinguishes these cases.</summary>
    [JsonStringEnumMemberName("missing_object")]
    MissingObject,

    /// <summary>A named subject has no usable project identity or supported word route.</summary>
    [JsonStringEnumMemberName("unresolved_identity")]
    UnresolvedIdentity,

    /// <summary>An allomorph or grammatical info: the stored analyses that use it.</summary>
    [JsonStringEnumMemberName("uses")]
    Uses,

    /// <summary>An entry or an environment: the stored analyses that use its allomorphs.</summary>
    [JsonStringEnumMemberName("through_allomorphs")]
    ThroughAllomorphs,

    /// <summary>A sense: the stored analyses that use its grammatical info.</summary>
    [JsonStringEnumMemberName("through_grammatical_info")]
    ThroughGrammaticalInfo,

    /// <summary>
    /// A natural class: the stored analyses that use the allomorphs its environments condition, and the words the
    /// phonological rules that name it ran in.
    /// </summary>
    [JsonStringEnumMemberName("through_environments_and_rules")]
    ThroughEnvironmentsAndRules,

    /// <summary>A rule: the words the stored per-word rule times say it ran in, by its key.</summary>
    [JsonStringEnumMemberName("rule_times")]
    RuleTimes,

    /// <summary>A letter or phoneme: the words spelled with it. The one match that is not by identity.</summary>
    [JsonStringEnumMemberName("spelling")]
    Spelling,

    /// <summary>Motif can't tell which words the subject touches; <see cref="WarningReach.CantTell"/> says why.</summary>
    [JsonStringEnumMemberName("cant_tell")]
    CantTell,
}

/// <summary>Why Motif can't tell which words a finding touches.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WarningCantTell>))]
public enum WarningCantTell
{
    /// <summary>PanGloss names no object for the finding.</summary>
    [JsonStringEnumMemberName("nothing_named")]
    NothingNamed,

    /// <summary>PanGloss names an object of a kind Motif doesn't follow to words, such as a template.</summary>
    [JsonStringEnumMemberName("kind_not_followed")]
    KindNotFollowed,

    /// <summary>The object PanGloss names is not in the FieldWorks project the grammar was checked against.</summary>
    [JsonStringEnumMemberName("not_in_project")]
    NotInProject,
}

/// <summary>
/// What a finding's subject leads to, by FieldWorks identity: the allomorphs and grammatical infos whose stored
/// uses count, the keys whose stored per-word rule times count, or the spellings that match a word.
/// </summary>
/// <param name="Path">How the subject reaches words.</param>
public sealed record WarningReach(WarningWordsPath Path)
{
    /// <summary>The specific identity or attribution limit, independent of any Selection.</summary>
    public WarningAttributionReason? Reason { get; init; }

    /// <summary>Why the subject reaches no words, when <see cref="Path"/> is <see cref="WarningWordsPath.CantTell"/>.</summary>
    public WarningCantTell? CantTell { get; init; }

    /// <summary>The GUIDs of the allomorphs a stored analysis may use.</summary>
    public IReadOnlyList<string> AllomorphIds { get; init; } = [];

    /// <summary>The GUIDs of the grammatical infos a stored analysis may use.</summary>
    public IReadOnlyList<string> GrammaticalInfoIds { get; init; } = [];

    /// <summary>The kinds and keys PanGloss's statistics time the subject's rules under.</summary>
    public IReadOnlyList<TraceTimingKey> TimingKeys { get; init; } = [];

    /// <summary>The letters that match a word spelled with them, as FieldWorks writes the phoneme's codes.</summary>
    public IReadOnlyList<string> Spellings { get; init; } = [];

    /// <summary>Allomorph members of a resource, separate from exact lexical use routes.</summary>
    public IReadOnlyList<string> MembershipAllomorphIds { get; init; } = [];

    /// <summary>Grammatical info members, without evidence that a parse selected the resource.</summary>
    public IReadOnlyList<string> MembershipGrammaticalInfoIds { get; init; } = [];

    /// <summary>Rule members; recorded calls establish member activity, not use of the resource.</summary>
    public IReadOnlyList<TraceTimingKey> MembershipTimingKeys { get; init; } = [];
}

/// <summary>The strength of word attribution, independent of severity or cause.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WarningAttributionState>))]
public enum WarningAttributionState
{
    /// <summary>Stored analyses or rule calls match an exact lexical or rule route.</summary>
    [JsonStringEnumMemberName("exact_uses")] ExactUses,
    /// <summary>Words use members; selection of the resource is unproven.</summary>
    [JsonStringEnumMemberName("membership_candidates")] MembershipCandidates,
    /// <summary>Spelling contains a representation, without confirmed phoneme use.</summary>
    [JsonStringEnumMemberName("spelling_candidates")] SpellingCandidates,
    /// <summary>A supported route matches no word in this Selection.</summary>
    [JsonStringEnumMemberName("none_in_selection")] NoneInSelection,
    /// <summary>A project resource has no word attribution.</summary>
    [JsonStringEnumMemberName("project_wide")] ProjectWide,
    /// <summary>The subject GUID is missing or resolves to the wrong class.</summary>
    [JsonStringEnumMemberName("missing_object")] MissingObject,
    /// <summary>No subject, usable project GUID, or supported class route is supplied.</summary>
    [JsonStringEnumMemberName("unresolved_identity")] UnresolvedIdentity,
    /// <summary>No usable stored word evidence is available for a supported route.</summary>
    [JsonStringEnumMemberName("evidence_unavailable")] EvidenceUnavailable,
}

/// <summary>Why word attribution is unavailable, distinguishing absent GUIDs from wrong classes.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WarningAttributionReason>))]
public enum WarningAttributionReason
{
    /// <summary>PanGloss supplied no subject.</summary>
    [JsonStringEnumMemberName("no_subject")] NoSubject,
    /// <summary>A subject is named without a valid project GUID.</summary>
    [JsonStringEnumMemberName("named_without_project_guid")] NamedWithoutProjectGuid,
    /// <summary>The checked Baseline contains no object with this GUID.</summary>
    [JsonStringEnumMemberName("stale_guid")] StaleGuid,
    /// <summary>The GUID belongs to a different FieldWorks class.</summary>
    [JsonStringEnumMemberName("wrong_class")] WrongClass,
    /// <summary>The project resource has no lexical or rule owner to attribute.</summary>
    [JsonStringEnumMemberName("no_word_attribution")] NoWordAttribution,
    /// <summary>The named class has no supported route to stored word evidence.</summary>
    [JsonStringEnumMemberName("unsupported_kind")] UnsupportedKind,
}

/// <summary>How the words a finding touches were matched.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WarningWordsMatch>))]
public enum WarningWordsMatch
{
    /// <summary>By membership in a resource, excluded from exact-use counts.</summary>
    [JsonStringEnumMemberName("membership")]
    Membership,
    /// <summary>A project resource without word attribution.</summary>
    [JsonStringEnumMemberName("project_wide")]
    ProjectWide,
    /// <summary>A missing GUID or wrong class, distinguished by Reason.</summary>
    [JsonStringEnumMemberName("missing_object")]
    MissingObject,
    /// <summary>No subject, usable GUID, or supported class route, distinguished by Reason.</summary>
    [JsonStringEnumMemberName("unresolved_identity")]
    UnresolvedIdentity,
    /// <summary>By exact FieldWorks identity, through stored analyses or stored rule times.</summary>
    [JsonStringEnumMemberName("identity")]
    Identity,

    /// <summary>By spelling, because the finding names only letters or phonemes; shown labelled as such.</summary>
    [JsonStringEnumMemberName("spelling")]
    Spelling,

    /// <summary>Motif can't tell; <see cref="WarningWords.CantTell"/> says why.</summary>
    [JsonStringEnumMemberName("cant_tell")]
    CantTell,
}

/// <summary>
/// The Selection's words a finding touches, in Selection order, and how many of them each meaning holds. The join
/// shows use, not cause: a word that uses what a warning names may fail for another reason.
/// </summary>
/// <param name="Match">
/// How the words were matched. When some subjects match by identity and others only by spelling, identity wins and
/// the spelling matches are left out.
/// </param>
/// <param name="Words">The words, each with the row every page shows for it.</param>
/// <param name="ByMeaning">The words counted by meaning, most words first.</param>
public sealed record WarningWords(
    WarningWordsMatch Match, IReadOnlyList<ObjectUseWord> Words, IReadOnlyList<ObjectUseMeaning> ByMeaning)
{
    /// <summary>The explicit state; an empty supported route means none in this Selection.</summary>
    public WarningAttributionState State => Match switch
    {
        WarningWordsMatch.Identity => Words.Count > 0 ? WarningAttributionState.ExactUses : WarningAttributionState.NoneInSelection,
        WarningWordsMatch.Membership => Words.Count > 0 ? WarningAttributionState.MembershipCandidates : WarningAttributionState.NoneInSelection,
        WarningWordsMatch.Spelling => Words.Count > 0 ? WarningAttributionState.SpellingCandidates : WarningAttributionState.NoneInSelection,
        WarningWordsMatch.ProjectWide => WarningAttributionState.ProjectWide,
        WarningWordsMatch.MissingObject => WarningAttributionState.MissingObject,
        _ => WarningAttributionState.UnresolvedIdentity,
    };

    /// <summary>The specific identity or attribution limit, when no supported route exists.</summary>
    public WarningAttributionReason? Reason { get; init; }

    /// <summary>Words using members of the resource, retained alongside exact matches.</summary>
    public IReadOnlyList<ObjectUseWord> MembershipCandidates { get; init; } = [];

    /// <summary>Spelling candidates alongside exact matches, without confirmed phoneme use.</summary>
    public IReadOnlyList<ObjectUseWord> SpellingCandidates { get; init; } = [];

    /// <summary>Why no words could be found, when <see cref="Match"/> is <see cref="WarningWordsMatch.CantTell"/>.</summary>
    public WarningCantTell? CantTell { get; init; }

    /// <summary>The paths the words were found through, one for each kind of subject that matched.</summary>
    public IReadOnlyList<WarningWordsPath> Paths { get; init; } = [];
}

/// <summary>The Selection's words that use something any of some findings names, each counted once.</summary>
/// <param name="Words">How many words.</param>
/// <param name="NoParse">How many of them PanGloss built nothing for.</param>
/// <param name="ByMeaning">The words counted by meaning, most words first.</param>
public sealed record WarningWordsTouched(int Words, int NoParse, IReadOnlyList<ObjectUseMeaning> ByMeaning)
{
    /// <summary>Distinct spelling candidates without exact or membership evidence, excluded from Words.</summary>
    public int BySpellingOnly { get; init; }

    /// <summary>Distinct membership candidates without exact evidence, excluded from Words.</summary>
    public int ByMembershipOnly { get; init; }
}
