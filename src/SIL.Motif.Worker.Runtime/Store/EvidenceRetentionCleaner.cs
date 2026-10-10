using System.Globalization;
using SIL.Motif.Contract.Jobs;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Parsimony;

namespace SIL.Motif.Worker.Store;

/// <summary>Reports bundle files removed and material retained by durable pins.</summary>
public sealed record EvidenceRetentionResult(
    IReadOnlyList<string> DeletedBundleIds,
    IReadOnlyList<WorkspaceCleanupFailure> Failures,
    long RetainedPinnedBytes,
    IReadOnlyDictionary<string, int> PinReasonCounts);

/// <summary>Removes only old, unpinned bundles registered to one worker-owned project.</summary>
public sealed class EvidenceRetentionCleaner
{
    private readonly EvidenceArtifactRepository _artifacts;
    private readonly IWorkspaceOwnership _ownership;
    private readonly IJobClock _clock;
    private readonly IWorkspaceFileSystem _fileSystem;
    private readonly int _retainedUnpinnedCount;

    /// <summary>Creates a cleaner with the recommended twenty unpinned bundles per project.</summary>
    public EvidenceRetentionCleaner(EvidenceArtifactRepository artifacts, IWorkspaceOwnership ownership,
        IJobClock? clock = null, IWorkspaceFileSystem? fileSystem = null, int retainedUnpinnedCount = 20)
    {
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _clock = clock ?? new SystemJobClock();
        _fileSystem = fileSystem ?? new LocalWorkspaceFileSystem();
        if (retainedUnpinnedCount < 0) throw new ArgumentOutOfRangeException(nameof(retainedUnpinnedCount));
        _retainedUnpinnedCount = retainedUnpinnedCount;
    }

    /// <summary>Retains current and pinned material plus the newest unpinned bundles up to the configured count.</summary>
    public EvidenceRetentionResult Clean(string projectKey)
    {
        var items = _artifacts.ListForRetention(projectKey);
        var deleted = new List<string>();
        var failures = new List<WorkspaceCleanupFailure>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        long pinnedBytes = 0;
        foreach (var item in items.Where(item => item.IsCurrent || item.PinReasons.Count > 0))
        {
            var reasons = item.IsCurrent ? item.PinReasons.Append("current-baseline-material") : item.PinReasons;
            foreach (var reason in reasons.Distinct(StringComparer.Ordinal))
                counts[reason] = counts.GetValueOrDefault(reason) + 1;
            pinnedBytes = checked(pinnedBytes + DirectoryBytes(item.Bundle.EvidenceDirectory));
        }

        var eligible = items.Where(item => !item.IsCurrent && item.PinReasons.Count == 0)
            .Where(item => item.Bundle.State is "available" or "deleting")
            .OrderByDescending(item => item.Bundle.CreatedUtc, StringComparer.Ordinal)
            .ThenByDescending(item => item.Bundle.BundleId, StringComparer.Ordinal)
            .Skip(_retainedUnpinnedCount).ToArray();
        foreach (var item in eligible)
        {
            var bundle = item.Bundle;
            if (!_artifacts.TryMarkDeleting(bundle.BundleId, _clock.UtcNow)) continue;
            if (!ValidateRegisteredPath(projectKey, bundle, out var error))
            {
                failures.Add(new WorkspaceCleanupFailure(bundle.EvidenceDirectory, error!));
                continue;
            }
            try
            {
                if (_fileSystem.Exists(bundle.EvidenceDirectory)) DeleteTree(bundle.EvidenceDirectory);
                if (_artifacts.CompleteDeletion(bundle.BundleId, _clock.UtcNow)) deleted.Add(bundle.BundleId);
                else failures.Add(new WorkspaceCleanupFailure(bundle.EvidenceDirectory,
                    "The bundle remained pinned or changed while deletion was finishing."));
            }
            catch (Exception exception)
            {
                failures.Add(new WorkspaceCleanupFailure(bundle.EvidenceDirectory,
                    "The Parsimony bundle could not be deleted and remains registered for retry.", exception));
            }
        }
        return new EvidenceRetentionResult(deleted, failures, pinnedBytes, counts);
    }

    /// <summary>
    /// Removes staging and renamed directories whose durable writer claim has expired, then any work directory
    /// that no live claim owns, such as one left after its publication committed.
    /// </summary>
    public WorkspaceCleanupResult RecoverExpiredBuilds(string projectKey, EvidenceArtifactPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);
        var deleted = new List<string>();
        var failures = new List<WorkspaceCleanupFailure>();
        var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in _artifacts.ListExpiredBuildClaims(projectKey))
        {
            var paths = new[] { claim.StagingDirectory, claim.FinalDirectory, claim.WorkingDirectory }
                .Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.Ordinal).ToArray();
            var cleaned = true;
            foreach (var path in paths)
            {
                handled.Add(Path.GetFullPath(path!));
                try
                {
                    switch (publisher.DiscardOrphan(path))
                    {
                        case OrphanDiscard.Removed:
                            deleted.Add(path!);
                            break;
                        case OrphanDiscard.Absent:
                            break;
                        case OrphanDiscard.Link:
                            failures.Add(new WorkspaceCleanupFailure(path!,
                                "The abandoned Parsimony path is a link and is refused without being followed."));
                            cleaned = false;
                            break;
                        case OrphanDiscard.NotADirectory:
                            failures.Add(new WorkspaceCleanupFailure(path!,
                                "The abandoned Parsimony path is not a directory and was left in place."));
                            cleaned = false;
                            break;
                        default:
                            failures.Add(new WorkspaceCleanupFailure(path!,
                                "The abandoned Parsimony path is outside its registered project root."));
                            cleaned = false;
                            break;
                    }
                }
                catch (Exception exception)
                {
                    failures.Add(new WorkspaceCleanupFailure(path!,
                        "The abandoned Parsimony path could not be removed.", exception));
                    cleaned = false;
                }
            }
            if (cleaned) _artifacts.RemoveBuildClaim(claim.BundleId);
        }
        RecoverUnclaimedWorkDirectories(projectKey, publisher, handled, deleted, failures);
        return new WorkspaceCleanupResult(deleted, failures);
    }

    private void RecoverUnclaimedWorkDirectories(string projectKey, EvidenceArtifactPublisher publisher,
        ISet<string> handled, List<string> deleted, List<WorkspaceCleanupFailure> failures)
    {
        IReadOnlyList<string> entries;
        try
        {
            // Listing comes first: a directory found here had its claim inserted before it was created.
            entries = publisher.ListWorkingDirectories();
        }
        catch (Exception exception)
        {
            failures.Add(new WorkspaceCleanupFailure(publisher.WorkRoot,
                "The Parsimony work root could not be listed and was left in place.", exception));
            return;
        }
        foreach (var entry in entries)
        {
            var path = Path.GetFullPath(entry);
            if (handled.Contains(path)) continue;
            try
            {
                switch (publisher.DiscardWorkingDirectory(path,
                    candidate => IsOwnedByLiveClaim(projectKey, publisher.WorkRoot, candidate)))
                {
                    case WorkDirectoryDiscard.Removed:
                        deleted.Add(path);
                        break;
                    case WorkDirectoryDiscard.OwnedByLiveClaim:
                        break;
                    case WorkDirectoryDiscard.Link:
                        failures.Add(new WorkspaceCleanupFailure(path,
                            "The Parsimony work entry is a link and is refused without being followed."));
                        break;
                    case WorkDirectoryDiscard.NotADirectory:
                        failures.Add(new WorkspaceCleanupFailure(path,
                            "The Parsimony work path is not a working directory and was left in place."));
                        break;
                    default:
                        failures.Add(new WorkspaceCleanupFailure(path,
                            "The Parsimony work path is outside the work root and was left in place."));
                        break;
                }
            }
            catch (Exception exception)
            {
                failures.Add(new WorkspaceCleanupFailure(path,
                    "The abandoned Parsimony work path could not be removed.", exception));
            }
        }
    }

    // Stored paths may differ in case, so match on the bundle directory name, which is unique under work/.
    private bool IsOwnedByLiveClaim(string projectKey, string workRoot, string entry)
    {
        var claims = _artifacts.ListLiveBuildClaimWorkingDirectories(projectKey);
        foreach (var claim in claims)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(claim), Path.GetFullPath(workRoot)))
                throw new InvalidDataException("A live Parsimony claim is not directly under the work root.");
        }
        var name = Path.GetFileName(entry);
        return claims.Any(claim => StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(claim), name));
    }

    private bool ValidateRegisteredPath(string projectKey, ParsimonyBundleRecord bundle, out string? error)
    {
        error = null;
        string directory;
        try { directory = Path.GetFullPath(bundle.EvidenceDirectory); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            error = "The registered bundle path is malformed.";
            return false;
        }
        var projectRoot = ParsimonyArtifactPath.ProjectRoot(_ownership.WorkerRoot, projectKey);
        string expectedDirectory;
        try
        {
            expectedDirectory = Path.GetFullPath(ParsimonyArtifactPath.BundleDirectory(_ownership.WorkerRoot,
                projectKey, bundle.BaselineDigest, bundle.ModelFingerprint, bundle.MaterialKey, bundle.InputKind,
                bundle.CandidateIdentity, bundle.BundleId));
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            error = "The registered bundle identity is malformed.";
            return false;
        }
        var relative = Path.GetRelativePath(projectRoot, directory);
        if (!StringComparer.Ordinal.Equals(directory, expectedDirectory) || relative is "." or ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal) || Path.IsPathRooted(relative) ||
            !StringComparer.Ordinal.Equals(Path.GetFileName(directory),
                ParsimonyArtifactPath.BundleDirectoryName(bundle.BundleId)) ||
            !StringComparer.Ordinal.Equals(Path.GetFullPath(bundle.GrammarFactsPath),
                Path.Combine(directory, "grammar-facts.sqlite")) ||
            !StringComparer.Ordinal.Equals(Path.GetFullPath(bundle.EvidencePath),
                Path.Combine(directory, "evidence.sqlite")) || !_ownership.IsOwned(directory))
        {
            error = "The registered bundle path is outside its exact worker-owned project directory.";
            return false;
        }
        if (_fileSystem.Exists(directory) && (_fileSystem.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            error = "A Parsimony bundle reparse point is refused.";
            return false;
        }
        return true;
    }

    private long DirectoryBytes(string path)
    {
        if (!_fileSystem.Exists(path) || (_fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            return 0;
        if ((_fileSystem.GetAttributes(path) & FileAttributes.Directory) == 0)
            return new FileInfo(path).Length;
        long bytes = 0;
        foreach (var entry in _fileSystem.EnumerateFileSystemEntries(path))
        {
            if (!_ownership.IsOwned(entry) || (_fileSystem.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                continue;
            bytes = checked(bytes + DirectoryBytes(entry));
        }
        return bytes;
    }

    private void DeleteTree(string target)
    {
        if (!_ownership.IsOwned(target)) throw new IOException("A Parsimony entry is outside the worker-owned root.");
        if (!_fileSystem.Exists(target)) return;
        var attributes = _fileSystem.GetAttributes(target);
        if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("A Parsimony reparse point is refused.");
        if ((attributes & FileAttributes.Directory) != 0)
        {
            foreach (var entry in _fileSystem.EnumerateFileSystemEntries(target))
            {
                if (!IsLexicallyContained(target, entry) || !_ownership.IsOwned(entry))
                    throw new IOException("A Parsimony entry is outside the exact registered bundle directory.");
                DeleteTree(entry);
            }
            if ((_fileSystem.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A Parsimony reparse point appeared during cleanup.");
            _fileSystem.DeleteDirectory(target);
        }
        else _fileSystem.DeleteFile(target);
    }

    private static bool IsLexicallyContained(string root, string candidate)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(candidate));
        return relative is not ("." or "..") && !relative.StartsWith(".." + Path.DirectorySeparatorChar,
                   StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private sealed class LocalWorkspaceFileSystem : IWorkspaceFileSystem
    {
        public bool Exists(string path) => Directory.Exists(path) || File.Exists(path);
        public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
        public IReadOnlyList<string> EnumerateFileSystemEntries(string path) => Directory.GetFileSystemEntries(path);
        public void DeleteFile(string path) => File.Delete(path);
        public void DeleteDirectory(string path) => Directory.Delete(path, recursive: false);
    }
}
