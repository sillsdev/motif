using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;

namespace SIL.Motif.Tests.Contract.HandAnalysis;

internal sealed class InMemoryHandAnalysisIndex : IHandAnalysisIndex
{
    private readonly Dictionary<string, CursorBinding> cursors = new(StringComparer.Ordinal);
    private readonly List<Snapshot> pins = [];
    private readonly List<Publication> retained = [];

    private sealed record Publication(SnapshotStamp Stamp, WordIdentity Wordform, ImmutableArray<WordFact.Reading> Words,
        ImmutableDictionary<Guid, string?> NativeClasses, ImmutableDictionary<string, ReferencePage> References);

    public void Publish(SnapshotStamp stamp, WordIdentity wordform, params ImmutableArray<WordFact.Reading> words) =>
        Publish(stamp, wordform, words, []);

    public void Publish(SnapshotStamp stamp, WordIdentity wordform, ImmutableArray<WordFact.Reading> words,
        ImmutableArray<ReferenceResultTarget> nativeTargets, params (ReferenceQuery Query, ReferencePage Page)[] references)
    {
        retained.RemoveAll(p => p.Stamp.ProjectId == stamp.ProjectId && p.Stamp.SnapshotId == stamp.SnapshotId);
        retained.Insert(0, new(stamp, wordform, words,
            nativeTargets.ToImmutableDictionary(target => target.Id, target => target.NativeClass),
            references.ToImmutableDictionary(evidence => JsonSerializer.Serialize(evidence.Query), evidence => evidence.Page,
                StringComparer.Ordinal)));
        var old = retained.Where(p => p.Stamp.ProjectId == stamp.ProjectId).Skip(4).ToArray();
        foreach (var point in old)
            retained.Remove(point);
    }

    public ValueTask<CommandOutcome<IHandAnalysisSnapshot>> PinAsync(Guid projectId, string? exactSnapshotId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var publication = retained.Find(p => p.Stamp.ProjectId == projectId &&
            (exactSnapshotId is null || p.Stamp.SnapshotId == exactSnapshotId))
            ?? pins.Select(pin => pin.Publication).FirstOrDefault(p => p.Stamp.ProjectId == projectId &&
                p.Stamp.SnapshotId == exactSnapshotId);
        if (publication is null)
            return exactSnapshotId is null
                ? Refuse<IHandAnalysisSnapshot>(RefusalCodes.HandAnalysisIndexUnavailable,
                    new Dictionary<string, string> { [HandAnalysisRefusalFacts.Cause] = HandAnalysisRefusalFacts.Unavailable })
                : Refuse<IHandAnalysisSnapshot>(RefusalCodes.HandAnalysisSnapshotExpired,
                    new Dictionary<string, string> { [HandAnalysisRefusalFacts.SnapshotId] = exactSnapshotId });

        ct.ThrowIfCancellationRequested();
        var pin = new Snapshot(this, publication);
        pins.Add(pin);
        return ValueTask.FromResult(CommandOutcome<IHandAnalysisSnapshot>.Success(pin));
    }

    public ValueTask<CommandOutcome<IHandAnalysisSnapshot>> PinAtAsync(Guid projectId, string? home, BaselineToken? baseline,
        (SnapshotStamp Stamp, BaselineSemanticIdentity? Baseline)[] publications)
    {
        var matches = publications.Where(p => p.Stamp.ProjectId == projectId &&
            (baseline is null ? p.Stamp.HomePoint == home : p.Baseline == baseline.SemanticIdentity))
            .Select(p => p.Stamp.SnapshotId).Distinct().ToArray();
        return matches.Length switch
        {
            0 => Refuse<IHandAnalysisSnapshot>(RefusalCodes.HandAnalysisSnapshotExpired),
            1 => PinAsync(projectId, matches[0], CancellationToken.None),
            _ => Refuse<IHandAnalysisSnapshot>(RefusalCodes.HandAnalysisIndexUnavailable,
                new Dictionary<string, string> { [HandAnalysisRefusalFacts.Cause] = HandAnalysisRefusalFacts.AmbiguousBasis })
        };
    }

    private static ValueTask<CommandOutcome<T>> Refuse<T>(string code, IReadOnlyDictionary<string, string>? facts = null)
        where T : class =>
        ValueTask.FromResult(CommandOutcome<T>.Refused(new(code, RefusalCatalog.Specs[code].DefaultReason, "Read refused.", facts)));

    private sealed class Snapshot(InMemoryHandAnalysisIndex index, Publication publication) : IHandAnalysisSnapshot
    {
        private bool disposed;
        public Publication Publication { get; } = publication;
        public SnapshotStamp Stamp => Publication.Stamp;

        public ProjectRead Project
        {
            get
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                return new ProjectRead.Unavailable(Stamp, new(RefusalCodes.HandAnalysisIndexUnavailable, [],
                    "Project settings have not been projected."));
            }
        }

        public ValueTask<CommandOutcome<Page<WordFact>>> WordsAsync(WordQuery query, PageRequest page, CancellationToken ct)
        {
            CheckRead(ct);
            var matches = query switch
            {
                WordQuery.Alternatives alternatives when alternatives.Wordform == Publication.Wordform => Publication.Words,
                WordQuery.Reading { Key: ReadingKey.Analysis(var reference) } =>
                    Publication.Words.Where(row => row.Analysis == reference).ToImmutableArray(),
                _ => []
            };
            var useRank = query is WordQuery.Alternatives { Order: WordOrder.DefaultRank };
            var ordered = matches.Select(row => (Row: row, Key: new WordOrderKey(useRank ? row.DefaultRankKey : null,
                    row.EncounterKey, JsonSerializer.Serialize(row.Analysis), row.SelectedGlossId)))
                .OrderBy(row => row.Key.Rank, StringComparer.Ordinal)
                .ThenBy(row => row.Key.Encounter, StringComparer.Ordinal)
                .ThenBy(row => row.Key.Analysis, StringComparer.Ordinal)
                .ThenBy(row => row.Key.GlossId)
                .Select(row => ((WordFact)row.Row, JsonSerializer.Serialize(row.Key))).ToArray();
            return ReadPage(query, page, ordered, ct);
        }

        private sealed record WordOrderKey(string? Rank, string Encounter, string Analysis, Guid? GlossId);

        private ValueTask<CommandOutcome<Page<T>>> ReadPage<T, TQuery>(TQuery query, PageRequest page,
            (T Row, string Key)[] ordered, CancellationToken ct, Page<T>? evidence = null)
        {
            if (page.Limit is < 1 or > PageRequest.MaxLimit)
                return Refuse<Page<T>>(RefusalCodes.PagingInvalid);

            var queryKey = JsonSerializer.Serialize(query);
            var start = 0;
            if (page.Cursor is { } cursor)
            {
                if (!index.cursors.TryGetValue(cursor, out var binding) ||
                    binding.SnapshotId != Stamp.SnapshotId || binding.QueryKey != queryKey)
                    return Refuse<Page<T>>(RefusalCodes.PagingInvalid);
                start = Array.FindIndex(ordered, row => row.Key == binding.LastOrderKey) + 1;
                if (start == 0)
                    return Refuse<Page<T>>(RefusalCodes.PagingInvalid);
            }

            var items = ordered.Skip(start).Take(page.Limit).Select(row => row.Row).ToImmutableArray();
            ct.ThrowIfCancellationRequested();
            string? next = null;
            if (start + items.Length < ordered.Length)
            {
                next = Guid.NewGuid().ToString("N");
                index.cursors.Add(next, new(Stamp.SnapshotId, queryKey, ordered[start + items.Length - 1].Key));
            }
            return ValueTask.FromResult(CommandOutcome<Page<T>>.Success(evidence is null
                ? new(Stamp, items, ordered.Length, next, true, [])
                : evidence with { Stamp = Stamp, Items = items, NextCursor = next }));
        }

        public ValueTask<CommandOutcome<OccurrencePage>> OccurrencesAsync(OccurrenceQuery query, PageRequest page, CancellationToken ct)
        {
            CheckRead(ct);
            return Refuse<OccurrencePage>(RefusalCodes.HandAnalysisSourceUnavailable);
        }

        public ValueTask<CommandOutcome<Page<FormHit>>> FormsAsync(FormQuery query, PageRequest page, CancellationToken ct)
        {
            CheckRead(ct);
            return Refuse<Page<FormHit>>(RefusalCodes.HandAnalysisIndexUnavailable);
        }

        public ValueTask<CommandOutcome<Page<LexicalFact>>> LexiconAsync(LexiconQuery query, PageRequest page, CancellationToken ct)
        {
            CheckRead(ct);
            return Refuse<Page<LexicalFact>>(RefusalCodes.HandAnalysisIndexUnavailable);
        }

        public async ValueTask<CommandOutcome<ReferencePage>> ReferencesAsync(ReferenceQuery query, PageRequest page, CancellationToken ct)
        {
            CheckRead(ct);
            var evidence = Publication.References.GetValueOrDefault(JsonSerializer.Serialize(query));
            var ordered = evidence?.Rows.Items.Select((row, ordinal) => (row, ordinal.ToString(CultureInfo.InvariantCulture)))
                .ToArray() ?? [];
            var rows = await ReadPage(query, page, ordered, ct, evidence?.Rows);
            if (rows.Value is not { } result)
                return CommandOutcome<ReferencePage>.Refused(rows.Refusal!);
            if (query is ReferenceQuery.DeleteClosure(var target))
            {
                var nativeClass = Publication.NativeClasses.GetValueOrDefault(target.Id);
                if (nativeClass is not null && !MatchesDeletionKind(target.Kind, nativeClass))
                    return await Refuse<ReferencePage>(RefusalCodes.HandAnalysisTypeMismatch);
                if (nativeClass is null || evidence is null)
                    return await Refuse<ReferencePage>(RefusalCodes.HandAnalysisIncompleteReferences);
            }
            return CommandOutcome<ReferencePage>.Success(new(result, evidence?.Counts ?? new(0, 0, 0, [], null, true, null)));
        }

        private static bool MatchesDeletionKind(DeletionKind kind, string nativeClass) => (kind, nativeClass) switch
        {
            (DeletionKind.Entry, "LexEntry") or (DeletionKind.Sense, "LexSense") => true,
            (DeletionKind.Allomorph, "MoStemAllomorph" or "MoAffixAllomorph" or "MoAffixProcess") => true,
            _ => false
        };

        private void CheckRead(CancellationToken ct)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            ct.ThrowIfCancellationRequested();
        }

        public ValueTask DisposeAsync()
        {
            disposed = true;
            index.pins.Remove(this);
            return ValueTask.CompletedTask;
        }
    }
}
