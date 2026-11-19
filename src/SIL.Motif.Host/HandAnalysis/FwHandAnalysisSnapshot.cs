using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;

namespace SIL.Motif.Host.HandAnalysis;

/// <summary>An immutable pin over detached approval facts; disposal only ends access.</summary>
public sealed class FwHandAnalysisSnapshot : IHandAnalysisSnapshot, IApprovedReadingSource
{
    private readonly Point point;
    private bool _disposed;

    internal FwHandAnalysisSnapshot(Point point) => this.point = point;

    internal sealed record Point(SnapshotStamp Stamp, BaselineSemanticIdentity? Baseline, ApprovedReadingSet Set,
        string? LiveStamp = null);

    public SnapshotStamp Stamp => point.Stamp;

    public ApprovedReadingSet Approved
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return point.Set;
        }
    }

    public ProjectRead Project
    {
        get
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return new ProjectRead.Unavailable(Stamp, new(RefusalCodes.HandAnalysisUnsupportedOperation, [],
                "Project settings are not projected by this index."));
        }
    }

    public ValueTask<CommandOutcome<Page<WordFact>>> WordsAsync(WordQuery query, PageRequest page, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        if (page.Limit is < 1 or > PageRequest.MaxLimit)
            return Refused(RefusalCodes.HandAnalysisInvalidPageLimit);
        ImmutableArray<ApprovedAnalysisFact> rows;
        switch (query)
        {
            case WordQuery.Alternatives { Wordform: WordIdentity.Native(var word) }:
                rows = point.Set.ForWordform(word);
                break;
            case WordQuery.Reading { Key: ReadingKey.Analysis(AnalysisRef.Native(var analysis)) }:
                rows = point.Set.ForAnalysis(analysis);
                break;
            default:
                return Refused(RefusalCodes.HandAnalysisUnsupportedOperation);
        }

        var queryKey = JsonSerializer.Serialize(query);
        var ordered = rows;
        var start = 0;
        if (page.Cursor is not null)
        {
            if (!TryReadCursor(page.Cursor, queryKey, out var last)) return Refused(RefusalCodes.HandAnalysisInvalidCursor);
            int low = 0, high = ordered.Length;
            while (low < high)
            {
                var mid = (low + high) / 2;
                if (ordered[mid].AnalysisId.CompareTo(last) > 0) high = mid;
                else low = mid + 1;
            }

            start = low;
        }

        var items = ordered.Skip(start).Take(page.Limit).ToArray();
        var more = start + items.Length < ordered.Length;
        var next = more ? WriteCursor(queryKey, items[^1].AnalysisId) : null;
        var limitations = point.Set.SkippedOtherHumanAgentApprovals > 0
            ? ImmutableArray.Create(new ReadLimitation("approval-other-human-agent", point.Set.SkippedSample,
                $"{point.Set.SkippedOtherHumanAgentApprovals} analyses approved only by another human agent are not counted as approved."))
            : ImmutableArray<ReadLimitation>.Empty;
        var facts = items.Select(fact => (WordFact)ToReading(fact)).ToImmutableArray();
        return ValueTask.FromResult(CommandOutcome<Page<WordFact>>.Success(
            new Page<WordFact>(Stamp, facts, ordered.Length, next, true, limitations)));
    }

    public ValueTask<CommandOutcome<OccurrencePage>> OccurrencesAsync(OccurrenceQuery query, PageRequest page, CancellationToken ct) =>
        Unsupported<OccurrencePage>(ct);

    public ValueTask<CommandOutcome<Page<FormHit>>> FormsAsync(FormQuery query, PageRequest page, CancellationToken ct) =>
        Unsupported<Page<FormHit>>(ct);

    public ValueTask<CommandOutcome<Page<LexicalFact>>> LexiconAsync(LexiconQuery query, PageRequest page, CancellationToken ct) =>
        Unsupported<Page<LexicalFact>>(ct);

    public ValueTask<CommandOutcome<ReferencePage>> ReferencesAsync(ReferenceQuery query, PageRequest page, CancellationToken ct) =>
        Unsupported<ReferencePage>(ct);

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }

    private WordFact.Reading ToReading(ApprovedAnalysisFact fact) => new(new AnalysisRef.Native(fact.AnalysisId), fact.WordformId,
        null, null, fact.Bundles, [], [new EvaluationFact(fact.EvaluationId, fact.AgentId, true, true)], null, null,
        fact.AnalysisId.ToString("D"), null, null, fact.Signature,
        fact.UnsignedCause is null ? [] : [new ReadLimitation(fact.UnsignedCause, [fact.AnalysisId],
            "The analysis has no verified Reading identity.")]);

    private ValueTask<CommandOutcome<T>> Unsupported<T>(CancellationToken ct) where T : class
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ct.ThrowIfCancellationRequested();
        return ValueTask.FromResult(CommandOutcome<T>.Refused(FwHandAnalysisIndex.Refusal(RefusalCodes.HandAnalysisUnsupportedOperation)));
    }

    private static ValueTask<CommandOutcome<Page<WordFact>>> Refused(string code) =>
        ValueTask.FromResult(CommandOutcome<Page<WordFact>>.Refused(FwHandAnalysisIndex.Refusal(code)));

    private string WriteCursor(string queryKey, Guid last) => Convert.ToBase64String(Encoding.UTF8.GetBytes(
        JsonSerializer.Serialize(new CursorBinding(Stamp.SnapshotId, queryKey, last.ToString("D")))));

    private bool TryReadCursor(string cursor, string queryKey, out Guid last)
    {
        last = default;
        try
        {
            var binding = JsonSerializer.Deserialize<CursorBinding>(Convert.FromBase64String(cursor));
            return binding is not null && binding.SnapshotId == Stamp.SnapshotId && binding.QueryKey == queryKey &&
                   Guid.TryParse(binding.LastOrderKey, out last);
        }
        catch (Exception error) when (error is FormatException or JsonException)
        {
            return false;
        }
    }
}
