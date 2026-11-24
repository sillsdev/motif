using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.RecordStore.HandAnalysis;

internal sealed record ExternalBasis(string SourceRevision = "", string ParserRevision = "", string SettingsRevision = "");

internal class IndexUnavailableException(string message, Exception? inner = null,
    string cause = HandAnalysisRefusalFacts.IncompleteNative) : InvalidOperationException(message, inner)
{
    internal string Cause { get; } = cause;
}

/// <summary>A request named another project; only that request is refused, the host stays as it was.</summary>
internal sealed class WrongProjectException(string message)
    : IndexUnavailableException(message, null, HandAnalysisRefusalFacts.Unavailable);

internal sealed class SnapshotExpiredException(string message) : InvalidOperationException(message);

internal sealed record PinnedPoint(SnapshotStamp Stamp, StoreState Proof, HandAnalysisProjection Hand, ReferenceIndexSnapshot Index)
{
    public WfiProjection Projection => Hand.Wfi;
}

/// <summary>Serves immutable pins of an existing published Home through read-only SQLite connections.</summary>
/// <remarks>
/// Opening a missing Home fails. The host acquires no application writer lock and leaves the main database
/// and WAL content unchanged. SQLite may create sidecars and update shared memory to coordinate reads
/// with a live writer. Pins hold immutable facts after the database transaction ends.
/// </remarks>
internal sealed partial class HandAnalysisHost : IHandAnalysisIndex, IAsyncDisposable
{
    public const int IndexSchemaVersion = 3;
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
    private readonly FormCollationFactory collationFactory;
    private readonly IProgress<OpenPhase>? progress;
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private bool disposed;

    private HandAnalysisHost(string path, RecordStore reader, ExternalBasis basis, FormCollationFactory collationFactory,
        IProgress<OpenPhase>? progress)
    {
        this.collationFactory = collationFactory;
        this.reader = reader;
        this.basis = basis;
        this.progress = progress;
        storeScope = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        this.path = path;
    }

    /// <summary>Opens the read-only handle; with <paramref name="loadPoint"/> it also loads and projects the current point, so it is Ready on return.</summary>
    /// <param name="progress">Told as each <see cref="OpenPhase"/> starts, for this open and for every later load by this host.</param>
    public static async Task<HandAnalysisHost> OpenAsync(string path, ExternalBasis? basis = null, CancellationToken ct = default,
        FormCollationFactory? collation = null, IProgress<OpenPhase>? progress = null, bool loadPoint = false)
    {
        ct.ThrowIfCancellationRequested();
        path = RecordStore.ResolvePath(path);
        var started = System.Diagnostics.Stopwatch.StartNew();
        progress?.Report(OpenPhase.Open);
        ct.ThrowIfCancellationRequested();
        HandAnalysisHost host;
        try
        {
            host = new HandAnalysisHost(path, RecordStore.OpenPublishedReader(path), basis ?? new(),
                collation ?? OrdinalFormCollation.Factory, progress);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or InvalidDataException or InvalidOperationException)
        {
            throw new IndexUnavailableException("The published Home cannot be opened: " + exception.Message, exception);
        }
        host.phases = [new(OpenPhase.Open, TimeSpan.Zero, started.Elapsed)];
        host.readiness = HostReadiness.Opened;
        if (!loadPoint) return host;
        try
        {
            await host.RefreshAsync(ct);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
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
            return Hold(await LoadCurrentAsync(projectId, external, ct));
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Loads the store's current point and puts it at the head of the ring in one step. Every phase builds on the
    /// handle's own immutable results, so a pin made meanwhile waits on the gate and sees the old head or the new one.
    /// </summary>
    internal async Task<SnapshotStamp> RefreshAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return (await LoadCurrentAsync(null, basis, ct)).Stamp;
        }
        finally { gate.Release(); }
    }

    /// <summary>Test hook: called once the new point is built and before the ring changes; throwing leaves the ring as it was.</summary>
    internal Action? SwapReached { get; set; }

    // Caller holds the gate.
    private async Task<PinnedPoint> LoadCurrentAsync(Guid? projectId, ExternalBasis external, CancellationToken ct)
    {
        var recorder = new PhaseRecorder(this);
        readiness = HostReadiness.Verifying;
        referenceIndex = ReferenceIndexState.Building;
        try
        {
            recorder.Begin(OpenPhase.State, ct);
            var (point, verification) = await AcquirePointAsync(ct, () => recorder.Begin(OpenPhase.Load, ct));
            if (verification == StoreVerification.Certified && verifiedProof?.SamePoint(point.State) == true)
                verification = StoreVerification.Full;
            recorder.Begin(OpenPhase.Project, ct);
            var home = point.State.Revision;
            var project = point.ProjectId ?? throw new IndexUnavailableException("The native Home identity is unavailable.");
            if (projectId.HasValue && projectId != project)
                throw new WrongProjectException("The requested project does not identify this Home.");
            var collation = collationFactory(HandAnalysisProjection.Systems(point));
            var id = SnapshotId(project, point.State, external, collation.Version);
            var existing = ring.FirstOrDefault(p => p.Stamp.SnapshotId == id);
            lock (pinLifetime)
                existing ??= live.GetValueOrDefault(id).Point;
            PinnedPoint pinned;
            if (existing is not null)
            {
                pinned = existing.Stamp.Verification == SnapshotVerification.Full || verification != StoreVerification.Full
                    ? existing : existing with { Stamp = existing.Stamp with { Verification = SnapshotVerification.Full } };
                recorder.Begin(OpenPhase.Pin, ct);
            }
            else
            {
                var hand = ring.FirstOrDefault(p => p.Proof.SamePoint(point.State) && p.Hand.ComparisonVersion == collation.Version)?.Hand
                    ?? HandAnalysisProjection.Build(point, collation, ct);
                recorder.Begin(OpenPhase.Pin, ct);
                var stamp = new SnapshotStamp(id, project, home, external.SourceRevision, external.ParserRevision, external.SettingsRevision,
                    collation.Version, IndexSchemaVersion,
                    verification == StoreVerification.Full ? SnapshotVerification.Full : SnapshotVerification.Certified);
                pinned = new PinnedPoint(stamp, point.State, hand, point.GetPlanningIndex(ct));
            }
            ct.ThrowIfCancellationRequested();
            SwapReached?.Invoke();
            lock (pinLifetime) Retain(pinned);
            recorder.Finish();
            readiness = HostReadiness.Ready;
            referenceIndex = ReferenceIndexState.Ready;
            return pinned;
        }
        catch (OperationCanceledException)
        {
            recorder.Abandon();
            Settle();
            throw;
        }
        catch (WrongProjectException)
        {
            recorder.Abandon();
            Settle();
            throw;
        }
        catch (IndexUnavailableException exception)
        {
            recorder.Abandon();
            readiness = HostReadiness.Unavailable;
            referenceIndex = ReferenceIndexState.Unavailable;
            unavailable = exception.Message;
            throw;
        }
        catch
        {
            recorder.Abandon();
            Settle();
            throw;
        }
    }

    // A cancelled or failed load changes nothing the host already served.
    private void Settle()
    {
        lock (pinLifetime)
        {
            if (unavailable is not null)
            {
                readiness = HostReadiness.Unavailable;
                referenceIndex = ReferenceIndexState.Unavailable;
                return;
            }
            readiness = ring.Count > 0 ? HostReadiness.Ready : HostReadiness.Opened;
            referenceIndex = ring.Count > 0 ? ReferenceIndexState.Ready : ReferenceIndexState.NotBuilt;
        }
    }

    private void Retain(PinnedPoint point)
    {
        var replaced = ring.FirstOrDefault(p => p.Stamp.SnapshotId == point.Stamp.SnapshotId);
        if (replaced is not null) ring.Remove(replaced);
        ring.AddFirst(point);
        if (ring.Count > RetainedPoints) ring.RemoveLast();
        unavailable = null;
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

    /// <summary>Reads and verifies the current point through this host's read-only handle; nothing is written or retained.</summary>
    internal async Task CheckPointAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            await AcquirePointAsync(ct);
        }
        finally { gate.Release(); }
    }

    private async Task<(VerifiedPoint Point, StoreVerification Verification)> AcquirePointAsync(CancellationToken ct, Action? stateRead = null)
    {
        (VerifiedPoint Point, StoreVerification Verification) served;
        try
        {
            served = await reader.ServedPointAsync(ct, stateRead);
        }
        catch (Exception exception) when (exception is SqliteException or IOException or InvalidDataException or InvalidOperationException)
        {
            ct.ThrowIfCancellationRequested();
            var cause = exception is InvalidDataException || exception is SqliteException { SqliteErrorCode: 11 or 26 }
                ? HandAnalysisRefusalFacts.Corrupt : HandAnalysisRefusalFacts.Unavailable;
            throw new IndexUnavailableException("The store point cannot be verified: " + exception.Message, exception, cause);
        }
        ct.ThrowIfCancellationRequested();
        var point = served.Point;
        var closure = point.Index.Incoming([]);
        if (!closure.NativeComplete)
            throw new IndexUnavailableException("The reference index is incomplete: " + string.Join("; ", closure.Limitations),
                cause: point.IndexTrusted ? HandAnalysisRefusalFacts.IncompleteNative : HandAnalysisRefusalFacts.Corrupt);
        return served;
    }

    internal bool IsCurrent(StoreState proof)
    {
        try { return RecordStore.IsPublishedPoint(path, proof); }
        catch (Exception exception) when (exception is SqliteException or IOException or InvalidDataException) { return false; }
    }

    private string SnapshotId(Guid project, StoreState proof, ExternalBasis external, string comparisonVersion) => Convert.ToHexStringLower(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new
        {
            Store = Encode(storeScope), Project = project, Proof = proof,
            Source = Encode(external.SourceRevision), Parser = Encode(external.ParserRevision), Settings = Encode(external.SettingsRevision),
            ComparisonVersion = comparisonVersion, IndexSchemaVersion
        })));

    private static string Encode(string text) => Convert.ToBase64String(MemoryMarshal.AsBytes(text.AsSpan()));

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try
        {
            if (disposed) return;
            disposed = true;
            readiness = HostReadiness.Unavailable;
            unavailable = "The host is disposed.";
            lock (pinLifetime) { ring.Clear(); live.Clear(); }
            await reader.DisposeAsync();
        }
        finally { gate.Release(); }
    }
}
