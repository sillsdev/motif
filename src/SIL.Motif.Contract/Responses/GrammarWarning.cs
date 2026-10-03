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
    public string? Title { get; init; }
    public string? Explanation { get; init; }
    public string? HelpPath { get; init; }
    public string? HelpBody { get; init; }
    public string? HelpUrl { get; init; }
    public string? Locale { get; init; }
    public GrammarSubjectStatus Scope { get; init; }
    public IReadOnlyList<GrammarFieldWorksPlace> FieldWorksPlaces { get; init; } = [];

    /// <summary>Whether the finding came from checking the grammar or importing it.</summary>
    public GrammarFindingOrigin Origin { get; init; }

    /// <summary>
    /// The Selection's exact word uses and separately labelled membership and spelling candidates, read from the
    /// stored Parse all words when the finding is read; <see langword="null"/> when no stored Parse all words matches
    /// the current Baseline and Selection, or when a subject was stored without what it reaches.
    /// </summary>
    public WarningWords? YourWords { get; init; }

    /// <summary>
    /// How strongly the finding is attributed to words, even without stored word evidence. A supported route with no
    /// matching Parse all words is <see cref="WarningAttributionState.EvidenceUnavailable"/>, never zero words; a
    /// finding with no supported route says which subject explains why, by <see cref="WarningReach.Unattributed"/>.
    /// </summary>
    public WarningAttributionState AttributionState => YourWords?.State ?? Unattributed() switch
    {
        null when Subject.Any(part => part.Reach is not null) => WarningAttributionState.EvidenceUnavailable,
        null => WarningAttributionState.UnresolvedIdentity,
        { Path: WarningWordsPath.MissingObject } => WarningAttributionState.MissingObject,
        { Path: WarningWordsPath.ProjectWide } => WarningAttributionState.ProjectWide,
        _ => WarningAttributionState.UnresolvedIdentity,
    };

    /// <summary>
    /// The specific identity or attribution limit behind <see cref="AttributionState"/>, when there is one;
    /// <see cref="WarningAttributionReason.NoSubject"/> when PanGloss names no subject at all.
    /// </summary>
    public WarningAttributionReason? AttributionReason => YourWords is { } yours ? yours.Reason :
        Subject.All(part => part.Reach is null) ? WarningAttributionReason.NoSubject : Unattributed()?.Reason;

    /// <summary>
    /// Limits encountered while following subjects or their owners, including limits alongside usable routes.
    /// These remain visible whether stored word evidence is available or not.
    /// </summary>
    public IReadOnlyList<WarningAttributionReason> AttributionLimits => Subject.Select(part => part.Reach)
        .OfType<WarningReach>().SelectMany(reach => reach.AttributionLimits.Concat(
            !reach.IsRoute && reach.Reason is { } reason ? [reason] : [])).Distinct().ToArray();

    private WarningReach? Unattributed() =>
        WarningReach.Unattributed(Subject.Select(part => part.Reach).OfType<WarningReach>().ToArray());
}

/// <summary>One summary row grouping diagnostics by their stable code.</summary>
/// <param name="Code">The diagnostic code used to match this row to its findings.</param>
/// <param name="GroupName">The name shown for this kind of diagnostic.</param>
/// <param name="Level">The report level for this kind of diagnostic.</param>
/// <param name="Count">How many diagnostics of this kind the report contains.</param>
public sealed record GrammarWarningSummary(string Code, string? GroupName, GrammarDiagnosticLevel Level, int Count)
{
    /// <summary>
    /// How many of the Selection's words exactly use something this kind's findings name, each counted once;
    /// membership and spelling candidates are excluded. <see langword="null"/> when not known, as for the parser's
    /// own summary rows or with no stored Parse all words.
    /// </summary>
    public int? YourWords { get; init; }

    /// <summary>Whether all findings have available word evidence and every named connection was followed.</summary>
    public bool? WordAttributionComplete { get; init; }

    /// <summary>The named connections that could not be followed, even when known matches exist.</summary>
    public IReadOnlyList<WarningAttributionReason> AttributionLimits { get; init; } = [];

    /// <summary>
    /// How many words this kind's findings reach only as membership candidates, excluded from
    /// <see cref="YourWords"/>; <see langword="null"/> when <see cref="YourWords"/> is.
    /// </summary>
    public int? ByMembershipOnly { get; init; }

    /// <summary>
    /// How many words this kind's findings reach only as spelling candidates, excluded from <see cref="YourWords"/>
    /// and <see cref="ByMembershipOnly"/>; <see langword="null"/> when <see cref="YourWords"/> is.
    /// </summary>
    public int? BySpellingOnly { get; init; }
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
    public GrammarSubjectStatus Status { get; init; }
    public string? Field { get; init; }
    public string? SourceClass { get; init; }

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
    /// <summary>
    /// A template, slot, ad hoc prohibition, irregularly inflected form type, phoneme set or feature system: the words
    /// that use one of its members, as membership candidates. Using an affix in a slot does not prove a parse used
    /// the slot, so these words are never counted as exact uses.
    /// </summary>
    [JsonStringEnumMemberName("membership")]
    Membership,

    /// <summary>
    /// A feature, feature value or feature structure: the stored analyses that use the allomorphs and grammatical
    /// infos whose feature specifications name it, and the words the rules whose specifications name it ran in.
    /// </summary>
    [JsonStringEnumMemberName("through_feature_owners")]
    ThroughFeatureOwners,

    /// <summary>A project resource that nothing a word uses refers to; <see cref="WarningReach.Reason"/> says so.</summary>
    [JsonStringEnumMemberName("project_wide")]
    ProjectWide,

    /// <summary>The GUID is absent or identifies a different class; Reason distinguishes these cases.</summary>
    [JsonStringEnumMemberName("missing_object")]
    MissingObject,

    /// <summary>
    /// A subject named without a project GUID, or of a kind Motif doesn't follow to words;
    /// <see cref="WarningReach.Reason"/> distinguishes these cases.
    /// </summary>
    [JsonStringEnumMemberName("unresolved_identity")]
    UnresolvedIdentity,

    /// <summary>An allomorph or grammatical info: the stored analyses that use it.</summary>
    [JsonStringEnumMemberName("uses")]
    Uses,

    /// <summary>
    /// An entry, an environment or a stem name: the stored analyses that use its allomorphs, or the allomorphs and
    /// derivational affixes that name it.
    /// </summary>
    [JsonStringEnumMemberName("through_allomorphs")]
    ThroughAllomorphs,

    /// <summary>
    /// A sense or an inflection class: the stored analyses that use its grammatical info, or the grammatical infos
    /// and affix allomorphs that name it.
    /// </summary>
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

    /// <summary>
    /// A letter or phoneme: the words spelled with its letters, as spelling candidates. Spelling never proves the
    /// parser used the phoneme, so these words are never counted as exact uses.
    /// </summary>
    [JsonStringEnumMemberName("spelling")]
    Spelling,
}

/// <summary>
/// What a finding's subject leads to, by FieldWorks identity: the allomorphs and grammatical infos whose stored
/// uses count, the keys whose stored per-word rule times count, or the spellings that match a word. Read from the
/// checked Baseline's own copy of the project, so a later FieldWorks edit never changes it.
/// </summary>
/// <param name="Path">
/// How the subject reaches words. When it is <see cref="WarningWordsPath.Membership"/>, the identities in
/// <see cref="AllomorphIds"/>, <see cref="GrammaticalInfoIds"/> and <see cref="TimingKeys"/> are members of the
/// subject, and the words they find are membership candidates, never exact uses. On every other path they are
/// exact routes, and only the <c>Membership</c> lists hold members.
/// </param>
public sealed record WarningReach(WarningWordsPath Path)
{
    /// <summary>
    /// The specific identity or attribution limit, independent of any Selection; set when <see cref="Path"/> is
    /// <see cref="WarningWordsPath.MissingObject"/>, <see cref="WarningWordsPath.UnresolvedIdentity"/> or
    /// <see cref="WarningWordsPath.ProjectWide"/>.
    /// </summary>
    public WarningAttributionReason? Reason { get; init; }

    /// <summary>
    /// Limits from subjects or owners whose routes could not be followed, retained alongside usable routes.
    /// A completed route's word matches do not remove these limits.
    /// </summary>
    public IReadOnlyList<WarningAttributionReason> AttributionLimits { get; init; } = [];

    /// <summary>Whether this reach is a route to words at all, rather than a reason there is none.</summary>
    [JsonIgnore]
    public bool IsRoute => Path is not (WarningWordsPath.MissingObject or WarningWordsPath.UnresolvedIdentity or
        WarningWordsPath.ProjectWide);

    /// <summary>
    /// The subject that explains a finding with no route to words: a missing object first, then an unresolved
    /// identity, then a project-wide resource. <see langword="null"/> when some subject is a route, or none is named.
    /// </summary>
    public static WarningReach? Unattributed(IReadOnlyList<WarningReach> reaches) =>
        reaches.Count == 0 || reaches.Any(reach => reach.IsRoute)
            ? null
            : reaches.FirstOrDefault(reach => reach.Path == WarningWordsPath.MissingObject) ??
              reaches.FirstOrDefault(reach => reach.Path == WarningWordsPath.UnresolvedIdentity) ??
              reaches[0];

    /// <summary>The GUIDs of the allomorphs a stored analysis may use.</summary>
    public IReadOnlyList<string> AllomorphIds { get; init; } = [];

    /// <summary>The GUIDs of the grammatical infos a stored analysis may use.</summary>
    public IReadOnlyList<string> GrammaticalInfoIds { get; init; } = [];

    /// <summary>The kinds and keys PanGloss's statistics time the subject's rules under.</summary>
    public IReadOnlyList<TraceTimingKey> TimingKeys { get; init; } = [];

    /// <summary>
    /// The phoneme codes or vernacular allomorph forms that can match a word's spelling. Lexical forms are a
    /// fallback only for words with no analysis, and never establish use of the named object by identity.
    /// </summary>
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
    /// <summary>PanGloss captured an unresolved reference, independently of later project data.</summary>
    [JsonStringEnumMemberName("unresolved_reference")] UnresolvedReference,
    /// <summary>The checked Baseline contains no object with this GUID.</summary>
    [JsonStringEnumMemberName("stale_guid")] StaleGuid,
    /// <summary>The GUID belongs to a different FieldWorks class.</summary>
    [JsonStringEnumMemberName("wrong_class")] WrongClass,
    /// <summary>The project resource has no lexical or rule owner to attribute.</summary>
    [JsonStringEnumMemberName("no_word_attribution")] NoWordAttribution,
    /// <summary>Natural-class notation supplies a label without a resolved object reference.</summary>
    [JsonStringEnumMemberName("unresolved_environment_notation")] UnresolvedEnvironmentNotation,
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

    /// <summary>By spelling of named letters or forms, without analysis identity; shown labelled as such.</summary>
    [JsonStringEnumMemberName("spelling")]
    Spelling,

}

/// <summary>
/// The Selection's words a finding touches, in Selection order, and how many of them each meaning holds. The join
/// shows use, not cause: a word that uses what a warning names may fail for another reason.
/// </summary>
/// <param name="Match">
/// The strongest route any subject has: <see cref="WarningWordsMatch.Identity"/> over
/// <see cref="WarningWordsMatch.Membership"/> over <see cref="WarningWordsMatch.Spelling"/>. With no route, it says
/// why: a missing object, an unresolved identity, or a project-wide resource.
/// </param>
/// <param name="Words">
/// The words <paramref name="Match"/> found, each with the row every page shows for it: exact uses for
/// <see cref="WarningWordsMatch.Identity"/>, membership or spelling candidates for the other two routes.
/// <see cref="MembershipCandidates"/> and <see cref="SpellingCandidates"/> hold the weaker matches beyond these, so
/// the three lists never share a word.
/// </param>
/// <param name="ByMeaning">The words in <paramref name="Words"/> counted by meaning, most words first.</param>
public sealed record WarningWords(
    WarningWordsMatch Match, IReadOnlyList<ObjectUseWord> Words, IReadOnlyList<ObjectUseMeaning> ByMeaning)
{
    /// <summary>
    /// The strongest attribution the words show: exact uses, then membership candidates, then spelling candidates,
    /// then none in this Selection for a route that matched nothing. With no route, the reason there is none.
    /// </summary>
    public WarningAttributionState State => Match switch
    {
        WarningWordsMatch.ProjectWide => WarningAttributionState.ProjectWide,
        WarningWordsMatch.MissingObject => WarningAttributionState.MissingObject,
        WarningWordsMatch.UnresolvedIdentity => WarningAttributionState.UnresolvedIdentity,
        _ when Words.Count > 0 => Match switch
        {
            WarningWordsMatch.Identity => WarningAttributionState.ExactUses,
            WarningWordsMatch.Membership => WarningAttributionState.MembershipCandidates,
            _ => WarningAttributionState.SpellingCandidates,
        },
        _ when MembershipCandidates.Count > 0 => WarningAttributionState.MembershipCandidates,
        _ when SpellingCandidates.Count > 0 => WarningAttributionState.SpellingCandidates,
        _ => WarningAttributionState.NoneInSelection,
    };

    /// <summary>The specific identity or attribution limit, when no subject is a route to words.</summary>
    public WarningAttributionReason? Reason { get; init; }

    /// <summary>
    /// Words that use a member of what the finding names, such as an affix in a named slot, beyond
    /// <see cref="Words"/>: candidates, not confirmed uses of the named object itself.
    /// </summary>
    public IReadOnlyList<ObjectUseWord> MembershipCandidates { get; init; } = [];

    /// <summary>
    /// Words spelled with a named phoneme's letters, beyond <see cref="Words"/> and
    /// <see cref="MembershipCandidates"/>: candidates, not confirmed uses of the phoneme.
    /// </summary>
    public IReadOnlyList<ObjectUseWord> SpellingCandidates { get; init; } = [];

    /// <summary>The paths the words were found through, one for each kind of subject that matched.</summary>
    public IReadOnlyList<WarningWordsPath> Paths { get; init; } = [];
}

/// <summary>
/// The Selection's words that use something any of some findings names, each counted once. Only exact uses are
/// counted in <paramref name="Words"/>; a word some finding reaches exactly is never also a candidate.
/// </summary>
/// <param name="Words">How many words exactly use something a finding names.</param>
/// <param name="NoParse">How many of those words PanGloss built nothing for.</param>
/// <param name="ByMeaning">Those words counted by meaning, most words first.</param>
public sealed record WarningWordsTouched(int Words, int NoParse, IReadOnlyList<ObjectUseMeaning> ByMeaning)
{
    /// <summary>Whether the counts cover every finding and all of its named connections.</summary>
    public bool IsComplete { get; init; } = true;

    /// <summary>The named connections that could not be followed; counts include only known matches.</summary>
    public IReadOnlyList<WarningAttributionReason> AttributionLimits { get; init; } = [];

    /// <summary>Findings whose word evidence was unavailable, excluded from the known counts.</summary>
    public int UnavailableFindingCount { get; init; }

    /// <summary>Distinct spelling candidates without exact or membership evidence, excluded from Words.</summary>
    public int BySpellingOnly { get; init; }

    /// <summary>Distinct membership candidates without exact evidence, excluded from Words.</summary>
    public int ByMembershipOnly { get; init; }
}

/// <summary>A verified FieldWorks destination supplied by the diagnostic's advice owner.</summary>
public sealed record GrammarFieldWorksPlace(string Tool, string Field);
