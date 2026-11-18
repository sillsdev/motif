using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.RecordStore.HandAnalysis;

internal sealed record ExternalBasis(string SourceRevision = "", string ParserRevision = "", string SettingsRevision = "");

internal sealed class IndexUnavailableException(string message, Exception? inner = null,
    string cause = HandAnalysisRefusalFacts.IncompleteNative) : InvalidOperationException(message, inner)
{
    internal string Cause { get; } = cause;
}

internal sealed class SnapshotExpiredException(string message) : InvalidOperationException(message);

internal sealed record PinnedPoint(SnapshotStamp Stamp, StoreState Proof, WfiProjection Projection, ReferenceIndexSnapshot Index);

/// <summary>Serves immutable pins of an existing published Home through read-only SQLite connections.</summary>
/// <remarks>
/// Opening a missing Home fails. The host acquires no application writer lock and leaves the main database
/// and WAL content unchanged. SQLite may create sidecars and update shared memory to coordinate reads
/// with a live writer. Pins hold immutable facts after the database transaction ends.
/// </remarks>
internal sealed class HandAnalysisHost : IHandAnalysisIndex, IAsyncDisposable
{
    public const int IndexSchemaVersion = 2;
    public const string ComparisonVersionNone = "none";
    public const int RetainedPoints = 4;

    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly LinkedList<PinnedPoint> ring = new();
    private readonly object pinLifetime = new();
    private readonly Dictionary<string, (PinnedPoint Point, int Count)> live = new(StringComparer.Ordinal);
    private readonly RecordStore reader;
    private readonly string storeScope;
    private readonly string path;
    private readonly ExternalBasis basis;
    private bool disposed;

    private HandAnalysisHost(string path, RecordStore reader, ExternalBasis basis)
    {
        this.reader = reader;
        this.basis = basis;
        storeScope = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        this.path = path;
    }

    public static Task<HandAnalysisHost> OpenAsync(string path, ExternalBasis? basis = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        path = RecordStore.ResolvePath(path);
        try
        {
            return Task.FromResult(new HandAnalysisHost(path, RecordStore.OpenPublishedReader(path), basis ?? new()));
        }
        catch (Exception exception) when (exception is SqliteException or IOException or InvalidDataException or InvalidOperationException)
        {
            throw new IndexUnavailableException("The published Home cannot be opened: " + exception.Message, exception);
        }
    }

    public async ValueTask<CommandOutcome<IHandAnalysisSnapshot>> PinAsync(Guid projectId, string? exactSnapshotId, CancellationToken ct)
    {
        try
        {
            return CommandOutcome<IHandAnalysisSnapshot>.Success(await PinAsync(projectId, basis, exactSnapshotId, ct));
        }
        catch (SnapshotExpiredException exception)
        {
            return CommandOutcome<IHandAnalysisSnapshot>.Refused(new(RefusalCodes.HandAnalysisSnapshotExpired,
                FailureReason.Refused, exception.Message,
                new Dictionary<string, string> { [HandAnalysisRefusalFacts.SnapshotId] = exactSnapshotId! }));
        }
        catch (IndexUnavailableException exception)
        {
            return CommandOutcome<IHandAnalysisSnapshot>.Refused(new(RefusalCodes.HandAnalysisIndexUnavailable,
                FailureReason.Refused, exception.Message,
                new Dictionary<string, string> { [HandAnalysisRefusalFacts.Cause] = exception.Cause }));
        }
    }

    internal Task<StoreSnapshot> PinAsync(ExternalBasis external, string? exactSnapshotId, CancellationToken ct = default) =>
        PinAsync(null, external, exactSnapshotId, ct);

    private async Task<StoreSnapshot> PinAsync(Guid? projectId, ExternalBasis external, string? exactSnapshotId, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (exactSnapshotId is not null)
            {
                lock (pinLifetime)
                {
                    var found = ring.FirstOrDefault(p => p.Stamp.SnapshotId == exactSnapshotId)
                        ?? live.GetValueOrDefault(exactSnapshotId).Point;
                    ct.ThrowIfCancellationRequested();
                    if (found is null || projectId.HasValue && found.Stamp.ProjectId != projectId)
                        throw new SnapshotExpiredException("The snapshot is not retained for this Home.");
                    return Hold(found);
                }
            }

            var point = await AcquirePointAsync(ct);
            var home = point.State.Revision;
            var project = point.ProjectId ?? throw new IndexUnavailableException("The native Home identity is unavailable.");
            if (projectId.HasValue && projectId != project)
                throw new IndexUnavailableException("The requested project does not identify this Home.", cause: HandAnalysisRefusalFacts.Unavailable);
            var id = SnapshotId(project, point.State, external);
            var existing = ring.FirstOrDefault(p => p.Stamp.SnapshotId == id);
            lock (pinLifetime)
                existing ??= live.GetValueOrDefault(id).Point;
            if (existing is not null)
            {
                ct.ThrowIfCancellationRequested();
                Retain(existing);
                return Hold(existing);
            }

            var projection = ring.FirstOrDefault(p => p.Proof.SamePoint(point.State))?.Projection
                ?? WfiProjection.Build(point.Records, point.Index.UsageSchema, ct);
            var stamp = new SnapshotStamp(id, project, home, external.SourceRevision, external.ParserRevision, external.SettingsRevision,
                ComparisonVersionNone, IndexSchemaVersion, SnapshotVerification.Full);
            var pinned = new PinnedPoint(stamp, point.State, projection, point.GetPlanningIndex(ct));
            ct.ThrowIfCancellationRequested();
            Retain(pinned);
            return Hold(pinned);
        }
        finally { gate.Release(); }
    }

    private void Retain(PinnedPoint point)
    {
        ring.Remove(point);
        ring.AddFirst(point);
        if (ring.Count > RetainedPoints) ring.RemoveLast();
    }

    private StoreSnapshot Hold(PinnedPoint point)
    {
        var id = point.Stamp.SnapshotId;
        lock (pinLifetime)
            live[id] = (point, live.GetValueOrDefault(id).Count + 1);
        return new(this, point);
    }

    internal void Release(string id)
    {
        lock (pinLifetime)
        {
            if (!live.TryGetValue(id, out var entry)) return;
            if (entry.Count == 1) live.Remove(id);
            else live[id] = (entry.Point, entry.Count - 1);
        }
    }

    internal async Task VerifyAsync()
    {
        await gate.WaitAsync();
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            await AcquirePointAsync(default);
        }
        finally { gate.Release(); }
    }

    private async Task<VerifiedPoint> AcquirePointAsync(CancellationToken ct)
    {
        VerifiedPoint point;
        try
        {
            point = await reader.VerifiedPointAsync(ct);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or InvalidDataException or InvalidOperationException)
        {
            ct.ThrowIfCancellationRequested();
            var cause = exception is InvalidDataException || exception is SqliteException { SqliteErrorCode: 11 or 26 }
                ? HandAnalysisRefusalFacts.Corrupt : HandAnalysisRefusalFacts.Unavailable;
            throw new IndexUnavailableException("The store point cannot be verified: " + exception.Message, exception, cause);
        }
        ct.ThrowIfCancellationRequested();
        var closure = point.Index.Incoming([]);
        if (!closure.NativeComplete)
            throw new IndexUnavailableException("The reference index is incomplete: " + string.Join("; ", closure.Limitations),
                cause: point.IndexTrusted ? HandAnalysisRefusalFacts.IncompleteNative : HandAnalysisRefusalFacts.Corrupt);
        return point;
    }

    internal bool IsCurrent(StoreState proof)
    {
        try { return RecordStore.IsPublishedPoint(path, proof); }
        catch (Exception exception) when (exception is SqliteException or IOException or InvalidDataException) { return false; }
    }

    private string SnapshotId(Guid project, StoreState proof, ExternalBasis external) => Convert.ToHexStringLower(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            Store = Encode(storeScope), Project = project, Proof = proof,
            Source = Encode(external.SourceRevision), Parser = Encode(external.ParserRevision), Settings = Encode(external.SettingsRevision),
            ComparisonVersionNone, IndexSchemaVersion
        })));

    private static string Encode(string text) => Convert.ToBase64String(MemoryMarshal.AsBytes(text.AsSpan()));

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (disposed) return;
            disposed = true;
            ring.Clear();
            lock (pinLifetime) live.Clear();
            await reader.DisposeAsync();
        }
        finally { gate.Release(); }
    }
}
