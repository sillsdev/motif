using System.Collections.Generic;
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
}

/// <summary>How the words a finding touches were matched.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<WarningWordsMatch>))]
public enum WarningWordsMatch
{
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
    /// <summary>How many of the words were matched only by spelling.</summary>
    public int BySpellingOnly { get; init; }
}
