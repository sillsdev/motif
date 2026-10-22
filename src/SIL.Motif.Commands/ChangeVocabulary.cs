using System.Globalization;
using System.Text;
using System.Text.Json;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Model;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Commands;

/// <summary>What a person calls one kind of change, in every place Motif names it.</summary>
/// <param name="Key">The stable name of the kind, used to group and count; <c>change</c> for an unknown kind.</param>
/// <param name="Singular">The noun for one such change, such as "rule".</param>
/// <param name="Plural">The noun for several, such as "rules".</param>
/// <param name="Title">A sentence-case title for one change, such as "Add a phonological rule".</param>
/// <param name="ChangesGrammar">
/// Whether a change of this kind can move a parse anywhere in the project, rather than only for the word it
/// names; a Trial of a Proposal with one measures the Default Selection.
/// </param>
public sealed record ChangeKind(string Key, string Singular, string Plural, string Title, bool ChangesGrammar)
{
    /// <summary>The noun for <paramref name="count"/> changes of this kind, with the count: "2 rules".</summary>
    public string Count(int count) =>
        count.ToString(CultureInfo.CurrentCulture) + " " + (count == 1 ? Singular : Plural);
}

/// <summary>How many changes of one kind a Proposal makes.</summary>
/// <param name="Kind">The kind of change.</param>
/// <param name="Count">How many operations of that kind the Proposal holds.</param>
public sealed record ChangeKindTally(ChangeKind Kind, int Count)
{
    /// <summary>The count and its noun, such as "2 rules".</summary>
    public string Text => Kind.Count(Count);
}

/// <summary>
/// The one table that names a Proposal's changes: operation kind to change kind, its nouns, its title, and whether
/// it changes the grammar. The Proposals page, the review list and the word card all read it, so a new operation
/// family or a better title is taught here once.
/// </summary>
/// <remarks>
/// Every operation kind maps to something: a kind this table does not know is a plain "change", titled from its
/// own name, and is treated as a grammar change unless it belongs to the analysis group, because a Trial of too
/// many words costs time while a Trial of too few would mislead.
/// </remarks>
public static class ChangeVocabulary
{
    /// <summary>The key of a kind this table does not name.</summary>
    public const string GenericKey = "change";

    private const string AnalysisGroup = "analysis/";

    private static readonly ChangeKind Approved = Word("parse-approved", "parse approved", "parses approved",
        "Approve an analysis");
    private static readonly ChangeKind Disapproved = Word("parse-disapproved", "parse disapproved",
        "parses disapproved", "Disapprove an analysis");
    private static readonly ChangeKind Evaluated = Word("parse-evaluated", "parse opinion", "parse opinions",
        "Change an opinion on an analysis");
    private static readonly ChangeKind SetToUnknown = Word("parse-unknown", "parse set to Unknown",
        "parses set to Unknown", "Set an analysis to Unknown");
    private static readonly ChangeKind Removed = Word("parse-removed", "parse removed", "parses removed",
        "Remove an analysis");
    private static readonly ChangeKind Added = Word("parse-added", "parse added", "parses added", "Add an analysis");
    private static readonly ChangeKind Spelling = Word("spelling", "spelling marked", "spellings marked",
        "Mark incorrectly spelled");

    private static readonly IReadOnlyDictionary<string, ChangeKind> ByOperationKind =
        new Dictionary<string, ChangeKind>(StringComparer.Ordinal)
        {
            ["grammar/phPhonData/createPhonRules"] = Grammar("rule", "rule", "rules", "Add a phonological rule"),
            ["lexical/lexEntry/createLexemeForm"] = Grammar("allomorph", "allomorph", "allomorphs", "Add an allomorph"),
            ["lexical/lexEntry/createAlternateForms"] =
                Grammar("allomorph", "allomorph", "allomorphs", "Add an allomorph"),
            ["grammar/phPhonData/createEnvironments"] =
                Grammar("environment", "environment", "environments", "Add an environment"),
            ["grammar/phPhonData/createNaturalClasses"] =
                Grammar("natural-class", "natural class", "natural classes", "Add a natural class"),
            ["grammar/phPhonemeSet/createPhonemes"] = Grammar("phoneme", "phoneme", "phonemes", "Add a phoneme"),
            ["grammar/partOfSpeech/createAffixTemplates"] =
                Grammar("template", "affix template", "affix templates", "Add an affix template"),
            ["grammar/partOfSpeech/createAffixSlots"] =
                Grammar("slot", "affix slot", "affix slots", "Add an affix slot"),
            ["texts/langProject/createTexts"] = Word("text", "text", "texts", "Add a text"),
            ["analysis/langProject/createWordforms"] = Word("wordform", "wordform", "wordforms", "Add a wordform"),
            ["lexical/lexDb/createEntries"] = Grammar("entry", "entry", "entries", "Add a dictionary entry"),
            ["lists/cmPossibilityList/createPossibilities"] =
                Grammar("category", "category", "categories", "Add a part of speech"),
            ["lists/cmPossibility/createSubPossibilities"] =
                Grammar("category", "category", "categories", "Add a part of speech"),
            [WfiAnalysisOperationKinds.AddRefEvaluations] = Evaluated,
            [WfiAnalysisOperationKinds.RemoveRefEvaluations] = SetToUnknown,
            [WfiAnalysisAgentEvaluationOperationKinds.Add] = Word("source-validated", "source-validated decision",
                "source-validated decisions", "Record a source-validated decision"),
            [DecisionEvidenceOperationKinds.Record] = Word("decision-evidence", "decision evidence record",
                "decision evidence records", "Record decision evidence"),
            [WfiAnalysisOperationKinds.DeleteAnalysis] = Removed,
            [WfiAnalysisOperationKinds.CreateAnalysis] = Added,
            [WfiWordformSpellingStatusOperationKinds.SetSpellingStatus] = Spelling,
        };

    // A pending word change names its action; these read after the word, as "cat: approve an analysis".
    private static readonly IReadOnlyDictionary<string, ChangeKind> ByWordAction =
        new Dictionary<string, ChangeKind>(StringComparer.Ordinal)
        {
            [AnalysisChangeKinds.Approve] = Approved,
            [AnalysisChangeKinds.Reject] = Disapproved,
            [AnalysisChangeKinds.Candidate] = SetToUnknown,
            [AnalysisChangeKinds.AddCandidate] = Added,
            [AnalysisChangeKinds.RemoveAnalysis] = Removed,
            [AnalysisChangeKinds.IncorrectSpelling] = Spelling,
        };

    // A composer call reads as what a linguist did: "Add phoneme ɸ", or "Add a phoneme" when it names nothing.
    private static readonly IReadOnlyDictionary<string, (string Named, string Unnamed)> ByComposer =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["AuthorPhoneme"] = ("Add phoneme", "Add a phoneme"),
            ["AuthorNaturalClass"] = ("Add natural class", "Add a natural class"),
            ["AuthorEnvironment"] = ("Add environment", "Add an environment"),
            ["AuthorPhonologicalRule"] = ("Add rule", "Add a phonological rule"),
            ["AuthorAffixSlot"] = ("Add affix slot", "Add an affix slot"),
            ["AuthorPartOfSpeech"] = ("Add category", "Add a part of speech"),
            ["AuthorLexicalEntry"] = ("Add entry", "Add a dictionary entry"),
            ["AuthorAlternateForm"] = ("Add alternate form", "Add an alternate form to an entry"),
            ["AuthorAffixProcessAllomorph"] = ("Add process allomorph", "Add an affix process allomorph to an entry"),
            ["AuthorAffixTemplate"] = ("Add affix template", "Add an affix template"),
            ["AuthorFeatureValue"] = ("Add feature value", "Add a feature value"),
            ["AuthorFeatureStructure"] = ("Add feature structure", "Add a feature structure"),
            ["AuthorLexemeForm"] = ("Add lexeme form", "Add a lexeme form"),
            ["EditNaturalClass"] = ("Change natural class", "Change a natural class"),
            ["RelinkNaturalClass"] = ("Point to natural class", "Point to another natural class"),
            ["EditAdhocProhibition"] = ("Change restriction", "Change a co-occurrence restriction"),
            ["EditCompoundRule"] = ("Change compound rule", "Turn a compound rule off or on"),
            ["SetNoDefaultCompounding"] = ("Change default compounding", "Turn default compounding off or on"),
            ["RewriteLexemeForm"] = ("Change lexeme form", "Rewrite the lexeme form of existing entries"),
            ["EditDerivationalAffixRestriction"] = ("Restrict derivational affix", "Restrict the stems a derivational affix attaches to"),
            ["EditAffixSlot"] = ("Change affix slot", "Change an affix slot"),
            ["EditAffixTemplate"] = ("Change affix template", "Change an affix template"),
            ["EditInflectionalAffix"] = ("Change affix", "Change an inflectional affix"),
            ["EditAllomorphCondition"] = ("Change where allomorph", "Change where an allomorph occurs"),
            ["AuthorText"] = ("Add text", "Add a text"),
            ["EditAnalysisDecision"] = ("Decide analysis", "Record a decision about an analysis"),
            ["OrderAllomorphs"] = ("Reorder allomorphs of", "Reorder allomorphs"),
            ["RetireAllomorph"] = ("Retire allomorph", "Retire an allomorph"),
            ["RetireRedundantZeroAffix"] = ("Retire zero affix", "Retire a zero affix"),
            ["RecordParsimonyDisposition"] = ("Record a decision on", "Record a Parsimony decision"),
        };

    /// <summary>
    /// The review title of one composer call, in a linguist's words: "Add phoneme ɸ" when the call names what it
    /// makes, "Add a phoneme" when it does not. A composer this table does not know reads from its own name, with
    /// "Author" read as "Add" and "Edit" as "Change".
    /// </summary>
    /// <param name="composer">The composer's name, such as <c>AuthorPhoneme</c>.</param>
    /// <param name="name">The name the call gave what it makes, when it gave one.</param>
    public static string ComposerTitle(string composer, string? name = null)
    {
        composer ??= string.Empty;
        var (named, unnamed) = ByComposer.TryGetValue(composer, out var known) ? known : FromComposerName(composer);
        return string.IsNullOrWhiteSpace(name) ? unnamed : named + " " + name.Trim();
    }

    /// <summary>
    /// What a group of operations makes, counted by kind in linguist nouns: "1 rule", "2 phonemes, 1 natural class".
    /// Kinds this table does not name are counted only when nothing else is: "3 changes".
    /// </summary>
    public static string Summary(IReadOnlyList<DraftOperation> operations)
    {
        ArgumentNullException.ThrowIfNull(operations);
        var tallies = operations.Select(Of).GroupBy(kind => kind.Key, StringComparer.Ordinal)
            .Select(group => new ChangeKindTally(group.First(), group.Count())).ToArray();
        var named = tallies.Where(tally => tally.Kind.Key != GenericKey).ToArray();
        return named.Length > 0
            ? string.Join(", ", named.Select(tally => tally.Text))
            : new ChangeKind(GenericKey, "change", "changes", "Change", true).Count(operations.Count);
    }

    /// <summary>Names one operation kind; an evaluation's member tells approval from disapproval.</summary>
    /// <param name="operationKind">The operation kind, such as <c>grammar/phPhonData/createPhonRules</c>.</param>
    /// <param name="evaluationMember">The <c>member</c> of an evaluation's payload, when the caller has it.</param>
    public static ChangeKind Of(string operationKind, string? evaluationMember = null)
    {
        operationKind ??= string.Empty;
        if (operationKind == WfiAnalysisOperationKinds.AddRefEvaluations)
        {
            return evaluationMember switch
            {
                HumanEvaluationPayload.Approves => Approved,
                HumanEvaluationPayload.Disapproves => Disapproved,
                _ => Evaluated,
            };
        }
        return ByOperationKind.TryGetValue(operationKind, out var known)
            ? known
            : new ChangeKind(GenericKey, "change", "changes", Humanize(operationKind),
                !operationKind.StartsWith(AnalysisGroup, StringComparison.Ordinal));
    }

    /// <summary>Names one operation of a finalized Proposal.</summary>
    public static ChangeKind Of(OperationEnvelope operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return Of(operation.Kind, MemberOf(operation.After));
    }

    /// <summary>Names one operation of a Draft.</summary>
    public static ChangeKind Of(DraftOperation operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return Of(operation.Kind, operation.After.TryGetValue("member", out var member) &&
            member.ValueKind == JsonValueKind.String ? member.GetString() : null);
    }

    /// <summary>Names a pending word change by the action a person chose, such as <c>approve</c>.</summary>
    public static ChangeKind OfWordAction(string action) =>
        ByWordAction.TryGetValue(action ?? string.Empty, out var known)
            ? known
            : new ChangeKind(GenericKey, "change", "changes", Humanize(action ?? string.Empty), false);

    /// <summary>The review title of a pending word change: "cat: approve an analysis".</summary>
    public static string WordTitle(string word, string action)
    {
        var title = OfWordAction(action).Title;
        return word + ": " + (title.Length == 0 ? title : char.ToLowerInvariant(title[0]) + title[1..]);
    }

    /// <summary>Counts a Proposal's changes by kind, in the order each kind first appears.</summary>
    public static IReadOnlyList<ChangeKindTally> Count(Proposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return proposal.Operations.Select(Of).GroupBy(kind => kind.Key, StringComparer.Ordinal)
            .Select(group => new ChangeKindTally(group.First(), group.Count())).ToArray();
    }

    /// <summary>Whether any of a Proposal's changes can move a parse beyond the words it names.</summary>
    public static bool ChangesGrammar(Proposal proposal)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        return proposal.Operations.Any(operation => Of(operation).ChangesGrammar);
    }

    private static ChangeKind Grammar(string key, string singular, string plural, string title) =>
        new(key, singular, plural, title, true);

    private static ChangeKind Word(string key, string singular, string plural, string title) =>
        new(key, singular, plural, title, false);

    private static string? MemberOf(JsonElement? after) =>
        after is { ValueKind: JsonValueKind.Object } payload &&
        payload.TryGetProperty("member", out var member) && member.ValueKind == JsonValueKind.String
            ? member.GetString()
            : null;

    private static (string Named, string Unnamed) FromComposerName(string composer)
    {
        var words = Humanize(composer);
        foreach (var (prefix, verb) in new[] { ("Author ", "Add "), ("Edit ", "Change ") })
            if (words.StartsWith(prefix, StringComparison.Ordinal)) words = verb + words[prefix.Length..];
        return (words, words);
    }

    // Kinds read "group/entity/verbNoun", and pending actions "add-candidate"; the last segment reads as words.
    private static string Humanize(string kind)
    {
        var name = kind[(kind.LastIndexOf('/') + 1)..];
        var words = new StringBuilder();
        foreach (var ch in name)
        {
            if (ch is '-' or '_') words.Append(' ');
            else if (char.IsUpper(ch) && words.Length > 0 && words[^1] != ' ')
                words.Append(' ').Append(char.ToLowerInvariant(ch));
            else words.Append(words.Length == 0 ? char.ToUpperInvariant(ch) : ch);
        }
        return words.Length == 0 ? "Change" : words.ToString();
    }
}
