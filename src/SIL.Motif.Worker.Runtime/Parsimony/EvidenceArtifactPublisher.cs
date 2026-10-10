using SIL.Motif.Contract.Baselines;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Parsimony;

/// <summary>Publication checkpoints used to exercise crash windows without changing file ownership.</summary>
public enum EvidencePublicationCheckpoint
{
    BeforeFileClose,
    BeforeRename,
    BeforeStoreCommit,
    AfterStoreCommit,
    AfterWorkListing,
    BeforeWorkDelete
}

/// <summary>Injects a deterministic failure at one evidence publication checkpoint.</summary>
public interface IEvidencePublicationHooks
{
    /// <summary>Runs at one named checkpoint while publishing a closed bundle.</summary>
    void Reach(EvidencePublicationCheckpoint checkpoint, string path);
}

/// <summary>What the expired-claim recovery did with one registered path.</summary>
public enum OrphanDiscard
{
    Removed,
    Absent,
    Link,
    NotADirectory,
    OutsideParsimonyRoot
}

/// <summary>What startup recovery did with one entry under the Parsimony work root.</summary>
public enum WorkDirectoryDiscard
{
    Removed,
    OwnedByLiveClaim,
    NotADirectory,
    Link,
    OutsideWorkRoot
}

/// <summary>Stages closed evidence files and publishes each immutable pair with a same-volume directory rename.</summary>
public sealed class EvidenceArtifactPublisher
{
    private readonly WorkspaceOwnership _ownership;
    private readonly string _parsimonyRoot;
    private readonly IEvidencePublicationHooks? _hooks;

    /// <summary>Creates the publisher below the worker-owned project directory.</summary>
    public EvidenceArtifactPublisher(string workerRoot, string projectKey,
        IEvidencePublicationHooks? hooks = null)
    {
        _ownership = WorkspaceOwnership.Bootstrap(workerRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectKey);
        _parsimonyRoot = ParsimonyArtifactPath.ProjectRoot(_ownership.WorkerRoot, projectKey);
        _hooks = hooks;
        CreateOwnedDirectory(_parsimonyRoot);
    }

    /// <summary>Creates a worker-owned temporary directory for one build attempt.</summary>
    public string CreateWorkingDirectory(string bundleId)
    {
        var root = Path.Combine(_parsimonyRoot, "w");
        CreateOwnedDirectory(root);
        var path = WorkingDirectory(bundleId);
        Directory.CreateDirectory(path);
        EnsureOwnedDirectory(path);
        return path;
    }

    /// <summary>Returns the worker-owned path reserved for one build attempt.</summary>
    public string WorkingDirectory(string bundleId)
    {
        ValidatePathComponent(bundleId, nameof(bundleId));
        return Path.Combine(WorkRoot, ParsimonyArtifactPath.BundleDirectoryName(bundleId));
    }

    /// <summary>The worker-owned root that holds one working directory per build attempt.</summary>
    public string WorkRoot => Path.Combine(_parsimonyRoot, "w");

    /// <summary>
    /// Lists every entry under the work root without following any of them. A link or unowned root throws.
    /// </summary>
    public IReadOnlyList<string> ListWorkingDirectories()
    {
        var root = WorkRoot;
        if (!Directory.Exists(root) && !IsLink(root)) return [];
        EnsureOwnedDirectory(root);
        var entries = Directory.EnumerateFileSystemEntries(root).Order(StringComparer.Ordinal).ToArray();
        _hooks?.Reach(EvidencePublicationCheckpoint.AfterWorkListing, Path.GetFullPath(root));
        return entries;
    }

    /// <summary>
    /// Removes one listed work entry unless a live claim owns it, re-checked after the listing and just before the
    /// delete. Links are refused without being followed, and only a directory directly under the work root is removed.
    /// </summary>
    public WorkDirectoryDiscard DiscardWorkingDirectory(string entry, Func<string, bool> ownedByLiveClaim)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry);
        ArgumentNullException.ThrowIfNull(ownedByLiveClaim);
        var path = Path.GetFullPath(entry);
        // Case-insensitive: a root spelled with other case still matches its listing on Windows and macOS.
        if (!StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(path), Path.GetFullPath(WorkRoot)))
            return WorkDirectoryDiscard.OutsideWorkRoot;
        _hooks?.Reach(EvidencePublicationCheckpoint.BeforeWorkDelete, path);
        if (ownedByLiveClaim(path)) return WorkDirectoryDiscard.OwnedByLiveClaim;
        if (IsLink(path)) return WorkDirectoryDiscard.Link;
        if (!_ownership.IsOwned(path)) return WorkDirectoryDiscard.OutsideWorkRoot;
        if (File.Exists(path)) return WorkDirectoryDiscard.NotADirectory;
        if (Directory.Exists(path)) DeleteTree(path);
        return WorkDirectoryDiscard.Removed;
    }

    // Detects a link without following it, so a dangling link is seen too; Exists is false for one.
    private static bool IsLink(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        if (info.LinkTarget is not null) return true;
        return info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0;
    }

    /// <summary>Creates a staging directory beside the final location for one exact material key.</summary>
    public string CreateStagingDirectory(string bundleId, BaselineToken token, string modelFingerprint,
        string materialKey, string inputKind = "baseline", string? candidateIdentity = null)
    {
        var staging = StagingDirectory(bundleId, token, modelFingerprint, materialKey, inputKind, candidateIdentity);
        var parent = Path.GetDirectoryName(staging)!;
        CreateOwnedDirectory(parent);
        Directory.CreateDirectory(staging);
        EnsureOwnedDirectory(staging);
        return staging;
    }

    /// <summary>Returns the same-volume staging path for one exact material key.</summary>
    public string StagingDirectory(string bundleId, BaselineToken token, string modelFingerprint, string materialKey,
        string inputKind = "baseline", string? candidateIdentity = null)
    {
        ValidatePathComponent(bundleId, nameof(bundleId));
        ArgumentNullException.ThrowIfNull(token);
        var parent = MaterialDirectory(token, modelFingerprint, materialKey, inputKind, candidateIdentity);
        return Path.Combine(parent, ParsimonyArtifactPath.StagingDirectoryName(bundleId));
    }

    /// <summary>Returns the immutable destination for the exact Baseline material key.</summary>
    public string FinalDirectory(string bundleId, BaselineToken token, string modelFingerprint, string materialKey,
        string inputKind = "baseline", string? candidateIdentity = null)
    {
        ValidatePathComponent(bundleId, nameof(bundleId));
        ArgumentNullException.ThrowIfNull(token);
        var parent = MaterialDirectory(token, modelFingerprint, materialKey, inputKind, candidateIdentity);
        return Path.Combine(parent, ParsimonyArtifactPath.BundleDirectoryName(bundleId));
    }

    private string MaterialDirectory(BaselineToken token, string modelFingerprint, string materialKey,
        string inputKind, string? candidateIdentity)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ParsimonyArtifactPath.MaterialDirectory(_parsimonyRoot, token.BundleDigest, modelFingerprint,
            materialKey, inputKind, candidateIdentity);
    }

    /// <summary>Flushes and closes the two required files before publication.</summary>
    public void FlushAndClose(string stagingDirectory)
    {
        var directory = Path.GetFullPath(stagingDirectory);
        EnsureOwnedDirectory(directory);
        var files = Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal).ToArray();
        var expected = new[] { Path.Combine(directory, "evidence.sqlite"),
            Path.Combine(directory, "grammar-facts.sqlite") }.Order(StringComparer.Ordinal).ToArray();
        if (!files.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidDataException("A Parsimony staging directory must contain exactly its two SQLite files.");
        foreach (var path in expected)
        {
            if (!File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("A Parsimony artifact file is missing or uses a reparse point.");
            using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            _hooks?.Reach(EvidencePublicationCheckpoint.BeforeFileClose, path);
            file.Flush(flushToDisk: true);
        }
    }

    /// <summary>Moves a complete staging directory to a fresh immutable bundle directory.</summary>
    public string Publish(string stagingDirectory, string bundleId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        ValidatePathComponent(bundleId, nameof(bundleId));
        var source = Path.GetFullPath(stagingDirectory);
        EnsureOwnedDirectory(source);
        if (!StringComparer.Ordinal.Equals(Path.GetFileName(source), ParsimonyArtifactPath.StagingDirectoryName(bundleId)))
            throw new IOException("The Parsimony staging directory does not match its bundle identity.");
        var parent = Path.GetDirectoryName(source)!;
        var destination = Path.Combine(parent, ParsimonyArtifactPath.BundleDirectoryName(bundleId));
        EnsureOwnedDirectory(parent);
        if (Directory.Exists(destination) || File.Exists(destination))
            throw new IOException("The Parsimony bundle destination already exists.");
        _hooks?.Reach(EvidencePublicationCheckpoint.BeforeRename, source);
        Directory.Move(source, destination);
        EnsureOwnedDirectory(destination);
        return destination;
    }

    /// <summary>Removes an unpublished worker-owned attempt without following links.</summary>
    public void Discard(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return;
        var path = Path.GetFullPath(directory);
        if (!IsParsimonyPath(path) || !_ownership.IsOwned(path) || !Directory.Exists(path)) return;
        DeleteTree(path);
    }

    /// <summary>Removes a registered orphan only when it is a real directory below this project's Parsimony root.</summary>
    public OrphanDiscard DiscardOrphan(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory)) return OrphanDiscard.Absent;
        var path = Path.GetFullPath(directory);
        if (!IsParsimonyPath(path)) return OrphanDiscard.OutsideParsimonyRoot;
        if (IsLink(path)) return OrphanDiscard.Link;
        if (!_ownership.IsOwned(path)) return OrphanDiscard.OutsideParsimonyRoot;
        if (Directory.Exists(path))
        {
            DeleteTree(path);
            return OrphanDiscard.Removed;
        }
        return File.Exists(path) ? OrphanDiscard.NotADirectory : OrphanDiscard.Absent;
    }

    /// <summary>Runs the store-commit checkpoint for an otherwise fully validated bundle.</summary>
    public void BeforeStoreCommit(string directory) =>
        _hooks?.Reach(EvidencePublicationCheckpoint.BeforeStoreCommit, Path.GetFullPath(directory));

    /// <summary>Runs after the paired-store transaction commits.</summary>
    public void AfterStoreCommit(string directory) =>
        _hooks?.Reach(EvidencePublicationCheckpoint.AfterStoreCommit, Path.GetFullPath(directory));

    private bool IsParsimonyPath(string path)
    {
        var relative = Path.GetRelativePath(_parsimonyRoot, path);
        return relative is not ("." or "..") && !relative.StartsWith(".." + Path.DirectorySeparatorChar,
                   StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private void CreateOwnedDirectory(string path)
    {
        Directory.CreateDirectory(path);
        EnsureOwnedDirectory(path);
    }

    private void EnsureOwnedDirectory(string path)
    {
        if (!_ownership.IsOwned(path) || !Directory.Exists(path) ||
            (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A Parsimony artifact path is outside the owned worker root or uses a reparse point.");
    }

    private void DeleteTree(string path)
    {
        EnsureOwnedDirectory(path);
        foreach (var entry in Directory.EnumerateFileSystemEntries(path))
        {
            if (!IsLexicallyContained(path, entry) || !_ownership.IsOwned(entry))
                throw new IOException("A Parsimony artifact entry is outside its registered directory.");
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A Parsimony artifact reparse point is refused.");
            if ((attributes & FileAttributes.Directory) != 0) DeleteTree(entry);
            else File.Delete(entry);
        }
        EnsureOwnedDirectory(path);
        Directory.Delete(path);
    }

    private static bool IsLexicallyContained(string root, string candidate)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(candidate));
        return relative is not ("." or "..") && !relative.StartsWith(".." + Path.DirectorySeparatorChar,
                   StringComparison.Ordinal) && !Path.IsPathRooted(relative);
    }

    private static void ValidatePathComponent(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value is "." or ".." || value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.Contains(Path.DirectorySeparatorChar) || value.Contains(Path.AltDirectorySeparatorChar))
            throw new ArgumentException("A bundle identity must be one path component.", parameterName);
    }

}
