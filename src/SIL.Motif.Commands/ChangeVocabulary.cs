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
            [WfiAnalysisOperationKinds.AddRefEvaluations] = Evaluated,
            [WfiAnalysisOperationKinds.RemoveRefEvaluations] = SetToUnknown,
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
