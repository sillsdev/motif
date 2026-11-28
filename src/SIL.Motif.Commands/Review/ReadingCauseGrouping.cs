using System.Text;
using SIL.LCModel;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Baselines;

namespace SIL.Motif.Commands.Review;

/// <summary>What the Baseline can say about the causes a Reading names, and how often a word occurs in it.</summary>
internal interface IReadingCauseSource
{
    /// <summary>The Baseline's occurrences of <paramref name="word"/>, or zero.</summary>
    long Tokens(string word);

    /// <summary>The headword of the entry, or null when it cannot be named.</summary>
    string? EntryLabel(string entryId);

    /// <summary>Whether the Baseline holds any occurrence counts.</summary>
    bool HasTokens { get; }

    /// <summary>Whether slots can be read at all; false when the Baseline's project could not be opened.</summary>
    bool CanNameSlots { get; }

    /// <summary>
    /// The slots of the analysis, empty when it names none, one <see cref="ReadingDeltaGroupings.Derivational"/> entry for a
    /// derivational affix, or null when it is not in the Baseline.
    /// </summary>
    IReadOnlyList<(string Id, string? Name)>? Slots(string msaId);
}

/// <summary>Groups the Reading changes of the chosen forms by the entry or slot each changed Reading names.</summary>
internal static class ReadingCauseGrouping
{
    private const int Samples = 5;

    internal static ReadingDeltaGroups Group(string by, IEnumerable<ReadingDeltaForm> forms, IReadingCauseSource source,
        int offset, int limit)
    {
        var groups = new Dictionary<string, Accumulator>(StringComparer.Ordinal);
        foreach (var form in forms)
        {
            Tally(by, form, form.Retired, source, groups, retired: true);
            Tally(by, form, form.Added, source, groups, retired: false);
        }
        var rows = groups.Select(pair => pair.Value.ToGroup(pair.Key, source))
            .OrderByDescending(group => group.Tokens).ThenBy(group => group.Key, StringComparer.Ordinal).ToArray();
        var page = rows.Skip(offset).Take(limit).ToArray();
        return new ReadingDeltaGroups(by, rows.Length, page,
            SIL.Motif.Contract.Responses.ResultPaging.NextCursor(offset, page.Length, rows.Length));
    }

    private static void Tally(string by, ReadingDeltaForm form, IReadOnlyList<(ReadingSignature Signature, long Count)> readings,
        IReadingCauseSource source, Dictionary<string, Accumulator> groups, bool retired)
    {
        foreach (var (signature, count) in readings)
            foreach (var key in Causes(by, signature, source))
            {
                if (!groups.TryGetValue(key.Id, out var group)) groups[key.Id] = group = new Accumulator(key.Label);
                group.Add(form.Word, count, retired, source);
            }
    }

    // The distinct causes one Reading names; a Reading that names a cause twice counts once.
    private static IEnumerable<(string Id, string? Label)> Causes(string by, ReadingSignature signature, IReadingCauseSource source)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var morpheme in signature.Morphemes)
        {
            if (by == ReadingDeltaGroupings.Entry)
            {
                if (morpheme.EntryId is { } entry && seen.Add(entry)) yield return (entry, source.EntryLabel(entry));
                continue;
            }
            if (morpheme.IsStem) continue;
            var slots = morpheme.MsaId is { } msa ? source.Slots(msa) : null;
            if (slots is null || slots.Count == 0)
            {
                var key = slots is null ? ReadingDeltaGroupings.UnknownMsa : ReadingDeltaGroupings.NoSlot;
                if (seen.Add(key)) yield return (key, null);
                continue;
            }
            foreach (var slot in slots)
                if (seen.Add(slot.Id)) yield return (slot.Id, slot.Name);
        }
    }

    /// <summary>The spelling words are counted by: lower-cased, then composed, as the Baseline's token counts are keyed.</summary>
    internal static string Fold(string word) => word.ToLowerInvariant().Normalize(NormalizationForm.FormC);

    private sealed class Accumulator(string? label)
    {
        // Folded spelling to its first-seen spelling and token count, so case variants count once.
        private readonly Dictionary<string, (string Word, long Tokens)> _words = new(StringComparer.Ordinal);
        private long _retired, _added;

        public string? Label { get; } = label;

        public void Add(string word, long count, bool retired, IReadingCauseSource source)
        {
            if (retired) _retired += count; else _added += count;
            if (!_words.ContainsKey(Fold(word))) _words[Fold(word)] = (word, source.Tokens(word));
        }

        public ReadingDeltaGroup ToGroup(string key, IReadingCauseSource source) => new(key, Label, _retired, _added,
            _words.Count, _words.Values.Sum(item => item.Tokens),
            [.. _words.Values.OrderByDescending(item => item.Tokens).ThenBy(item => item.Word, StringComparer.Ordinal)
                .Take(Samples).Select(item => item.Word)]);
    }
}

/// <summary>The production source: token counts from the current Baseline's summary, names from its project copy.</summary>
internal sealed class BaselineCauseSource : IReadingCauseSource, IDisposable
{
    private readonly Dictionary<string, long> _tokens = new(StringComparer.Ordinal);
    private BaselineReadCache? _reader;

    private BaselineCauseSource() { }

    /// <summary>Opens the source; token counts and names the Baseline cannot supply are left empty.</summary>
    internal static BaselineCauseSource Open(MotifDatabase database, string projectKey)
    {
        var source = new BaselineCauseSource();
        try
        {
            if (new BaselineRepository(database).GetCurrentEvidence(projectKey) is not { } evidence) return source;
            foreach (var text in evidence.Summary.Texts)
                foreach (var (word, count) in text.OccurrencesByWord)
                {
                    var key = Key(word);
                    source._tokens[key] = source._tokens.GetValueOrDefault(key) + count;
                }
            if (File.Exists(evidence.Baseline.FwDataPath) || Directory.Exists(evidence.Baseline.FwDataPath))
                source._reader = BaselineReadCache.Open(evidence.Baseline.FwDataPath);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or BaselineSummaryMissingException
            or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            // Without a Baseline the counts and names stay empty; the read says so.
        }
        return source;
    }

    public bool HasTokens => _tokens.Count > 0;

    public bool CanNameSlots => _reader is not null;

    public long Tokens(string word) => _tokens.GetValueOrDefault(Key(word));

    public string? EntryLabel(string entryId) =>
        Find(entryId) is ILexEntry entry ? entry.HeadWord?.Text : null;

    public IReadOnlyList<(string Id, string? Name)>? Slots(string msaId) => Find(msaId) switch
    {
        IMoDerivAffMsa => [(ReadingDeltaGroupings.Derivational, null)],
        IMoInflAffMsa msa => [.. msa.SlotsRC.Select(slot => (CanonicalId.FromGuid(slot.Guid).Value, (string?)slot.Name.BestAnalysisAlternative.Text))],
        IMoMorphSynAnalysis => [],
        _ => null,
    };

    private ICmObject? Find(string id) =>
        _reader is not null && CanonicalId.TryParse(id, out var parsed) &&
        _reader.Cache.ServiceLocator.ObjectRepository.TryGetObject(parsed.ToGuid(), out var found) ? found : null;

    private static string Key(string word) => ReadingCauseGrouping.Fold(word);

    public void Dispose() => _reader?.Dispose();
}
