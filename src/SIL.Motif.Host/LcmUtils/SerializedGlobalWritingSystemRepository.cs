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
/// <para>
/// That lock cannot cover every reader. A project's own store copies a shared definition as the template for a
/// new writing system after the lock is released, so a shared file must never be missing while the lock is held
/// either. The inherited <c>Replace</c> deletes the live file and later moves a stashed copy back over the saved
/// one, which both opens that gap and discards the replacement. A replacement that keeps its writing system's
/// identity therefore takes the old definition's place and is saved, and the save swaps the new file in, as
/// pinned by <c>ReplacingASharedWritingSystemSwapsItsFileWithoutRemovingIt</c>. A replacement whose content
/// matches the stored definition, apart from its date, writes nothing, so the shared file keeps its date and is
/// not offered to other projects as newer, as pinned by
/// <c>ReplacingASharedWritingSystemWithAnEqualDefinitionLeavesItsFileUntouched</c>. A replacement under another tag
/// removes the old definition and saves the new one without restoring the old file, as pinned by
/// <c>ReplacingASharedWritingSystemUnderANewTagMovesItsFile</c>.
/// </para>
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
            if (base.Contains(ws.LanguageTag)) ReplaceUnderGate(ws.LanguageTag, ws);
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
        WithRepositoryGate(() => ReplaceUnderGate(languageTag, newWs));
    }

    private void ReplaceUnderGate(string languageTag, CoreWritingSystemDefinition newWs)
    {
        var held = base.Contains(languageTag);
        var keepsIdentity = held && newWs.Id == languageTag
            && IetfLanguageTag.AreTagsEquivalent(languageTag, newWs.LanguageTag);
        if (!keepsIdentity)
        {
            // The old tag's file goes with it; the inherited stash would move it back after the save.
            if (held) base.Remove(languageTag);
            base.Set(newWs);
            base.Save();
            return;
        }

        var stored = WritingSystems[languageTag];
        if (ReferenceEquals(stored, newWs))
        {
            base.Save();
            return;
        }

        if (HasSameContent(stored, newWs)) return;

        // The id is already held by the old definition, so its dictionary entry is swapped, not added.
        WritingSystems[languageTag] = newWs;
        // Marked changed so the replacement is written over the file that holds the old definition.
        newWs.ForceChanged();
        base.Save();
    }

    // A definition that differs only in its date is left alone, so the shared file keeps its date and bytes.
    private static bool HasSameContent(CoreWritingSystemDefinition stored, CoreWritingSystemDefinition candidate)
    {
        var probe = new CoreWritingSystemDefinition(stored, cloneId: true) { DateModified = candidate.DateModified };
        return probe.ValueEquals(candidate);
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
