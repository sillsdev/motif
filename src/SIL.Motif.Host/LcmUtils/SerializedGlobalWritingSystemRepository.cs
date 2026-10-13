using System.Security.Cryptography;
using System.Text;
using SIL.LCModel.Core.WritingSystems;
using SIL.WritingSystems;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Host.LcmUtils;

/// <summary>
/// A <see cref="CoreGlobalWritingSystemRepository"/> whose access is serialized across processes sharing its store,
/// and whose <c>Set</c> of an existing id replaces it.
/// </summary>
/// <remarks>
/// Disposing a <see cref="SIL.LCModel.LcmCache"/> saves its local writing-system store, and each save
/// reaches the shared repository through file-backed reads and writes. A process-local lock cannot protect Motif
/// processes using the same repository path. A cross-process lock derived from that path guards construction and each
/// access, as pinned by <c>ConcurrentSampleBuildersKeepTheSharedWritingSystemRepositoryReadable</c>.
/// </remarks>
internal class SerializedGlobalWritingSystemRepository : CoreGlobalWritingSystemRepository
{
    private static readonly object RepositoryAccessGate = new();
    private readonly WorkerMutexOwner _repositoryWriteGate;
    private int _gateDepth;

    protected SerializedGlobalWritingSystemRepository(WorkerMutexOwner repositoryWriteGate)
    {
        _repositoryWriteGate = repositoryWriteGate;
    }

    internal static SerializedGlobalWritingSystemRepository Create() =>
        Create(repositoryWriteGate => new SerializedGlobalWritingSystemRepository(repositoryWriteGate));

    internal static TRepository Create<TRepository>(Func<WorkerMutexOwner, TRepository> createRepository)
        where TRepository : SerializedGlobalWritingSystemRepository
    {
        var repositoryPath = GlobalWritingSystemRepository<CoreWritingSystemDefinition>.CurrentVersionPath(
            GlobalWritingSystemRepository<CoreWritingSystemDefinition>.DefaultBasePath);
        var gate = new WorkerMutexOwner(GetRepositoryLockName(repositoryPath));
        var transferred = false;

        try
        {
            while (!gate.TryAcquire()) Thread.Sleep(10);

            var repository = createRepository(gate);
            gate.Release();
            transferred = true;
            return repository;
        }
        finally
        {
            if (!transferred) gate.Dispose();
        }
    }

    /// <summary>Returns the cross-process lock name for a writing-system repository path.</summary>
    internal static string GetRepositoryLockName(string repositoryPath)
    {
        var normalizedPath = ResolveRepositoryPath(repositoryPath);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            normalizedPath = normalizedPath.ToUpperInvariant();
        var repositoryIdentity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath)));
        return "Motif.SerializedGlobalWritingSystemRepository." + repositoryIdentity;
    }

    private static string ResolveRepositoryPath(string repositoryPath)
    {
        var fullPath = Path.GetFullPath(repositoryPath);
        Directory.CreateDirectory(fullPath);

        var root = Path.GetPathRoot(fullPath)!;
        var resolvedPath = root;
        var pathParts = fullPath[root.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in pathParts)
        {
            var candidate = Path.Combine(resolvedPath, part);
            var target = new DirectoryInfo(candidate).ResolveLinkTarget(returnFinalTarget: true);
            resolvedPath = Path.GetFullPath(target?.FullName ?? candidate);
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(resolvedPath));
    }

    public override bool CanSave(CoreWritingSystemDefinition ws) =>
        WithRepositoryGate(() => base.CanSave(ws));

    public override void Conflate(string fromLanguageTag, string toLanguageTag) =>
        WithRepositoryGate(() => base.Conflate(fromLanguageTag, toLanguageTag));

    public override bool Contains(string languageTag) =>
        WithRepositoryGate(() => base.Contains(languageTag));

    protected override IWritingSystemFactory<CoreWritingSystemDefinition> CreateWritingSystemFactory() =>
        WithRepositoryGate(base.CreateWritingSystemFactory);

    public override CoreWritingSystemDefinition Get(string languageTag) =>
        WithRepositoryGate(() => base.Get(languageTag));

    public override IEnumerable<CoreWritingSystemDefinition> AllWritingSystems =>
        WithRepositoryGate(() => base.AllWritingSystems.ToArray());

    public override int Count => WithRepositoryGate(() => base.Count);

    public override void Remove(string languageTag) =>
        WithRepositoryGate(() => base.Remove(languageTag));

    protected override void RemoveDefinition(CoreWritingSystemDefinition ws) =>
        WithRepositoryGate(() => base.RemoveDefinition(ws));

    public override void Save() => WithRepositoryGate(base.Save);

    /// <inheritdoc />
    public override void Set(CoreWritingSystemDefinition ws)
    {
        WithRepositoryGate(() =>
        {
            if (base.Contains(ws.LanguageTag)) base.Replace(ws.LanguageTag, ws);
            else
            {
                base.Set(ws);
                // Flush a new ID before another process refreshes this repository's file statistics.
                base.Save();
            }
        });
    }

    /// <inheritdoc />
    public override void Replace(string languageTag, CoreWritingSystemDefinition newWs)
    {
        WithRepositoryGate(() => base.Replace(languageTag, newWs));
    }

    public override bool TryGet(string languageTag, out CoreWritingSystemDefinition ws)
    {
        CoreWritingSystemDefinition? found = null;
        var result = WithRepositoryGate(() => base.TryGet(languageTag, out found!));
        ws = found!;
        return result;
    }

    public override bool WritingSystemIdHasChanged(string languageTag) =>
        WithRepositoryGate(() => base.WritingSystemIdHasChanged(languageTag));

    public override string WritingSystemIdHasChangedTo(string oldLanguageTag) =>
        WithRepositoryGate(() => base.WritingSystemIdHasChangedTo(oldLanguageTag));

    private void WithRepositoryGate(Action operation) =>
        WithRepositoryGate(() =>
        {
            operation();
            return true;
        });

    private T WithRepositoryGate<T>(Func<T> operation)
    {
        lock (RepositoryAccessGate)
        {
            var outermost = _gateDepth == 0;
            if (outermost)
                while (!_repositoryWriteGate.TryAcquire()) Thread.Sleep(10);

            _gateDepth++;
            try
            {
                return operation();
            }
            finally
            {
                _gateDepth--;
                if (_gateDepth == 0) _repositoryWriteGate.Release();
            }
        }
    }
}
