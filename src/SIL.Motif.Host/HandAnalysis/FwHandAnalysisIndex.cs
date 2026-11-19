using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.HandAnalysis;
using SIL.Motif.Host.Baselines;

namespace SIL.Motif.Host.HandAnalysis;

/// <summary>Where the index finds the project files it reads; neither answer opens or changes the project.</summary>
public interface IHandAnalysisProjectSources
{
    /// <summary>The live <c>.fwdata</c> of the project, or null when it is not known.</summary>
    string? LiveFwData(Guid projectId);

    /// <summary>The saved Baseline copy with this semantic identity, or null when it is gone.</summary>
    string? BaselineFwData(Guid projectId, BaselineSemanticIdentity identity);
}

/// <summary>A pin that also exposes the approved Reading set it was built from.</summary>
public interface IApprovedReadingSource
{
    ApprovedReadingSet Approved { get; }
}

/// <summary>
/// Read-only hand-analysis index over a FieldWorks project. Each pin reads a private copy of the project (or of a
/// Baseline for <c>--at</c>), keeps only detached facts, and releases the copy before it returns.
/// </summary>
/// <remarks>
/// This slice serves <c>PinAsync</c>, <c>Stamp</c> and <c>WordsAsync</c> and the approved set; the other queries refuse with
/// <see cref="RefusalCodes.HandAnalysisUnsupportedOperation"/>. The latest four points per project are retained, and a
/// live pin is reused until the project file's length or modification time changes.
/// </remarks>
public sealed class FwHandAnalysisIndex(IHandAnalysisProjectSources sources) : IHandAnalysisIndex
{
    internal const string ComparisonVersion = "reading-signature-sense-free-1";
    internal const int Schema = 1;
    private const int Retained = 4;
    private readonly object _gate = new();
    private readonly List<FwHandAnalysisSnapshot.Point> _points = [];
    private int _loads;

    /// <summary>How many times a project has been copied and read, for observing live-pin reuse.</summary>
    public int Loads => Volatile.Read(ref _loads);

    public async ValueTask<CommandOutcome<IHandAnalysisSnapshot>> PinAsync(Guid projectId, string? exactSnapshotId, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (exactSnapshotId is not null)
        {
            var found = Find(point => point.Stamp.ProjectId == projectId && point.Stamp.SnapshotId == exactSnapshotId);
            return found is null
                ? Refuse(RefusalCodes.HandAnalysisSnapshotExpired, (HandAnalysisRefusalFacts.SnapshotId, exactSnapshotId))
                : Pin(found);
        }

        var live = sources.LiveFwData(projectId);
        var stamp = LiveStamp(live);
        if (stamp is not null)
        {
            var reused = Find(point => point.Stamp.ProjectId == projectId && point.LiveStamp == stamp);
            if (reused is not null) return Pin(reused);
        }

        return await BuildAsync(projectId, live, null, ct, stamp);
    }

    /// <summary>Pins the project state a saved Baseline captured, for <c>--at &lt;baseline&gt;</c>.</summary>
    public async ValueTask<CommandOutcome<IHandAnalysisSnapshot>> PinAtBaselineAsync(Guid projectId,
        BaselineSemanticIdentity identity, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ct.ThrowIfCancellationRequested();
        var found = Find(point => point.Stamp.ProjectId == projectId && point.Baseline == identity);
        if (found is not null) return Pin(found);
        var path = sources.BaselineFwData(projectId, identity);
        return path is null || !File.Exists(path)
            ? Refuse(RefusalCodes.HandAnalysisSnapshotExpired)
            : await BuildAsync(projectId, path, identity, ct);
    }

    private static string? LiveStamp(string? path)
    {
        if (path is null) return null;
        var info = new FileInfo(path);
        return info.Exists ? info.Length + ":" + info.LastWriteTimeUtc.Ticks : null;
    }

    private async ValueTask<CommandOutcome<IHandAnalysisSnapshot>> BuildAsync(Guid projectId, string? path,
        BaselineSemanticIdentity? baseline, CancellationToken ct, string? liveStamp = null)
    {
        if (path is null || !File.Exists(path))
            return Refuse(RefusalCodes.HandAnalysisIndexUnavailable, (HandAnalysisRefusalFacts.Cause, HandAnalysisRefusalFacts.Unavailable));
        ApprovedReadingSet set;
        try
        {
            Interlocked.Increment(ref _loads);
            set = await Task.Run(() =>
            {
                using var copy = baseline is null ? BaselineReadCache.OpenWithCoordinatedCopy(path, ct) : BaselineReadCache.Open(path);
                ct.ThrowIfCancellationRequested();
                return ApprovedReadingSet.Read(copy.Cache, ct);
            }, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            return Refuse(RefusalCodes.HandAnalysisIndexUnavailable, (HandAnalysisRefusalFacts.Cause, HandAnalysisRefusalFacts.Unavailable));
        }
        catch (Exception error) when (error is InvalidDataException or System.Xml.XmlException
                                          or LcmInitializationException or LcmCacheUnusableException)
        {
            return Refuse(RefusalCodes.HandAnalysisIndexUnavailable, (HandAnalysisRefusalFacts.Cause, HandAnalysisRefusalFacts.Corrupt));
        }

        ct.ThrowIfCancellationRequested();
        var home = baseline?.SemanticSnapshotDigest ?? "approvals:" + set.ContentDigest();
        var id = "hand-analysis/" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            string.Join('|', projectId, home, ComparisonVersion, Schema))));
        var stamp = new SnapshotStamp(id, projectId, home, "none", "none", "none", ComparisonVersion, Schema, SnapshotVerification.Full);
        var point = new FwHandAnalysisSnapshot.Point(stamp, baseline, set, liveStamp);
        lock (_gate)
        {
            var existing = _points.Find(item => item.Stamp.ProjectId == projectId && item.Stamp.SnapshotId == id);
            if (existing is not null)
            {
                if (liveStamp is null) return Pin(existing);
                _points.Remove(existing);
            }

            _points.Insert(0, point);
            foreach (var old in _points.Where(item => item.Stamp.ProjectId == projectId).Skip(Retained).ToArray())
                _points.Remove(old);
        }

        return Pin(point);
    }

    private FwHandAnalysisSnapshot.Point? Find(Func<FwHandAnalysisSnapshot.Point, bool> match)
    {
        lock (_gate) return _points.Find(item => match(item));
    }

    private static CommandOutcome<IHandAnalysisSnapshot> Pin(FwHandAnalysisSnapshot.Point point) =>
        CommandOutcome<IHandAnalysisSnapshot>.Success(new FwHandAnalysisSnapshot(point));

    internal static CommandOutcome<IHandAnalysisSnapshot> Refuse(string code, params (string Key, string Value)[] facts) =>
        CommandOutcome<IHandAnalysisSnapshot>.Refused(Refusal(code, facts));

    internal static Refusal Refusal(string code, params (string Key, string Value)[] facts) =>
        new(code, RefusalCatalog.Specs[code].DefaultReason, "The hand-analysis read was refused.",
            facts.Length == 0 ? null : facts.ToDictionary(fact => fact.Key, fact => fact.Value));
}
