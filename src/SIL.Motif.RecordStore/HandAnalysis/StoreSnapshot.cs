using System.Buffers.Text;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.RecordStore.HandAnalysis;

/// <summary>An immutable pinned point. Answers never change after a newer point is published.</summary>
internal sealed partial class StoreSnapshot : IHandAnalysisSnapshot
{
    private static readonly JsonSerializerOptions ReadJson = MotifJson.CreateHandAnalysisReadOptions();
    private static readonly ImmutableArray<ReadLimitation> UseCountLimitation = [new("use-count-native-only", [],
        "Use counts include native Text attachments and anchor records; occurrences in an external Source are outside the store.")];
    private PinnedPoint? held;
    private HandAnalysisHost? host;
    private PinnedPoint Point => Volatile.Read(ref held) ?? throw new ObjectDisposedException(nameof(StoreSnapshot));

    internal StoreSnapshot(HandAnalysisHost host, PinnedPoint point)
    {
        this.host = host;
        held = point;
        Stamp = point.Stamp;
    }

    public SnapshotStamp Stamp { get; }

    /// <summary>Checks the current published proof independently of this pin's immutable facts.</summary>
    public bool IsCurrent => Volatile.Read(ref host)?.IsCurrent(Point.Proof) == true;

    internal int AnalysisCount => Point.Projection.Readings.Count;
    internal int BundleCount => Point.Projection.BundleOwner.Count;
    internal string WfiFactsDigest => Point.Projection.FactsDigest;
    internal WfiProjection Projection => Point.Projection;
    internal HandAnalysisProjection Hand => Point.Hand;

    /// <summary>Rows or slots examined by lookups on this point since it was built; for indexed-lookup tests.</summary>
    internal long RowsTouched => Point.Hand.Touched;

    public ValueTask<CommandOutcome<Page<WordFact>>> WordsAsync(WordQuery query, PageRequest page, CancellationToken ct = default) =>
        Read(query, page, ct, (point, request) => Words(point.Projection, query, request, ct));

    private Page<WordFact> Words(WfiProjection projection, WordQuery query, PagePosition request, CancellationToken ct)
    {
        switch (query)
        {
            case WordQuery.Identity identity:
                var headers = Headers(projection, identity.Key);
                WordFact[] facts = headers.IsEmpty ? [] : [Header(projection, headers, identity)];
                return Paginate(request, facts, _ => 0, []);
            case WordQuery.Reading { Key: ReadingKey.Analysis { Reference: AnalysisRef.Parser } }:
                return Missing<WordFact>(request, Unavailable("parser-reading"));
            case WordQuery.Reading reading:
                var gloss = (reading.Key as ReadingKey.NativeGloss)?.Id;
                var analysis = gloss is { } id ? projection.GlossOwner.GetValueOrDefault(id)
                    : reading.Key is ReadingKey.Analysis { Reference: AnalysisRef.Native native } ? native.Id : Guid.Empty;
                WordFact[] match = projection.Readings.TryGetValue(analysis, out var found)
                    ? [found.Fact with { SelectedGlossId = gloss,
                        OccurrenceUseCount = gloss is { } g ? Hand.Occurrences.Counts.GetValueOrDefault(g) : found.Fact.OccurrenceUseCount }] : [];
                return Paginate(request, match, _ => 0, match.Length == 0 ? [] : UseCountLimitation);
            case WordQuery.Alternatives alternatives:
                var ranked = alternatives.Order == WordOrder.DefaultRank;
                var ordered = (ranked
                    ? Headers(projection, alternatives.Wordform).SelectMany(h => h.Analyses).Select(id => projection.Readings[id])
                        .OrderBy(r => r.Fact.DefaultRankKey, StringComparer.Ordinal)
                    : Headers(projection, alternatives.Wordform).SelectMany(h => h.Analyses).Select(id => projection.Readings[id])
                        .OrderBy(r => r.Ordinal)).Select((r, i) =>
                {
                    ct.ThrowIfCancellationRequested();
                    return (Key: (long)i, Reading: r);
                }).ToArray();
                var rows = Paginate(request, ordered, r => r.Key, UseCountLimitation);
                return new(Stamp, rows.Items.Select(r => (WordFact)r.Reading.Fact).ToImmutableArray(),
                    rows.Total, rows.NextCursor, rows.Complete, rows.Limitations);
            default: throw new ArgumentException("Unknown word query.", nameof(query));
        }
    }

    internal ValueTask<CommandOutcome<Page<WordHeader>>> InventoryAsync(PageRequest page, CancellationToken ct = default) =>
        Read("wordform inventory", page, ct, (point, request) =>
            Paginate(request, point.Projection.Headers, h => h.Encounter.RepositoryOrdinal, []));

    private static ImmutableArray<WordHeader> Headers(WfiProjection projection, WordIdentity key) => key switch
    {
        WordIdentity.Native native => projection.HeaderById.TryGetValue(native.Id, out var header) ? [header] : [],
        WordIdentity.CaseForm(var form) => projection.ByKey.TryGetValue((form.Ws, WfiProjection.Nfd(form.NfdCaseForm)), out var ids)
            ? ids.Select(id => projection.HeaderById[id]).ToImmutableArray() : [],
        _ => throw new ArgumentException("Unknown wordform identity.", nameof(key))
    };

    private static WordFact.Header Header(WfiProjection projection, ImmutableArray<WordHeader> headers, WordQuery.Identity query)
    {
        var focus = (query.Key as WordIdentity.CaseForm)?.Form;
        var exact = headers.Select(h => h.Id).ToImmutableArray();
        var forms = headers.Select(h => new WordformForms(h.Id, h.Forms)).ToImmutableArray();
        ImmutableArray<OtherCaseFact> other = [];
        if (query.CasePair is { } pair)
        {
            var matches = Headers(projection, new WordIdentity.CaseForm(pair))
                .Select(h => h.Id).Except(exact).ToImmutableArray();
            if (!matches.IsEmpty) other = [new(pair, matches)];
        }

        var analyses = headers.SelectMany(h => h.Analyses);
        return new(focus, exact, forms, other, null, null,
            new(analyses.LongCount(), analyses.Sum(id => (long)projection.Readings[id].Fact.Glosses.Length), true));
    }

    private sealed record PagePosition(int Limit, string QueryKey, long? Last);

    private PagePosition ReadPage<TQuery>(TQuery query, PageRequest request)
    {
        if (request.Limit is < 1 or > PageRequest.MaxLimit)
            throw new QueryRefused(new(RefusalCodes.HandAnalysisInvalidPageLimit, FailureReason.InvalidArgument,
                $"Page size must be 1 to {PageRequest.MaxLimit}."));
        var bound = Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(query, ReadJson)));
        if (request.Cursor is null) return new(request.Limit, bound, null);
        CursorBinding? binding;
        try { binding = JsonSerializer.Deserialize<CursorBinding>(Base64Url.DecodeFromChars(request.Cursor), ReadJson); }
        catch (Exception exception) when (exception is FormatException or JsonException)
        { throw InvalidCursor("The cursor is malformed."); }
        if (binding is null || binding.SnapshotId != Stamp.SnapshotId || binding.QueryKey != bound ||
            !long.TryParse(binding.LastOrderKey, NumberStyles.None, CultureInfo.InvariantCulture, out var last))
            throw InvalidCursor("The cursor belongs to another snapshot or query.");
        return new(request.Limit, bound, last);
    }

    private sealed class QueryRefused(Refusal refusal) : Exception(refusal.Message)
    {
        internal Refusal Refusal { get; } = refusal;
    }

    private static QueryRefused InvalidCursor(string message) =>
        new(new(RefusalCodes.HandAnalysisInvalidCursor, FailureReason.InvalidArgument, message));

    private ValueTask<CommandOutcome<T>> Read<TQuery, T>(TQuery query, PageRequest page, CancellationToken ct,
        Func<PinnedPoint, PagePosition, T> read) where T : class
    {
        var point = Point;
        ct.ThrowIfCancellationRequested();
        try
        {
            var result = read(point, ReadPage(query, page));
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CommandOutcome<T>.Success(result));
        }
        catch (QueryRefused refusal)
        {
            ct.ThrowIfCancellationRequested();
            return ValueTask.FromResult(CommandOutcome<T>.Refused(refusal.Refusal));
        }
    }

    private Page<T> Paginate<T>(PagePosition request, IReadOnlyList<T> ordered, Func<T, long> orderKey,
        ImmutableArray<ReadLimitation> limitations, bool complete = true)
    {
        var start = 0;
        if (request.Last is { } last)
        {
            int low = 0, high = ordered.Count;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (orderKey(ordered[mid]) <= last) low = mid + 1;
                else high = mid;
            }
            start = low;
            if (start == 0 || orderKey(ordered[start - 1]) != last || start == ordered.Count)
                throw InvalidCursor("The cursor has no continuation in this query.");
        }
        var builder = ImmutableArray.CreateBuilder<T>(Math.Min(request.Limit, ordered.Count - start));
        for (var i = start; i < start + builder.Capacity; i++) builder.Add(ordered[i]);
        var items = builder.MoveToImmutable();
        var more = start + items.Length < ordered.Count;
        var cursor = more ? Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(
            new CursorBinding(Stamp.SnapshotId, request.QueryKey, orderKey(items[^1]).ToString(CultureInfo.InvariantCulture)),
            ReadJson)) : null;
        return new(Stamp, items, complete ? ordered.Count : null, cursor, complete, limitations);
    }

    internal static ReadLimitation Unavailable(string family) => new(family + "-unavailable", [],
        $"The {family} facts are unavailable in this index.");

    private Page<T> Missing<T>(PagePosition request, ReadLimitation limitation) => Paginate<T>(request, [], _ => 0, [limitation], false);

    public ValueTask DisposeAsync()
    {
        var owner = Interlocked.Exchange(ref host, null);
        Interlocked.Exchange(ref held, null);
        owner?.Release(Stamp.SnapshotId);
        return ValueTask.CompletedTask;
    }
}
