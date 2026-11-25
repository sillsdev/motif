using System.Collections.Concurrent;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Contract.Query.Readings;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Baselines;

namespace SIL.Motif.Commands.Queries;

/// <summary>Which entry owns an analysis and what class of morph it is, read from a project's lexicon.</summary>
internal sealed class MorphCatalog
{
    private readonly Dictionary<string, (string Entry, string Kind, string? Gloss)> _byMsa;
    private readonly HashSet<string> _entries;

    private MorphCatalog(Dictionary<string, (string, string, string?)> byMsa, HashSet<string> entries)
    {
        _byMsa = byMsa;
        _entries = entries;
    }

    /// <summary>The number of analyses the catalog knows.</summary>
    public int Analyses => _byMsa.Count;

    /// <summary>The owner and class of an analysis; null when the project has no such analysis.</summary>
    public (string Entry, string Kind, string? Gloss)? Of(string msa) => _byMsa.TryGetValue(msa, out var found) ? found : null;

    /// <summary>Whether the project has an entry with this id.</summary>
    public bool HasEntry(string entry) => _entries.Contains(entry);

    /// <summary>Builds a catalog from a catalog of analyses; a test supplies its own.</summary>
    internal static MorphCatalog From(IEnumerable<(string Msa, string Entry, string Kind)> rows) =>
        From(rows.Select(row => (row.Msa, row.Entry, row.Kind, (string?)null)));

    /// <summary>Builds a catalog whose analyses also carry the gloss of their entry.</summary>
    internal static MorphCatalog From(IEnumerable<(string Msa, string Entry, string Kind, string? Gloss)> rows)
    {
        var map = new Dictionary<string, (string, string, string?)>(StringComparer.OrdinalIgnoreCase);
        var entries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (msa, entry, kind, gloss) in rows) { map[msa] = (entry, kind, gloss); entries.Add(entry); }
        return new MorphCatalog(map, entries);
    }

    /// <summary>Reads every analysis of every entry of the project; an analysis belongs to exactly one entry, so the map is exact.</summary>
    public static MorphCatalog FromProject(LcmCache cache) => From(
        from entry in cache.ServiceLocator.GetInstance<ILexEntryRepository>().AllInstances()
        from msa in entry.MorphoSyntaxAnalysesOC
        select (msa.Guid.ToString("D"), entry.Guid.ToString("D"), KindOf(msa.ClassName), GlossOf(entry, msa)));

    /// <summary>The gloss of the sense that uses the analysis, else of the first sense; null when none.</summary>
    private static string? GlossOf(ILexEntry entry, IMoMorphSynAnalysis msa)
    {
        var sense = entry.SensesOS.FirstOrDefault(candidate => candidate.MorphoSyntaxAnalysisRA == msa) ?? entry.SensesOS.FirstOrDefault();
        var text = sense?.Gloss?.BestAnalysisAlternative?.Text;
        return string.IsNullOrWhiteSpace(text) || text == "***" ? null : text;
    }

    private static string KindOf(string className) => className switch
    {
        "MoStemMsa" => MorphKinds.Stem,
        "MoDerivAffMsa" => MorphKinds.Derivational,
        "MoInflAffMsa" => MorphKinds.Inflectional,
        _ => MorphKinds.Affix,
    };

    /// <summary>
    /// Opens the project and reads its catalog. The project is the one named, else the grammar when it is a FieldWorks project file;
    /// null when neither is, or the project cannot be opened.
    /// </summary>
    public static MorphCatalog? TryLoad(string? projectPath, string? grammarPath, out string? failure)
    {
        failure = null;
        var path = !string.IsNullOrWhiteSpace(projectPath) ? projectPath
            : grammarPath is { } grammar && grammar.EndsWith(".fwdata", StringComparison.OrdinalIgnoreCase) ? grammar : null;
        if (path is null) return null;
        try
        {
            using var read = BaselineReadCache.Open(path);
            return FromProject(read.Cache);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failure = $"The project '{path}' could not be opened to name the morphs' entries: {exception.Message}";
            return null;
        }
    }
}

/// <summary>The cached corpus parse as <see cref="IReadingSource"/>: PanGloss's morphs, each given its entry and class from the catalog when there is one.</summary>
internal sealed class ParsedReadings(RealizesParseIndex index, MorphCatalog? catalog) : IReadingSource
{
    private readonly ConcurrentDictionary<string, WordReadings?> _cache = new(StringComparer.Ordinal);

    /// <summary>The parse index behind it.</summary>
    public RealizesParseIndex Index { get; } = index;

    private HashSet<string>? _ids;
    private IReadOnlyList<string>? _words;

    /// <summary>Whether a form or analysis id appears in any reading of the parse.</summary>
    public bool Mentions(string id)
    {
        if (_ids is null)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var word in _words ?? [])
                if (Get(word) is { } entry)
                    foreach (var morph in entry.Readings.SelectMany(reading => reading.Morphs)) { ids.Add(morph.Form); ids.Add(morph.Msa); }
            _ids = ids;
        }
        return _ids.Contains(id);
    }

    /// <summary>Whether morphs carry entry ids and classes.</summary>
    public bool HasCatalog => catalog is not null;

    public WordReadings? Get(string word) => _cache.GetOrAdd(word, key => Index.Get(key) is { } indexed ? Convert(indexed) : null);

    private WordReadings Convert(IndexedWord indexed)
    {
        var state = indexed.Status switch
        {
            IndexedStatus.Capped => ReadingStates.Capped,
            IndexedStatus.TimedOut => ReadingStates.TimedOut,
            IndexedStatus.Unsegmentable => ReadingStates.Unsegmentable,
            IndexedStatus.Failed => ReadingStates.Failed,
            _ => indexed.Analyses.Count > 0 ? ReadingStates.Parsed : ReadingStates.None,
        };
        return new WordReadings(state, [.. indexed.Analyses.Select(analysis => new WordReading(
            [.. analysis.Morphs.Select(morph => ToMorph(morph, analysis.Problem is not null))], analysis.Signature))]);
    }

    private ReadingMorph ToMorph(IndexedMorph morph, bool untrusted)
    {
        var known = catalog?.Of(morph.Msa);
        var runs = untrusted ? null : morph.Runs;
        var placed = runs is { Count: > 0 };
        return new ReadingMorph(morph.Form, morph.Msa, placed ? string.Concat(runs!.Select(run => run.Text)) : "",
            placed ? runs![0].Start : -1, placed ? runs![^1].Start + runs[^1].Text.Length : -1, morph.InflType,
            morph.Entry ?? known?.Entry, known?.Kind, morph.Signature, known?.Gloss);
    }

    /// <summary>What the parse covers, and whether its counts can be trusted.</summary>
    public ReadingsReport Report(IReadOnlyList<string> words, bool classesMatter)
    {
        _words = words;
        int types = 0, capped = 0, timedOut = 0, failed = 0, unsegmentable = 0;
        var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in words)
        {
            types++;
            if (catalog is not null && Get(word) is { } parsed)
                foreach (var morph in parsed.Readings.SelectMany(reading => reading.Morphs))
                    if (morph.Kind is null && catalog.Of(morph.Msa) is null) unknown.Add(morph.Msa);
            switch (Get(word)?.State ?? ReadingStates.Failed)
            {
                case ReadingStates.Capped: capped++; break;
                case ReadingStates.TimedOut: timedOut++; break;
                case ReadingStates.Failed: failed++; break;
                case ReadingStates.Unsegmentable: unsegmentable++; break;
            }
        }
        var complete = capped + timedOut + failed + unknown.Count == 0;
        var notes = new List<string>();
        if (!complete)
            notes.Add($"{capped} words stopped at the parser's work limit, {timedOut} at its word timeout and {failed} got no answer, so their reading counts, cause groups and retire estimates may be low or missing.");
        if (unknown.Count > 0)
            notes.Add($"{unknown.Count} analyses in the parse are not in the project's lexicon (the grammar and the project do not match); their readings are not attributed to any entry, so entry: counts may be low.");
        if (catalog is null && classesMatter)
            notes.Add("No project named the morphs: entry ids and morph classes are absent, so entry: ids are refused.");
        return new ReadingsReport(complete, types, capped, timedOut, failed, unsegmentable, unknown.Count, catalog is null ? "unavailable" : "project",
            notes.Count == 0 ? null : string.Join(" ", notes));
    }
}

/// <summary>Refusals of the readings queries.</summary>
internal static class ReadingsRefusals
{
    public static QueryRefusalException NeedsGrammar(string what) => new(new Refusal(RefusalCodes.QueryReadingsNeedsGrammar, FailureReason.InvalidArgument,
        $"{what} reads the readings of each word, which come from parsing the corpus. Pass --grammar <path> (a grammar or the .fwdata).",
        new Dictionary<string, string>(StringComparer.Ordinal) { ["flag"] = "--grammar" }));

    public static QueryRefusalException NeedsProject(ReadingTarget target, string? failure) => new(new Refusal(RefusalCodes.QueryReadingsNeedsProject,
        FailureReason.InvalidArgument,
        $"{target} names an entry, and PanGloss's batch gives each morph its form and analysis ids but not its entry. An analysis belongs to exactly one entry, so " +
        "pass --project <fwdata> (or a .fwdata as --grammar) to map them, or name the morpheme as msa:<id> or form:<id>." + (failure is null ? "" : " " + failure),
        new Dictionary<string, string>(StringComparer.Ordinal) { ["flag"] = "--project", ["morpheme"] = target.ToString() }));
}

/// <summary>Further refusals of the readings queries.</summary>
internal static class ReadingsIdRefusals
{
    public static QueryRefusalException CausesNeedProject(string? failure) => new(new Refusal(RefusalCodes.QueryReadingsNeedsProject, FailureReason.InvalidArgument,
        "--group-by cause tells a harmless homophone from a different root only with each morph's class, which PanGloss's batch does not give and the project's lexicon does. " +
        "Pass --project <fwdata> (or a .fwdata as --grammar)." + (failure is null ? "" : " " + failure),
        new Dictionary<string, string>(StringComparer.Ordinal) { ["flag"] = "--project" }));

    public static QueryRefusalException Unknown(ReadingTarget target, bool hasProject) => new(new Refusal(RefusalCodes.QueryRetireIdUnknown, FailureReason.InvalidArgument,
        target.Kind == "entry"
            ? $"{target} is not an entry of the project, so retiring it would silently match nothing."
            : $"{target} is not the form id or analysis id of any morph in the corpus parse" + (hasProject
                ? " and not an entry of the project, so it would silently match nothing."
                : "; as an entry id it would need --project <fwdata> to be mapped to its analyses. Nothing was searched but form and analysis ids."),
        new Dictionary<string, string>(StringComparer.Ordinal) { ["morpheme"] = target.ToString() }));
}

/// <summary>
/// What retiring morphemes would do to a set of words. The seam is this interface: today the answer is read from the cached reading sets
/// (<see cref="ReadingSetRetire"/>); PanGloss#472's <c>batch --retire</c> re-parses the affected words and will implement it exactly.
/// </summary>
internal interface IRetireEstimator
{
    /// <summary>The answer for the words of <paramref name="words"/>, each with its token count.</summary>
    WhatIfReport Estimate(IReadingSource readings, IReadOnlyList<(string Word, long Tokens)> words, IReadOnlyList<ReadingTarget> retire, bool complete, int limit);
}

/// <summary>The estimate from the cached reading sets: retiring cannot add a reading, but a capped parse may hide ones that would remain.</summary>
internal sealed class ReadingSetRetire : IRetireEstimator
{
    public const string MethodName = "reading-set difference over the cached corpus parse";
    public const string Caution = "estimate: confirm with a Trial";

    public WhatIfReport Estimate(IReadingSource readings, IReadOnlyList<(string Word, long Tokens)> words, IReadOnlyList<ReadingTarget> retire, bool complete, int limit)
    {
        var affected = new List<WhatIfWord>();
        var capped = new List<WhatIfWord>();
        var failed = new List<(string Word, long Tokens)>();
        foreach (var (word, tokens) in words)
        {
            if (readings.Get(word) is not { } entry || entry.State == ReadingStates.Failed) { failed.Add((word, tokens)); continue; }
            var count = ViaCount.Of(entry, retire);
            if (count.Via == 0) continue;
            var kept = entry.Readings.Where(reading => !reading.Morphs.Any(morph => retire.Any(target => target.Matches(morph)))).ToList();
            var line = new WhatIfWord(word, tokens, count.Readings, count.Via, count.Other, count.OnlyVia, !count.OnlyVia, entry.MayBeIncomplete,
                count.OnlyVia ? null : [.. kept.Select(reading => reading.Signature ?? string.Join("+", reading.Morphs.Select(morph => morph.Text.Length > 0 ? morph.Text : morph.Msa[..8])))]);
            (entry.MayBeIncomplete ? capped : affected).Add(line);
        }
        TypesTokens Sum(IEnumerable<WhatIfWord> set) { var list = set.ToList(); return new TypesTokens(list.Count, list.Sum(item => item.Tokens)); }
        var ordered = affected.OrderByDescending(item => item.Lost).ThenByDescending(item => item.Tokens).ThenBy(item => item.Word, StringComparer.Ordinal);
        return new WhatIfReport(true, false, MethodName, Caution, [.. retire.Select(target => target.ToString())], complete,
            Sum(affected), Sum(affected.Where(item => item.Lost)), Sum(affected.Where(item => item.Reduced)), Sum(capped),
            new TypesTokens(failed.Count, failed.Sum(item => item.Tokens)), [.. ordered.Take(limit)]);
    }
}
