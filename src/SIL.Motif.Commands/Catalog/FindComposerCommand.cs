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
public sealed record ComposerMatch(
    string Verb,
    string Title,
    string Description,
    IReadOnlyList<string> Subjects,
    JsonNode? IntentSchema,
    string SchemaSource,
    string? Example = null,
    ComposerSubjectPage? SubjectPage = null);

/// <summary>The composers a search found, best first.</summary>
/// <param name="Mode"><c>goal</c> or <c>subject</c>, whichever the request used.</param>
/// <param name="Results">At most five composers in a goal search, every composer of the Subject in a Subject search.</param>
public sealed record FindComposerResponse(string Mode, IReadOnlyList<ComposerMatch> Results);

/// <summary>
/// Searches the composer registry by goal or by Subject. The ranking is a case-insensitive overlap of the goal's
/// words with each composer's example goals (weight 3), title (2) and description (1), and a word that appears in a
/// Subject title the composer serves counts (2). Each word's hits are scaled by how few composers mention it, so a
/// rare word outweighs a common one. Reading only: nothing is staged and no project is opened.
/// </summary>
public static class FindComposerCommand
{
    private const int MaxGoalResults = 5;
    private const int GoalWeight = 3;
    private const int TitleWeight = 2;
    private const int DescriptionWeight = 1;
    private const int SubjectTitleWeight = 2;
    private const int StemLength = 6;

    private static readonly Lazy<HelpCatalog> Help = new(() => HelpCatalog.Load());

    private static readonly Regex Example = new(
        "```(?:json|sh)[ \\t]*\\n(?<body>.*?)\\n```", RegexOptions.Singleline | RegexOptions.CultureInvariant);

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
            if (!ComposerSubjects.All.Contains(subject, StringComparer.Ordinal))
                return Refuse(RefusalCodes.ComposerUnknownSubject,
                    $"No composer serves '{subject}'. Subjects are: {string.Join(", ", ComposerSubjects.All)}.");
            var serving = candidates.Where(candidate => candidate.Subjects.Contains(subject, StringComparer.Ordinal))
                .Select(candidate => candidate.ToMatch(detailed.Value, help, subject));
            return CommandOutcome<FindComposerResponse>.Success(new FindComposerResponse("subject", serving.ToArray()));
        }

        var words = Words(goal!).ToHashSet(StringComparer.Ordinal);
        var fields = candidates.Select(candidate => candidate.Fields(help)).ToArray();
        var rarity = words.ToDictionary(word => word, word =>
            Math.Log(1 + (double)candidates.Length / Math.Max(1, fields.Count(field => field.Mentions(word)))),
            StringComparer.Ordinal);
        var ranked = candidates
            .Select((candidate, index) => (Candidate: candidate, Score: fields[index].Score(words, rarity)))
            .Where(item => item.Score > 0)
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Candidate.Verb, StringComparer.Ordinal)
            .Take(MaxGoalResults)
            .Select(item => item.Candidate.ToMatch(detailed.Value, help, item.Candidate.BestSubject(words, help)));
        return CommandOutcome<FindComposerResponse>.Success(new FindComposerResponse("goal", ranked.ToArray()));
    }

    private static CommandOutcome<FindComposerResponse> Refuse(string code, string message) =>
        CommandOutcome<FindComposerResponse>.Refused(new Refusal(code, FailureReason.InvalidArgument, message));

    // A short stem, minus a plural s, lets "rules" meet "rule" and "analysed" meet "analyses".
    private static IEnumerable<string> Words(string text) =>
        Regex.Split(text.ToLowerInvariant(), "[^\\p{L}\\p{N}]+")
            .Where(word => word.Length >= 3 && !StopWords.Contains(word))
            .Select(Stem)
            .Distinct(StringComparer.Ordinal);

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
