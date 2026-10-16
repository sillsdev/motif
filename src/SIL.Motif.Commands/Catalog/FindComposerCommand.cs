using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Help;

namespace SIL.Motif.Commands.Catalog;

/// <summary>Finds the composers that fit a goal or serve a Subject, so an agent need not know their verbs.</summary>
/// <param name="Goal">What the linguist wants done, in a few words; exclusive with <paramref name="Subject"/>.</param>
/// <param name="Subject">A Subject id to list every composer that serves it; exclusive with <paramref name="Goal"/>.</param>
/// <param name="Detail">Either <c>concise</c> or <c>detailed</c>; detailed adds a worked example and the Subject page.</param>
public sealed record FindComposerRequest(string? Goal, string? Subject, string Detail = "concise");

/// <summary>The Subject page a result points at, with its opening paragraph.</summary>
/// <param name="Id">The Subject id.</param>
/// <param name="Title">The Subject's heading.</param>
/// <param name="Excerpt">The page's first paragraph, or null when the Subject has no page.</param>
public sealed record ComposerSubjectPage(string Id, string Title, string? Excerpt);

/// <summary>One composer a search found.</summary>
/// <param name="Verb">The composing verb.</param>
/// <param name="Title">The composer's short name.</param>
/// <param name="Description">When to reach for the composer.</param>
/// <param name="Subjects">The Subject ids the composer serves.</param>
/// <param name="IntentSchema">The closed JSON Schema of the intent, or null when only Help describes it.</param>
/// <param name="SchemaSource"><c>shape</c> when the schema is the composer's own, <c>help</c> when it is not.</param>
/// <param name="Example">In detailed mode, the first worked example on the composer's Help page, or null.</param>
/// <param name="SubjectPage">In detailed mode, the Subject page that best fits the search.</param>
/// <param name="Score">In a goal search, the rarity-weighted overlap that ranked the composer; null in a Subject search.</param>
/// <param name="MatchedWords">In a goal search, the goal's words found in the composer's text; null in a Subject search.</param>
public sealed record ComposerMatch(
    string Verb,
    string Title,
    string Description,
    IReadOnlyList<string> Subjects,
    JsonNode? IntentSchema,
    string SchemaSource,
    string? Example = null,
    ComposerSubjectPage? SubjectPage = null,
    double? Score = null,
    IReadOnlyList<string>? MatchedWords = null);

/// <summary>A Subject named as the nearest to a goal no composer covers.</summary>
/// <param name="Id">The Subject id.</param>
/// <param name="Title">The Subject's heading.</param>
public sealed record NearestSubject(string Id, string Title);

/// <summary>A composer that overlaps a goal too weakly to cover it.</summary>
/// <param name="Verb">The composing verb.</param>
/// <param name="Score">The rarity-weighted overlap, rounded to two places.</param>
public sealed record NearestComposer(string Verb, double Score);

/// <summary>The composers a search found, best first, or the statement that none covers the goal.</summary>
/// <param name="Mode"><c>goal</c> or <c>subject</c>, whichever the request used.</param>
/// <param name="Results">
/// At most five composers in a goal search, every composer of the Subject in a Subject search; empty when
/// <paramref name="NoMatch"/> is true.
/// </param>
/// <param name="NoMatch">True when no composer covers the goal, so a weak list would mislead.</param>
/// <param name="NearestSubjects">When there is no match, up to two Subjects closest to the goal; otherwise null.</param>
/// <param name="NearestComposers">When there is no match, up to three composers with their weak scores; otherwise null.</param>
/// <param name="Missing">
/// When there is no match, one sentence to relay to the person saying what Motif lacks; otherwise null.
/// </param>
/// <param name="Next">When there is no match, what to do: tell the person what is missing; otherwise null.</param>
public sealed record FindComposerResponse(
    string Mode,
    IReadOnlyList<ComposerMatch> Results,
    bool NoMatch = false,
    IReadOnlyList<NearestSubject>? NearestSubjects = null,
    IReadOnlyList<NearestComposer>? NearestComposers = null,
    string? Missing = null,
    string? Next = null);

/// <summary>
/// Searches the composer registry by goal or by Subject. The ranking is a case-insensitive overlap of the goal's
/// words with each composer's example goals (weight 3), title (2) and description (1), and a word that appears in a
/// Subject title the composer serves counts (2). Each word's hits are scaled by how few composers mention it, so a
/// rare word outweighs a common one. Reading only: nothing is staged and no project is opened.
/// <para>
/// A goal is answered in two steps. Content stems are the goal's words
/// of three or more letters minus a short stop-word list, each cut to a six-letter stem with a plural s removed.
/// First, each Subject, served or reserved, has a list of distinctive terms in Help; a term is hit when all its
/// stems are among the goal's stems, and the Subject with the most hits is the strongest. When that is one of the
/// reserved Subjects (infixation, reduplication, example sentences, grammar explanation, or outside authoring),
/// including on a tie with a served Subject, the answer is <c>noMatch</c> with it as the nearest Subject. Second,
/// a composer matches only if its example goals and title together contain at least two distinct stems of the
/// goal, or at least one when the goal has two stems or fewer; overlap with a description or a Subject title alone
/// never counts, and neither does a generic word such as add, form or rule.
/// The first step catches phenomena Motif has no composer for, which share common words with composers
/// that serve something else; the second step is a plain overlap count. Only matching composers are listed. When none matches, the
/// answer is <c>noMatch</c> with no results, the two nearest Subjects (the routed one first, then overlap with
/// Subject titles, descriptions and terms, then the Subjects of weakly overlapping composers), up to three weak
/// composers, and a ready sentence to relay. The thresholds and weights are named constants of this class.
/// </para>
/// </summary>
public static class FindComposerCommand
{
    // Every threshold and weight of the search rule lives in the constants below.

    // A goal word shorter than this is too common to name anything.
    private const int MinWordLetters = 3;
    // A longer word is cut to this many letters, so "analysed" meets "analyses" and "rules" meets "rule".
    private const int StemLength = 6;
    // A goal of this many stems or fewer is short enough that a single shared stem can cover it.
    private const int ShortGoalStems = 2;
    // Distinct goal stems a composer's example goals and title must contain to cover a short goal.
    private const int MatchesForShortGoal = 1;
    // Distinct goal stems they must contain to cover a longer goal; one shared word alone is too often chance.
    private const int MatchesForLongGoal = 2;
    // Example goals are written as the person would phrase the task, so they are the strongest evidence.
    private const int GoalWeight = 3;
    // A composer's title names its job but in fewer words than its example goals.
    private const int TitleWeight = 2;
    // A description says when to reach for a composer, and so mentions many neighbouring words.
    private const int DescriptionWeight = 1;
    // A Subject title the composer serves is as telling as the composer's own title.
    private const int SubjectTitleWeight = 2;
    // A hand-written Subject term is deliberate, so a whole-term hit outweighs a stray word of a description.
    private const int TermWeight = 3;
    // A goal search lists at most this many composers, enough to choose from without a wall of text.
    private const int MaxGoalResults = 5;
    // A no-match answer names at most this many Subjects, the routed one first.
    private const int MaxNearestSubjects = 2;
    // A no-match answer shows at most this many weak composers, to say what was ruled out.
    private const int MaxNearestComposers = 3;
    // The longest echo of the person's goal in the sentence to relay, so a pasted paragraph cannot flood it.
    private const int MaxEchoedGoalLength = 200;

    private static readonly Lazy<HelpCatalog> Help = new(() => HelpCatalog.Load());

    private static readonly Regex Example = new(
        "```(?:json|sh)[ \\t]*\\n(?<body>.*?)\\n```", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    // Generic action and grammar words say little about which composer is meant, so they rank but never cover.
    private static readonly string[] GenericWords =
    [
        "add", "make", "create", "new", "change", "set", "edit", "put", "give", "use", "write", "record", "mark",
        "remove", "delete", "update", "word", "form", "rule", "noun", "verb", "thing", "part", "type", "kind",
    ];

    private static readonly HashSet<string> GenericStems = GenericWords.Select(word => Stem(word))
        .ToHashSet(StringComparer.Ordinal);

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "with", "that", "this", "these", "those", "how", "can", "need", "want", "into", "from",
        "than", "there", "they", "them", "which", "what", "when", "where", "does", "have", "has", "was", "were",
        "are", "its", "our", "your", "would", "should", "could", "also", "any", "some", "each", "about", "like",
        "please", "make", "use", "using", "who", "why", "not", "but", "all", "out", "one", "per",
    };

    /// <summary>Ranks composers for a goal, or lists those serving a Subject. Reads only; nothing is staged.</summary>
    public static CommandOutcome<FindComposerResponse> Find(FindComposerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var goal = string.IsNullOrWhiteSpace(request.Goal) ? null : request.Goal.Trim();
        var subject = string.IsNullOrWhiteSpace(request.Subject) ? null : request.Subject.Trim();
        if (goal is null == subject is null)
            return Refuse(RefusalCodes.ComposerGoalOrSubject,
                "Give exactly one of a goal or a Subject to search for a composer.");
        var detailed = request.Detail?.Trim().ToLowerInvariant() switch
        {
            "concise" or null or "" => false,
            "detailed" => true,
            _ => (bool?)null,
        };
        if (detailed is null)
            return Refuse(RefusalCodes.ComposerInvalidDetail, "The detail level must be concise or detailed.");

        var help = Help.Value;
        var candidates = ProposalCommands.DraftComposers
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => Candidate.From(pair.Value, help))
            .ToArray();

        if (subject is not null)
        {
            if (ComposerSubjects.Reserved.Contains(subject, StringComparer.Ordinal))
                return CommandOutcome<FindComposerResponse>.Success(NoMatch(SubjectTitle(help, subject), [], [], help,
                    subject, new Dictionary<string, int>()) with { Mode = "subject" });
            if (!ComposerSubjects.All.Contains(subject, StringComparer.Ordinal))
                return Refuse(RefusalCodes.ComposerUnknownSubject,
                    $"No composer serves '{subject}'. Subjects are: {string.Join(", ", ComposerSubjects.All)}.");
            var serving = candidates.Where(candidate => candidate.Subjects.Contains(subject, StringComparer.Ordinal))
                .Select(candidate => candidate.ToMatch(detailed.Value, help, subject));
            return CommandOutcome<FindComposerResponse>.Success(new FindComposerResponse("subject", serving.ToArray()));
        }

        var spelled = SpelledWords(goal!);
        var words = spelled.Keys.ToHashSet(StringComparer.Ordinal);
        var fields = candidates.Select(candidate => candidate.Fields(help)).ToArray();
        var rarity = words.ToDictionary(word => word, word =>
            Math.Log(1 + (double)candidates.Length / Math.Max(1, fields.Count(field => field.Mentions(word)))),
            StringComparer.Ordinal);
        var scored = candidates
            .Select((candidate, index) => (Candidate: candidate, Fields: fields[index], Score: fields[index].Score(words, rarity)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Candidate.Verb, StringComparer.Ordinal)
            .ToArray();
        var termHits = TermHits(words, help);
        var best = termHits.Values.DefaultIfEmpty(0).Max();
        var reservedBest = ReservedSubjectAtTop(termHits, best);
        if (words.Count == 0 || reservedBest is not null)
            return CommandOutcome<FindComposerResponse>.Success(NoMatch(goal!, words, scored.Select(item =>
                (item.Candidate, item.Score)).ToArray(), help, reservedBest, termHits));
        var required = words.Count <= ShortGoalStems ? MatchesForShortGoal : MatchesForLongGoal;
        var matching = scored.Where(item => item.Fields.Covered(words).Count >= required).ToArray();
        if (matching.Length == 0)
            return CommandOutcome<FindComposerResponse>.Success(NoMatch(goal!, words, scored.Select(item =>
                (item.Candidate, item.Score)).ToArray(), help, null, termHits));
        var ranked = matching.Take(MaxGoalResults).Select(item => item.Candidate
            .ToMatch(detailed.Value, help, item.Candidate.BestSubject(words, help))
            with
            {
                Score = Math.Round(item.Score, 2),
                MatchedWords = words.Where(item.Fields.Mentions).Select(word => spelled[word]).ToArray(),
            });
        return CommandOutcome<FindComposerResponse>.Success(new FindComposerResponse("goal", ranked.ToArray()));
    }

    // A reserved Subject with the most term hits wins, ties with a served Subject included.
    private static string? ReservedSubjectAtTop(Dictionary<string, int> termHits, int best) =>
        best == 0 ? null : ComposerSubjects.Reserved.FirstOrDefault(id => termHits.GetValueOrDefault(id) == best);

    // How many of each Subject's terms the goal names: every stem of a term must be among the goal's stems.
    private static Dictionary<string, int> TermHits(HashSet<string> words, HelpCatalog help) => help.Subjects
        .ToDictionary(subject => subject.Id, subject => (subject.Terms ?? []).Count(term =>
            Words(term).ToArray() is { Length: > 0 } stems && stems.All(words.Contains)), StringComparer.Ordinal);

    private static FindComposerResponse NoMatch(string goal, HashSet<string> words,
        IReadOnlyList<(Candidate Candidate, double Score)> weak, HelpCatalog help, string? routed,
        IReadOnlyDictionary<string, int> termHits)
    {
        var composerScore = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (candidate, score) in weak)
            foreach (var id in candidate.Subjects) composerScore[id] = composerScore.GetValueOrDefault(id) + score;
        var nearest = ComposerSubjects.All.Concat(ComposerSubjects.Reserved).Where(id => id != routed)
            .Select((id, index) =>
            {
                var entry = help.Subjects.FirstOrDefault(subject => subject.Id == id);
                var overlap = (Words(SubjectTitle(help, id)).Count(words.Contains) * SubjectTitleWeight) +
                    Words(entry?.Description ?? string.Empty).Count(words.Contains) +
                    (termHits.GetValueOrDefault(id) * TermWeight);
                return (Id: id, Index: index, Overlap: overlap, Weak: composerScore.GetValueOrDefault(id));
            })
            .Where(item => item.Overlap > 0 || item.Weak > 0)
            .OrderByDescending(item => item.Overlap).ThenByDescending(item => item.Weak).ThenBy(item => item.Index)
            .Select(item => item.Id).Prepend(routed).OfType<string>()
            .Take(MaxNearestSubjects)
            .Select(id => new NearestSubject(id, SubjectTitle(help, id)))
            .ToArray();
        var phrase = EchoedGoal(goal);
        var missing = nearest.Length == 0
            ? Template(help, "composer-no-match-no-subject", DefaultMissingNoSubject, phrase, null)
            : Template(help, "composer-no-match", DefaultMissing, phrase, nearest[0].Title);
        var next = Template(help, "composer-no-match-next", DefaultNext, phrase, null).Replace("{missing}", missing);
        return new FindComposerResponse("goal", [], true, nearest,
            weak.Take(MaxNearestComposers).Select(item => new NearestComposer(item.Candidate.Verb, Math.Round(item.Score, 2))).ToArray(),
            missing, next);
    }

    // The goal made safe to quote in a sentence: one line, no quote, backtick or placeholder, and capped.
    internal static string EchoedGoal(string goal)
    {
        var plain = new string(goal.Where(c => c is not ('"' or '`' or '{' or '}' or '\u201C' or '\u201D')).ToArray());
        var phrase = string.Join(' ', plain.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .TrimEnd('?', '.', '!');
        return phrase.Length > MaxEchoedGoalLength ? phrase[..(MaxEchoedGoalLength - 1)].TrimEnd() + "\u2026" : phrase;
    }

    private static string Template(HelpCatalog help, string code, string fallback, string goal, string? subject) =>
        (help.RefusalWhy(code) ?? fallback).Replace("{goal}", goal).Replace("{subject}", subject ?? string.Empty);

    private const string DefaultMissing = "Motif has no Composer for \"{goal}\" yet; the nearest Subject is {subject}.";
    private const string DefaultMissingNoSubject = "Motif has no Composer for \"{goal}\" yet.";
    private const string DefaultNext = "Tell the person what is missing: {missing}";

    private static CommandOutcome<FindComposerResponse> Refuse(string code, string message) =>
        CommandOutcome<FindComposerResponse>.Refused(new Refusal(code, FailureReason.InvalidArgument, message));

    // A short stem, minus a plural s, lets "rules" meet "rule" and "analysed" meet "analyses".
    private static IEnumerable<string> Words(string text) =>
        Regex.Split(text.ToLowerInvariant(), "[^\\p{L}\\p{N}]+")
            .Where(word => word.Length >= MinWordLetters && !StopWords.Contains(word))
            .Select(Stem)
            .Distinct(StringComparer.Ordinal);

    // Each stem with the goal's own first spelling of it, so a result can name the words the person used.
    private static Dictionary<string, string> SpelledWords(string text)
    {
        var spelled = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var word in Regex.Split(text.ToLowerInvariant(), "[^\\p{L}\\p{N}]+")
                     .Where(word => word.Length >= MinWordLetters && !StopWords.Contains(word)))
            spelled.TryAdd(Stem(word), word);
        return spelled;
    }

    private static string Stem(string word)
    {
        if (word.Length > 3 && word[^1] == 's' && word[^2] is not ('s' or 'u' or 'i')) word = word[..^1];
        return word.Length > StemLength ? word[..StemLength] : word;
    }

    private sealed record Candidate(
        string Verb, IReadOnlyList<string> Subjects, IntentShape? Shape, HelpEntry? Entry)
    {
        public static Candidate From(IComposerDefinition definition, HelpCatalog help) =>
            new(definition.Verb, definition.Subjects, definition.Shape, help.Find(HelpEntryKind.Command, definition.Verb));

        public SearchFields Fields(HelpCatalog help) => new(
            Words(string.Join(' ', Entry?.Goals ?? [])).ToHashSet(StringComparer.Ordinal),
            Words(Entry?.Title ?? string.Empty).ToHashSet(StringComparer.Ordinal),
            Words(Entry?.Description ?? string.Empty).ToHashSet(StringComparer.Ordinal),
            Words(string.Join(' ', Subjects.Select(id => SubjectTitle(help, id)))).ToHashSet(StringComparer.Ordinal));

        // The Subject whose title shares the most words with the goal, else the composer's first Subject.
        public string BestSubject(HashSet<string> words, HelpCatalog help) => Subjects
            .Select((id, index) => (Id: id, Index: index, Hits: Words(SubjectTitle(help, id)).Count(words.Contains)))
            .OrderByDescending(item => item.Hits).ThenBy(item => item.Index).First().Id;

        public ComposerMatch ToMatch(bool detailed, HelpCatalog help, string subjectId)
        {
            var schema = Shape?.ToJsonSchema();
            return new ComposerMatch(
                Verb,
                Entry?.Title ?? Verb,
                Entry?.Description ?? string.Empty,
                Subjects,
                schema,
                schema is null ? "help" : "shape",
                detailed ? FirstExample(Entry?.HelpPage) : null,
                detailed ? SubjectPage(help, subjectId) : null);
        }
    }

    // The words of one composer's searchable text, by where they occur.
    private sealed record SearchFields(
        HashSet<string> Goals, HashSet<string> Title, HashSet<string> Description, HashSet<string> SubjectTitles)
    {
        // The goal's non-generic stems that the composer's example goals or title contain.
        public HashSet<string> Covered(HashSet<string> words) =>
            words.Where(word => !GenericStems.Contains(word) && (Goals.Contains(word) || Title.Contains(word))).ToHashSet(StringComparer.Ordinal);

        public bool Mentions(string word) =>
            Goals.Contains(word) || Title.Contains(word) || Description.Contains(word) || SubjectTitles.Contains(word);

        // A word found in few composers says more than one found in many, so each hit is scaled by its rarity.
        public double Score(HashSet<string> words, IReadOnlyDictionary<string, double> rarity) => words.Sum(word =>
            rarity[word] * ((Goals.Contains(word) ? GoalWeight : 0) + (Title.Contains(word) ? TitleWeight : 0) +
                (Description.Contains(word) ? DescriptionWeight : 0) + (SubjectTitles.Contains(word) ? SubjectTitleWeight : 0)));
    }

    private static string SubjectTitle(HelpCatalog help, string id) =>
        help.Subjects.FirstOrDefault(subject => subject.Id == id)?.Title ?? id.Replace('-', ' ');

    private static string? FirstExample(string? page) =>
        page is not null && Example.Match(page) is { Success: true } match ? match.Groups["body"].Value : null;

    private static ComposerSubjectPage SubjectPage(HelpCatalog help, string id)
    {
        var page = help.Subjects.FirstOrDefault(subject => subject.Id == id)?.HelpPage;
        var excerpt = page?.Split("\n\n").Select(block => block.Trim())
            .FirstOrDefault(block => block.Length > 0 && !block.StartsWith('#'));
        return new ComposerSubjectPage(id, SubjectTitle(help, id), excerpt);
    }
}
