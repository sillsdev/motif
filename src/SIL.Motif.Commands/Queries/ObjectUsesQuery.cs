using SIL.Motif.Host.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Projection;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Reads, from the stored Assessment, the Selection's words that use an object, the words it ran in, and the
/// morphemes a set of words shares. Every match is by exact identity, never by spelling or similarity.
/// </summary>
public static class ObjectUsesQuery
{
    /// <summary>
    /// Answers <paramref name="request"/> from the Assessment matching the current Baseline and Selection, with what
    /// the Baseline's copy of the project says about the object. A ref without a timing key takes the one its
    /// grammatical info or entry gives, so a morpheme from a word row finds the words it ran in.
    /// </summary>
    public static CommandOutcome<ObjectUsesResponse> Query(ObjectUsesRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Ref is null && request.Words is not { Count: > 0 } ||
            request.Ref is { } named && named.AllomorphId is null && named.GrammaticalInfoId is null &&
            (named.TimingKind is null || named.TimingKey is null))
            return CommandOutcome<ObjectUsesResponse>.Refused(new Refusal(
                "uses.invalid-request", FailureReason.InvalidArgument,
                "Name an allomorph, a grammatical info or a timing kind and key, or some words."));

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var current = CurrentEvidenceQuery.ReadCurrentEvidence(database, project);
            if (!current.Succeeded) return CommandOutcome<ObjectUsesResponse>.Refused(current.Refusal!);
            var snapshot = current.Value!;
            if (snapshot.Assessment is not { } assessment || snapshot.MatchingAssessment is not { } record)
                return CommandOutcome<ObjectUsesResponse>.Refused(new Refusal(
                    "uses.no-assessment", FailureReason.NotFound,
                    "No stored Assessment matches the current Baseline and default Selection."));
            var timings = snapshot.EffectiveObjectTimings;
            var facts = request.Ref is { } asked && snapshot.Baseline is { } baseline
                ? FactsOf(asked, baseline, project) : null;
            return CommandOutcome<ObjectUsesResponse>.Success(
                Read(assessment.Words, timings, WithTimingKey(request.Ref, facts), request.Words) with
                {
                    AssessmentId = record.AssessmentId,
                    WordOrigins = snapshot.EffectiveWords.Where(word => word.Origin is not null).ToDictionary(
                        word => word.Word, word => word.Origin!, StringComparer.Ordinal),
                    IsStale = snapshot.Freshness == EvidenceFreshness.Stale,
                    Facts = facts,
                });
        });
    }

    /// <summary>
    /// What <paramref name="words"/> and <paramref name="timings"/> say about <paramref name="reference"/> and
    /// <paramref name="wordSet"/>; each section is <see langword="null"/> when nothing asked for it.
    /// </summary>
    public static ObjectUsesResponse Read(IReadOnlyList<AssessmentWordResult> words,
        IReadOnlyList<AssessmentObjectTiming> timings, ObjectUseRef? reference, IReadOnlyList<string>? wordSet)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(timings);
        var asked = wordSet?.Select(Normalize).Where(word => word.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var held = words.Select(word => word.Word).ToHashSet(StringComparer.Ordinal);
        return new ObjectUsesResponse(string.Empty)
        {
            Ref = reference,
            Uses = reference is { } uses && (uses.AllomorphId is not null || uses.GrammaticalInfoId is not null)
                ? UsesOf(words, uses) : null,
            RanIn = reference is { TimingKind: not null, TimingKey: not null } ran ? RanIn(words, timings, ran) : null,
            Shared = asked is null ? null : SharedBy(words, asked),
            UnknownWords = asked?.Where(word => !held.Contains(word)).ToArray() ?? [],
        };
    }

    /// <summary>
    /// The words with a stored analysis, other than a disapproved one, whose morphs use the ref's ids, and how many
    /// words use them only in a disapproved analysis.
    /// </summary>
    public static ObjectUseWords UsesOf(IReadOnlyList<AssessmentWordResult> words, ObjectUseRef reference)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(reference);
        if (reference.AllomorphId is null && reference.GrammaticalInfoId is null) return new ObjectUseWords([], []);
        bool Uses(ParserReadingMorph morph) =>
            (reference.AllomorphId is null || SameId(reference.AllomorphId, morph.AllomorphId)) &&
            (reference.GrammaticalInfoId is null || SameId(reference.GrammaticalInfoId, morph.GrammaticalInfoId));
        var used = words.Where(word => MorphsUsedBy(word).Any(Uses)).ToArray();
        var disapprovedOnly = words.Except(used).Count(word => word.StoredAnalyses
            .Where(analysis => analysis.StoredAnalysisOpinion == ReadingGrade.Disapproved)
            .SelectMany(analysis => analysis.Morphs).Any(Uses));
        return Split(used.Select(word => new ObjectUseWord(WordRowProjection.Of(word))).ToArray()) with
        {
            NotCountingDisapproved = disapprovedOnly,
        };
    }

    /// <summary>
    /// The words whose stored per-word timings record the ref's kind and key with a call or some time, each with the
    /// calls and self time summed over directions.
    /// </summary>
    public static ObjectUseWords RanIn(IReadOnlyList<AssessmentWordResult> words,
        IReadOnlyList<AssessmentObjectTiming> timings, ObjectUseRef reference)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(timings);
        ArgumentNullException.ThrowIfNull(reference);
        var byWord = timings
            .Where(row => StringComparer.Ordinal.Equals(row.Kind, reference.TimingKind) &&
                SameId(reference.TimingKey, row.Key) && (row.Attempts is not 0 || row.ElapsedNs is not (null or 0)))
            .GroupBy(row => row.Word, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        return Split(words.Where(word => byWord.ContainsKey(word.Word)).Select(word =>
        {
            var rows = byWord[word.Word];
            return new ObjectUseWord(WordRowProjection.Of(word))
            {
                Calls = rows.Any(row => row.Attempts is not null) ? rows.Sum(row => row.Attempts ?? 0) : null,
                ElapsedNs = rows.Any(row => row.ElapsedNs is not null) ? rows.Sum(row => row.ElapsedNs ?? 0) : null,
            };
        }).ToArray());
    }

    /// <summary>
    /// The morphemes at least two of <paramref name="wordSet"/> use, most words first and then in the order the
    /// words first use them. A morph that names no allomorph or no grammatical info cannot be matched, so is left out.
    /// </summary>
    public static IReadOnlyList<SharedMorpheme> SharedBy(IReadOnlyList<AssessmentWordResult> words,
        IReadOnlyList<string> wordSet)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(wordSet);
        var asked = wordSet.Select(Normalize).ToHashSet(StringComparer.Ordinal);
        var morphemes = new List<(string Key, ParserReadingMorph Morph, List<string> Words)>();
        var indexOf = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var word in words.Where(word => asked.Contains(word.Word)))
            foreach (var morph in MorphsUsedBy(word))
            {
                if (morph.AllomorphId is not { } allomorph || morph.GrammaticalInfoId is not { } grammaticalInfo) continue;
                var key = IdKey(allomorph) + "/" + IdKey(grammaticalInfo);
                if (!indexOf.TryGetValue(key, out var index))
                {
                    indexOf.Add(key, morphemes.Count);
                    morphemes.Add((key, morph, [word.Word]));
                }
                else if (morphemes[index].Words[^1] != word.Word) morphemes[index].Words.Add(word.Word);
            }
        return morphemes.Where(item => item.Words.Count >= 2)
            .Select((item, order) => (item, order)).OrderByDescending(pair => pair.item.Words.Count)
            .ThenBy(pair => pair.order).Select(pair => new SharedMorpheme(pair.item.Morph, pair.item.Words)).ToArray();
    }

    /// <summary>
    /// <paramref name="reference"/> with the timing key <paramref name="facts"/> gives, when the ref names none;
    /// a key the caller gave is kept.
    /// </summary>
    public static ObjectUseRef? WithTimingKey(ObjectUseRef? reference, ObjectFacts? facts) =>
        reference is { TimingKind: null or "", } or { TimingKey: null or "" } && facts?.TimingKey is { } key
            ? reference with { TimingKind = key.Kind, TimingKey = key.Key }
            : reference;

    // The Baseline's own copy, opened as a scratch: reading it can never change the project the linguist edits.
    private static ObjectFacts? FactsOf(ObjectUseRef reference, BaselineRecord baseline, ProjectLocator project)
    {
        using var reader = BaselineReadCache.Open(baseline.FwDataPath);
        var cache = reader.Cache;
        var projectName = Path.GetFileNameWithoutExtension(project.FullFwDataPath);
        return ObjectFactsReader.Read(cache, reference, found =>
            FieldWorksLinks.TargetFor(cache, found) is { } target
                ? new TraceFieldWorksTarget(target.Tool, FieldWorksLinks.ToolName(target.Tool),
                    target.ObjectId.ToString("D"), FieldWorksLinks.ForTarget(projectName, target)!)
                : null);
    }

    // A disapproved analysis is one the linguist says the word is not, so it is no use of its morphs.
    private static IEnumerable<ParserReadingMorph> MorphsUsedBy(AssessmentWordResult word) => word.StoredAnalyses
        .Where(analysis => analysis.StoredAnalysisOpinion != ReadingGrade.Disapproved)
        .SelectMany(analysis => analysis.Morphs);

    internal static ObjectUseWords Split(IReadOnlyList<ObjectUseWord> words) => new(words, words
        .GroupBy(word => (word.Row.Meaning, word.Row.Tone))
        .Select(group => new ObjectUseMeaning(group.Key.Meaning, group.Key.Tone, group.Count()))
        .OrderByDescending(meaning => meaning.Words).ToArray());

    private static bool SameId(string? left, string? right) =>
        left is not null && right is not null && StringComparer.Ordinal.Equals(IdKey(left), IdKey(right));

    // A GUID compares as a GUID, whatever its case or braces; any other key compares exactly.
    private static string IdKey(string id) => Guid.TryParse(id, out var guid) ? guid.ToString("D") : id;

    private static string Normalize(string word) => word.Trim().Normalize(System.Text.NormalizationForm.FormD);
}
