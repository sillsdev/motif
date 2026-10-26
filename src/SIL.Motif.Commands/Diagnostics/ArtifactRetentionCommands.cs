using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.Win32.SafeHandles;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parsimony;
using SIL.Motif.Host.Store;

namespace SIL.Motif.Commands.Diagnostics;

/// <summary>Builds a read-only inventory from one worker root and its explicitly named project stores.</summary>
public static class ArtifactRetentionCommands
{
    private const int MaximumManifestBytes = 1024 * 1024;
    private const int MaximumStores = 100;
    private const int MaximumEntries = 10000;
    private const int MaximumDepth = 12;
    private const int MaximumReferenceRecords = 100000;
    private const int MaximumEvidenceJsonCharacters = 1024 * 1024;
    private const long MaximumSnapshotFileBytes = 2L * 1024 * 1024 * 1024;
    private const long MaximumSnapshotTotalBytes = 4L * 1024 * 1024 * 1024;
    private const string WorkerMarker = "SIL.Motif.WorkerRoot.v1";
    private const string PhysicalAliasRefused = "physical-alias-refused";
    private const string FileIdentityUnavailable = "file-identity-unavailable";
    private static readonly string[] Families = ["assessment-runs", "assessment-sources", "baselines"];

    /// <summary>Reads named database snapshots and the three generated-artifact families without changing them.</summary>
    public static ArtifactInventoryResponse Inventory(ArtifactInventoryRequest request) =>
        Inventory(request, MaximumEntries);

    internal static ArtifactInventoryResponse Inventory(ArtifactInventoryRequest request, int scanEntryBudget)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scanEntryBudget);
        var root = RequireDirectoryPath(request.RootPath, "root");
        var manifestPath = RequireFilePath(request.StoresManifestPath, "stores manifest");
        var manifest = ReadManifest(manifestPath);
        var diagnostics = new List<ArtifactInventoryDiagnostic>();
        var storeResults = manifest.Stores.Select(store => new ArtifactInventoryStore(
            store.DatabasePath, "not-read", "reference-domain-incomplete", null, null)).ToArray();
        var references = new ArtifactReferences();
        var rootOwned = HasWorkerMarker(root);
        if (!rootOwned)
            diagnostics.Add(new("unknown-worker-root", root));

        var referenceDomainComplete = manifest.ReferenceDomainComplete && rootOwned;
        var leases = new List<FileStream>();
        try
        {
            var lockFailure = ValidateStorePhysicalIdentities(manifest.Stores, storeResults, diagnostics) ??
                              AcquireStoreLeases(manifest.Stores, leases, storeResults, diagnostics);
            if (lockFailure is null)
            {
                using var snapshots = new PrivateSnapshotDirectory();
                var snapshotBudget = new SnapshotBudget();
                var referenceBudget = new ReferenceBudget();
                for (var index = 0; index < manifest.Stores.Count; index++)
                {
                    var locator = manifest.Stores[index];
                    try
                    {
                        var snapshot = SnapshotStore(locator, snapshots.PathFor(index), snapshotBudget);
                        var storeReferences = ReadSnapshot(locator, snapshot.DatabasePath, referenceBudget);
                        references.Merge(storeReferences);
                        storeResults[index] = new ArtifactInventoryStore(locator.DatabasePath,
                            "read", "snapshot-validated", MotifSchema.CurrentSchema, snapshot.Sha256);
                    }
                    catch (Exception exception) when (IsInventoryStoreFailure(exception))
                    {
                        referenceDomainComplete = false;
                        var code = StoreFailureCode(exception);
                        storeResults[index] = new ArtifactInventoryStore(locator.DatabasePath,
                            "incomplete", code, null, null);
                        diagnostics.Add(new(StoreDiagnosticCode(code), locator.DatabasePath));
                    }
                }
            }
            else
            {
                referenceDomainComplete = false;
            }

            if (!manifest.ReferenceDomainComplete)
                diagnostics.Add(new("reference-domain-not-declared-complete", null));
            if (references.IncompleteReasons.Count > 0)
                referenceDomainComplete = false;
            diagnostics.AddRange(references.IncompleteReasons
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .Select(reason => new ArtifactInventoryDiagnostic(reason, null)));

            var artifacts = ScanArtifacts(root, references, referenceDomainComplete, diagnostics, scanEntryBudget);
            diagnostics.AddRange(artifacts.Where(item => item.ReasonCode == "path-alias")
                .Select(item => new ArtifactInventoryDiagnostic("path-alias-refused", item.Path)));
            var inventoryComplete = !diagnostics.Any(IsScanDiagnostic);
            long? bytes = inventoryComplete && referenceDomainComplete ? SumLogicalBytes(artifacts) : null;
            return new ArtifactInventoryResponse(root, manifest.Sha256,
                manifest.ReferenceDomainComplete, referenceDomainComplete, inventoryComplete, bytes,
                storeResults.OrderBy(item => item.DatabasePath, PathComparer).ToArray(), artifacts, diagnostics
                    .Distinct()
                    .OrderBy(item => item.Code, StringComparer.Ordinal)
                    .ThenBy(item => item.Path, PathComparer)
                    .ToArray());
        }
        finally
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
    }

    private static bool IsScanDiagnostic(ArtifactInventoryDiagnostic diagnostic) =>
        diagnostic.Code is "scan-depth-limit" or "scan-entry-limit" or "path-unreadable" or
            "link-refused" or "path-not-directory" or "path-alias-refused" or PhysicalAliasRefused or
            FileIdentityUnavailable or "changed-during-inventory";

    private static long? SumLogicalBytes(IEnumerable<ArtifactInventoryEntry> artifacts)
    {
        long total = 0;
        foreach (var artifact in artifacts)
        {
            if (artifact.LogicalBytes is not { } bytes || total > long.MaxValue - bytes) return null;
            total += bytes;
        }
        return total;
    }

    private static string? ValidateStorePhysicalIdentities(IReadOnlyList<StoreLocator> stores,
        ArtifactInventoryStore[] storeResults, List<ArtifactInventoryDiagnostic> diagnostics)
    {
        var failures = new Dictionary<int, string>();
        var owners = new Dictionary<(ulong VolumeId, ulong FileId), HashSet<int>>();
        var identityUnavailable = false;
        for (var index = 0; index < stores.Count; index++)
        {
            var databasePath = stores[index].DatabasePath;
            var paths = new List<string> { databasePath };
            var lockPath = databasePath + ".use.lock";
            if (EntryExists(lockPath)) paths.Add(lockPath);
            foreach (var suffix in new[] { "-wal", "-shm" })
            {
                var sidecarPath = databasePath + suffix;
                if (EntryExists(sidecarPath)) paths.Add(sidecarPath);
            }

            foreach (var path in paths)
            {
                if (IsLink(path))
                {
                    failures[index] = "link-refused";
                    continue;
                }

                try
                {
                    var identity = ReadPhysicalFileIdentity(path);
                    if (identity.LinkCount > 1) failures[index] = PhysicalAliasRefused;
                    var fileObject = (identity.VolumeId, identity.FileId);
                    if (!owners.TryGetValue(fileObject, out var priorOwners))
                        owners[fileObject] = priorOwners = [];
                    if (priorOwners.Count > 0)
                    {
                        failures[index] = PhysicalAliasRefused;
                        foreach (var prior in priorOwners) failures[prior] = PhysicalAliasRefused;
                    }
                    priorOwners.Add(index);
                }
                catch (NotSupportedException)
                {
                    identityUnavailable = true;
                    break;
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    failures[index] = FileIdentityUnavailable;
                }
            }
        }

        if (identityUnavailable)
            for (var index = 0; index < stores.Count; index++)
                failures.TryAdd(index, FileIdentityUnavailable);

        if (failures.Count == 0) return null;
        foreach (var (index, reason) in failures.OrderBy(pair => pair.Key))
        {
            storeResults[index] = new ArtifactInventoryStore(stores[index].DatabasePath,
                "incomplete", reason, null, null);
            diagnostics.Add(new(StoreDiagnosticCode(reason), stores[index].DatabasePath));
        }
        for (var index = 0; index < storeResults.Length; index++)
            if (storeResults[index].Status == "not-read")
                storeResults[index] = storeResults[index] with { ReasonCode = "snapshot-not-attempted" };
        return "store-physical-identity-incomplete";
    }

    private static string StoreDiagnosticCode(string reason) => reason switch
    {
        PhysicalAliasRefused => "store-physical-alias-refused",
        FileIdentityUnavailable => "store-file-identity-unavailable",
        "link-refused" => "store-link-refused",
        _ => reason,
    };

    private static string? AcquireStoreLeases(IReadOnlyList<StoreLocator> stores, List<FileStream> leases,
        ArtifactInventoryStore[] storeResults, List<ArtifactInventoryDiagnostic> diagnostics)
    {
        var ordered = stores.Select((store, index) => (store, index))
            .OrderBy(item => item.store.DatabasePath, PathComparer).ToArray();
        foreach (var item in ordered)
        {
            var lockPath = item.store.DatabasePath + ".use.lock";
            if (!File.Exists(lockPath))
            {
                const string code = "missing-use-lock";
                storeResults[item.index] = new ArtifactInventoryStore(item.store.DatabasePath,
                    "busy", code, null, null);
                diagnostics.Add(new(code, lockPath));
                MarkUnreadStores(storeResults, item.index);
                return code;
            }

            try
            {
                RejectLink(lockPath);
                leases.Add(new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.None));
            }
            catch (IOException)
            {
                const string code = "active-store-use";
                storeResults[item.index] = new ArtifactInventoryStore(item.store.DatabasePath,
                    "busy", code, null, null);
                diagnostics.Add(new(code, lockPath));
                MarkUnreadStores(storeResults, item.index);
                return code;
            }
            catch (UnauthorizedAccessException)
            {
                const string code = "store-use-lock-unavailable";
                storeResults[item.index] = new ArtifactInventoryStore(item.store.DatabasePath,
                    "incomplete", code, null, null);
                diagnostics.Add(new(code, lockPath));
                MarkUnreadStores(storeResults, item.index);
                return code;
            }
        }

        return null;
    }

    private static void MarkUnreadStores(ArtifactInventoryStore[] stores, int failedIndex)
    {
        for (var index = 0; index < stores.Length; index++)
        {
            if (index == failedIndex || stores[index].Status != "not-read") continue;
            stores[index] = stores[index] with { ReasonCode = "snapshot-not-attempted" };
        }
    }

    private static StoreSnapshot SnapshotStore(StoreLocator locator, string snapshotDirectory,
        SnapshotBudget budget)
    {
        Directory.CreateDirectory(snapshotDirectory);
        var files = new List<SnapshotFile>();
        var databasePath = Path.Combine(snapshotDirectory, Path.GetFileName(locator.DatabasePath));
        CopyStable(locator.DatabasePath, databasePath, files, budget);
        var sidecars = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var sidecar = locator.DatabasePath + suffix;
            var exists = EntryExists(sidecar);
            sidecars.Add(suffix, exists);
            if (!exists) continue;
            RejectLink(sidecar);
            CopyStable(sidecar, databasePath + suffix, files, budget);
        }
        foreach (var (suffix, existed) in sidecars)
            if (EntryExists(locator.DatabasePath + suffix) != existed)
                throw new IOException("snapshot-source-changed");

        foreach (var file in files)
        {
            var physicalBeforeHash = ReadPhysicalFileIdentity(file.SourcePath);
            RequireSingleLink(physicalBeforeHash);
            if (ReadIdentity(file.SourcePath) != file.Identity || physicalBeforeHash != file.PhysicalIdentity ||
                HashFile(file.SourcePath) != file.Sha256 ||
                ReadPhysicalFileIdentity(file.SourcePath) != file.PhysicalIdentity)
                throw new IOException("snapshot-source-changed");
        }

        using (CreatePrivateSnapshotFile(databasePath + ".use.lock")) { }
        using (var recovery = new SqliteConnection(new SqliteConnectionStringBuilder
               {
                   DataSource = databasePath,
                   Mode = SqliteOpenMode.ReadWrite,
                   Pooling = false,
                   DefaultTimeout = 1,
               }.ToString()))
        {
            recovery.Open();
            using var command = recovery.CreateCommand();
            command.CommandText = "PRAGMA schema_version;";
            _ = command.ExecuteScalar();
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in files.OrderBy(item => item.Name, StringComparer.Ordinal))
        {
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(file.Name));
            hash.AppendData(Convert.FromHexString(file.Sha256));
        }

        return new StoreSnapshot(databasePath,
            "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    internal static void CopyStable(string sourcePath, string destinationPath,
        List<SnapshotFile> fingerprints, SnapshotBudget budget)
    {
        RejectLink(sourcePath);
        var before = ReadIdentity(sourcePath);
        var physicalBefore = ReadPhysicalFileIdentity(sourcePath);
        RequireSingleLink(physicalBefore);
        budget.Reserve(before.Length);

        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.SequentialScan);
        var physicalOpened = ReadPhysicalFileIdentity(source.SafeFileHandle);
        RequireSingleLink(physicalOpened);
        if (physicalOpened != physicalBefore) throw new IOException("snapshot-source-changed");
        using var destination = CreatePrivateSnapshotFile(destinationPath);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        int count;
        long copied = 0;
        while ((count = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            copied = checked(copied + count);
            if (copied > before.Length || copied > MaximumSnapshotFileBytes)
                throw new IOException("snapshot-source-changed");
            destination.Write(buffer, 0, count);
            hash.AppendData(buffer, 0, count);
        }
        destination.Flush(flushToDisk: true);
        var copiedHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        var after = ReadIdentity(sourcePath);
        var physicalAfterHandle = ReadPhysicalFileIdentity(source.SafeFileHandle);
        var physicalAfterPath = ReadPhysicalFileIdentity(sourcePath);
        RequireSingleLink(physicalAfterHandle);
        RequireSingleLink(physicalAfterPath);
        if (copied != before.Length || before != after || physicalBefore != physicalAfterHandle ||
            physicalBefore != physicalAfterPath || HashFile(sourcePath) != copiedHash)
            throw new IOException("snapshot-source-changed");
        fingerprints.Add(new SnapshotFile(sourcePath, Path.GetFileName(sourcePath), copiedHash, before,
            physicalBefore));
    }

    private static void RequireSingleLink(PhysicalFileIdentity identity)
    {
        if (identity.LinkCount > 1) throw new IOException(PhysicalAliasRefused);
        if (identity.LinkCount != 1) throw new IOException("snapshot-source-changed");
    }

    private static FileStream CreatePrivateSnapshotFile(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = 81920,
            Options = FileOptions.SequentialScan,
        };
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        else if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Private artifact snapshots are not supported on this platform.");

        FileStream? file = null;
        try
        {
            file = new FileStream(path, options);
            if (OperatingSystem.IsWindows())
                EnsureWindowsSnapshotAccess(path, isDirectory: false, requireProtectedRules: false,
                    CurrentWindowsUser());
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                EnsureUnixOwnerOnly(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            else
                throw new PlatformNotSupportedException("Private artifact snapshots are not supported on this platform.");
            return file;
        }
        catch
        {
            if (file is not null)
            {
                file.Dispose();
                try { File.Delete(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            throw;
        }
    }

    private static DirectoryInfo CreatePrivateSnapshotRoot(string path)
    {
        if (EntryExists(path)) throw new IOException("The private snapshot path already exists.");
        if (OperatingSystem.IsWindows()) return CreateWindowsPrivateSnapshotRoot(path);
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var ownerMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
            var directory = Directory.CreateDirectory(path, ownerMode);
            EnsureUnixOwnerOnly(path, ownerMode);
            return directory;
        }
        throw new PlatformNotSupportedException("Private artifact snapshots are not supported on this platform.");
    }

    [SupportedOSPlatform("windows")]
    private static DirectoryInfo CreateWindowsPrivateSnapshotRoot(string path)
    {
        var owner = CurrentWindowsUser();
        var security = new DirectorySecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        var directory = new DirectoryInfo(path);
        directory.Create(security);
        EnsureWindowsSnapshotAccess(path, isDirectory: true, requireProtectedRules: true, owner);
        return directory;
    }

    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier CurrentWindowsUser() => WindowsIdentity.GetCurrent().User ??
        throw new UnauthorizedAccessException("The current Windows user has no security identifier.");

    [SupportedOSPlatform("linux")]
    [SupportedOSPlatform("macos")]
    private static void EnsureUnixOwnerOnly(string path, UnixFileMode requiredOwnerMode)
    {
        var mode = File.GetUnixFileMode(path);
        var sharedMode = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                         UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        if ((mode & sharedMode) != UnixFileMode.None || (mode & requiredOwnerMode) != requiredOwnerMode)
            throw new UnauthorizedAccessException("The private snapshot path is not owner-only.");
    }

    [SupportedOSPlatform("windows")]
    private static void EnsureWindowsSnapshotAccess(string path, bool isDirectory, bool requireProtectedRules,
        SecurityIdentifier owner)
    {
        CommonObjectSecurity security = isDirectory
            ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        if (requireProtectedRules && !security.AreAccessRulesProtected ||
            !owner.Equals(security.GetOwner(typeof(SecurityIdentifier))))
            throw new UnauthorizedAccessException("The private snapshot path does not have a protected owner ACL.");

        var allows = security.GetAccessRules(includeExplicit: true, includeInherited: true,
                targetType: typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow)
            .ToArray();
        if (!allows.Any(rule => owner.Equals(rule.IdentityReference) &&
                                 (rule.FileSystemRights & FileSystemRights.FullControl) == FileSystemRights.FullControl) ||
            allows.Any(rule => !owner.Equals(rule.IdentityReference)))
            throw new UnauthorizedAccessException("The private snapshot path is accessible to another Windows user.");
    }

    private static string HashFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 81920, FileOptions.SequentialScan);
        if (stream.Length > MaximumSnapshotFileBytes)
            throw new InvalidDataException("snapshot-size-limit");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long countTotal = 0;
        int count;
        while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            countTotal = checked(countTotal + count);
            if (countTotal > MaximumSnapshotFileBytes)
                throw new InvalidDataException("snapshot-size-limit");
            hash.AppendData(buffer, 0, count);
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static PhysicalFileIdentity ReadPhysicalFileIdentity(string path)
    {
        if (OperatingSystem.IsLinux()) return ReadLinuxPhysicalFileIdentity(path);
        if (OperatingSystem.IsWindows()) return ReadWindowsPhysicalFileIdentity(path);
        throw new PlatformNotSupportedException(FileIdentityUnavailable);
    }

    private static PhysicalFileIdentity ReadPhysicalFileIdentity(SafeFileHandle handle)
    {
        if (OperatingSystem.IsLinux()) return ReadLinuxPhysicalFileIdentity(handle);
        if (OperatingSystem.IsWindows()) return ReadWindowsPhysicalFileIdentity(handle);
        throw new PlatformNotSupportedException(FileIdentityUnavailable);
    }

    [SupportedOSPlatform("linux")]
    private static PhysicalFileIdentity ReadLinuxPhysicalFileIdentity(string path) =>
        ReadLinuxPhysicalFileIdentity(AtCurrentWorkingDirectory, path, AtSymlinkNoFollow);

    [SupportedOSPlatform("linux")]
    private static PhysicalFileIdentity ReadLinuxPhysicalFileIdentity(SafeFileHandle handle)
    {
        var addedReference = false;
        try
        {
            handle.DangerousAddRef(ref addedReference);
            return ReadLinuxPhysicalFileIdentity(handle.DangerousGetHandle().ToInt32(), string.Empty,
                AtEmptyPath | AtSymlinkNoFollow);
        }
        finally
        {
            if (addedReference) handle.DangerousRelease();
        }
    }

    [SupportedOSPlatform("linux")]
    private static PhysicalFileIdentity ReadLinuxPhysicalFileIdentity(int directory, string path, int flags)
    {
        LinuxStatxBuffer result;
        try
        {
            if (LinuxStatx(directory, path, flags, StatxBasicStats, out result) != 0)
            {
                var error = Marshal.GetLastPInvokeError();
                if (error is 22 or 38 or 95)
                    throw new PlatformNotSupportedException(FileIdentityUnavailable, new Win32Exception(error));
                throw new IOException("Could not read Linux file identity.", new Win32Exception(error));
            }
        }
        catch (EntryPointNotFoundException exception)
        {
            throw new PlatformNotSupportedException(FileIdentityUnavailable, exception);
        }
        if ((result.Mask & (StatxInode | StatxLinkCount)) != (StatxInode | StatxLinkCount))
            throw new PlatformNotSupportedException(FileIdentityUnavailable);
        return new PhysicalFileIdentity(((ulong)result.DeviceMajor << 32) | result.DeviceMinor,
            result.Inode, result.LinkCount);
    }

    [SupportedOSPlatform("windows")]
    private static PhysicalFileIdentity ReadWindowsPhysicalFileIdentity(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.None);
        return ReadWindowsPhysicalFileIdentity(stream.SafeFileHandle);
    }

    [SupportedOSPlatform("windows")]
    private static PhysicalFileIdentity ReadWindowsPhysicalFileIdentity(SafeFileHandle handle)
    {
        if (!GetFileInformationByHandle(handle, out var result))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error is 1 or 50 or 120)
                throw new PlatformNotSupportedException(FileIdentityUnavailable, new Win32Exception(error));
            throw new IOException("Could not read Windows file identity.", new Win32Exception(error));
        }
        return new PhysicalFileIdentity(result.VolumeSerialNumber,
            ((ulong)result.FileIndexHigh << 32) | result.FileIndexLow, result.NumberOfLinks);
    }

    private const int AtCurrentWorkingDirectory = -100;
    private const int AtSymlinkNoFollow = 0x100;
    private const int AtEmptyPath = 0x1000;
    private const uint StatxBasicStats = 0x7ff;
    private const uint StatxLinkCount = 0x4;
    private const uint StatxInode = 0x100;

    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStatxBuffer
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint LinkCount;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowsFileInformation
    {
        public uint FileAttributes;
        public uint CreationTimeLow;
        public uint CreationTimeHigh;
        public uint LastAccessTimeLow;
        public uint LastAccessTimeHigh;
        public uint LastWriteTimeLow;
        public uint LastWriteTimeHigh;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    [DllImport("libc", EntryPoint = "statx", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern int LinuxStatx(int directory, string path, int flags, uint mask,
        out LinuxStatxBuffer result);

    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle,
        out WindowsFileInformation result);

    private static FileIdentity ReadIdentity(string path)
    {
        var info = new FileInfo(path);
        info.Refresh();
        if (!info.Exists) throw new FileNotFoundException("A named store file disappeared.", path);
        return new FileIdentity(info.Length, info.CreationTimeUtc.Ticks, info.LastWriteTimeUtc.Ticks,
            info.Attributes);
    }

    private static FileIdentity ReadDirectoryIdentity(string path)
    {
        var info = new DirectoryInfo(path);
        info.Refresh();
        if (!info.Exists) throw new DirectoryNotFoundException("A generated-artifact directory disappeared.");
        return new FileIdentity(0, info.CreationTimeUtc.Ticks, info.LastWriteTimeUtc.Ticks, info.Attributes);
    }

    private static StoreReferences ReadSnapshot(StoreLocator locator, string snapshotPath,
        ReferenceBudget referenceBudget) =>
        MotifDatabase.ReadExisting(snapshotPath, locator.Project,
            (connection, transaction) => ReadReferences(connection, transaction, referenceBudget));

    private static StoreReferences ReadReferences(SqliteConnection connection, SqliteTransaction transaction,
        ReferenceBudget referenceBudget)
    {
        var references = new StoreReferences(referenceBudget);
        var assessmentInvocations = ReadInvocations(connection, transaction, references);
        var baselineReferences = ReadBaselines(connection, transaction, references);
        var assessments = ReadAssessments(connection, transaction, baselineReferences, references);

        using (var metadata = connection.CreateCommand())
        {
            metadata.Transaction = transaction;
            metadata.CommandText = "SELECT CurrentAssessmentId FROM MotifMetadata WHERE Id = 1;";
            var current = metadata.ExecuteScalar() as string;
            if (!string.IsNullOrWhiteSpace(current))
            {
                if (!assessments.TryGetValue(current, out var invocationId))
                    references.IncompleteReasons.Add("unknown-current-assessment");
                else if (invocationId is not null)
                    references.AddInvocationRetainer(invocationId, "current", current);
            }
        }

        ReadAssessmentPins(connection, transaction, assessments, references);
        ReadReports(connection, transaction, assessments, references);
        ReadDifferences(connection, transaction, assessments, references);
        ReadRetainedMembers(connection, transaction, assessments, references);
        ReadRetainedInvocations(connection, transaction, assessmentInvocations, references);
        ReadParsimonyArtifactRoots(connection, transaction, references);
        ReadOpaqueReferences(connection, transaction, references);
        return references;
    }

    private static HashSet<string> ReadInvocations(SqliteConnection connection, SqliteTransaction transaction,
        StoreReferences references)
    {
        var invocationIds = new HashSet<string>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT InvocationId, length(EvidenceJson), EvidenceJson " +
                              "FROM AssessmentInvocations ORDER BY InvocationId;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!references.CountRecord()) break;
            var invocationId = reader.GetString(0);
            invocationIds.Add(invocationId);
            references.InvocationRows.Add(invocationId);
            if (reader.GetInt64(1) > MaximumEvidenceJsonCharacters)
            {
                references.IncompleteReasons.Add("invocation-evidence-size-limit");
                continue;
            }
            try
            {
                var evidenceJson = reader.GetString(2);
                using var evidenceDocument = JsonDocument.Parse(evidenceJson,
                    new JsonDocumentOptions { MaxDepth = 16 });
                RequireAllowedProperties(evidenceDocument.RootElement,
                    ["InvocationId", "SourcePath", "SourceBytesSha256", "ExecutableBytesSha256", "WordsPath",
                        "WordsSha256", "TsvPath", "TsvSha256", "StandardErrorPath", "StandardErrorSha256",
                        "PerWordTimeoutMs", "PerWordStepLimit", "Threads", "CollectStatistics", "ExecutableVersion",
                        "ExecutablePath", "AnalysesPath", "AnalysesSha256", "StoppedWordRetry", "GrammarWarnings"],
                    ["InvocationId", "SourcePath", "SourceBytesSha256", "ExecutableBytesSha256", "WordsPath",
                        "WordsSha256", "TsvPath", "TsvSha256", "StandardErrorPath", "StandardErrorSha256",
                        "PerWordTimeoutMs", "PerWordStepLimit", "Threads", "CollectStatistics"],
                    "invocation evidence");
                if (evidenceDocument.RootElement.TryGetProperty("StoppedWordRetry", out var retry) &&
                    retry.ValueKind != JsonValueKind.Null)
                    RequireAllowedProperties(retry,
                        ["LimitMs", "Stopped", "Retried", "Recovered", "StillStopped", "RetryInvocationId",
                            "FirstPassTsvSha256", "FirstPassAnalysesSha256", "RetryTsvSha256", "RetryAnalysesSha256",
                            "Words", "NotRetried", "Failure"],
                        ["LimitMs", "Stopped", "Retried", "Recovered", "StillStopped", "RetryInvocationId",
                            "FirstPassTsvSha256", "FirstPassAnalysesSha256", "RetryTsvSha256", "RetryAnalysesSha256",
                            "Words", "NotRetried"],
                        "stopped-word retry evidence");
                var evidence = JsonSerializer.Deserialize<BatchInvocationEvidence>(evidenceJson, JsonOptions);
                if (evidence is null || evidence.InvocationId != invocationId ||
                    !IsCompleteEvidence(evidence))
                {
                    references.IncompleteReasons.Add("malformed-invocation-evidence");
                    continue;
                }

                references.KnownInvocations.Add(invocationId);
                foreach (var path in EvidencePaths(evidence))
                    references.AddPathOwner(path, invocationId);
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or
                                                ArgumentException or InvalidOperationException)
            {
                references.IncompleteReasons.Add("malformed-invocation-evidence");
            }
        }

        return invocationIds;
    }

    private static Dictionary<string, string?> ReadAssessments(SqliteConnection connection,
        SqliteTransaction transaction, IReadOnlyDictionary<BaselineToken, IReadOnlyList<BaselineReference>> baselines,
        StoreReferences references)
    {
        var assessments = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT AssessmentId, InvocationId, CachePath, length(BaselineToken), BaselineToken " +
                              "FROM Assessments ORDER BY AssessmentId;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!references.CountRecord()) break;
            var assessmentId = reader.GetString(0);
            var invocationId = reader.IsDBNull(1) ? null : reader.GetString(1);
            assessments.Add(assessmentId, invocationId);
            if (invocationId is not null)
            {
                references.AddInvocationRetainer(invocationId, "assessment-history", assessmentId);
                if (!references.KnownInvocations.Contains(invocationId))
                    references.IncompleteReasons.Add("assessment-invocation-missing");
            }

            if (!reader.IsDBNull(2)) references.AddPathRetainer(reader.GetString(2), "assessment-history", assessmentId);

            if (reader.IsDBNull(3) || reader.GetInt64(3) > MaximumEvidenceJsonCharacters || reader.IsDBNull(4))
            {
                references.IncompleteReasons.Add("malformed-assessment-baseline-token");
                continue;
            }

            try
            {
                var tokenJson = reader.GetString(4);
                using var tokenDocument = JsonDocument.Parse(tokenJson, new JsonDocumentOptions { MaxDepth = 8 });
                RequireAllowedProperties(tokenDocument.RootElement,
                    ["ProjectIdentity", "SemanticSnapshotDigest", "ProjectionVersion", "CapturedUtc", "BundleDigest",
                        "CapturedHostSessionId", "CapturedEditGeneration"],
                    ["ProjectIdentity", "SemanticSnapshotDigest", "ProjectionVersion", "CapturedUtc", "BundleDigest"],
                    "Assessment BaselineToken");
                var token = JsonSerializer.Deserialize<BaselineToken>(tokenJson, BaselineTokenJsonOptions);
                if (token is null) throw new JsonException("The Assessment BaselineToken is missing.");
                if (!baselines.TryGetValue(token, out var matches))
                {
                    references.IncompleteReasons.Add("unresolved-assessment-baseline-token");
                    continue;
                }
                foreach (var baseline in matches)
                {
                    references.AddBaselinePath(baseline.RootDirectory, "assessment-history", assessmentId);
                    references.AddBaselinePath(baseline.FwDataPath, "assessment-history", assessmentId);
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException or
                                                ArgumentException or InvalidOperationException or FormatException or
                                                InvalidCastException)
            {
                references.IncompleteReasons.Add("malformed-assessment-baseline-token");
            }
        }

        return assessments;
    }

    private static void ReadAssessmentPins(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyDictionary<string, string?> assessments, StoreReferences references)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT AssessmentId, PinnedBy FROM AssessmentPins ORDER BY AssessmentId, PinnedBy;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!references.CountRecord()) break;
            var assessmentId = reader.GetString(0);
            if (!assessments.TryGetValue(assessmentId, out var invocationId))
            {
                references.IncompleteReasons.Add("unknown-assessment-pin");
                continue;
            }
            if (invocationId is not null)
                references.AddInvocationRetainer(invocationId, "assessment-pin", assessmentId);
        }
    }

    private static void ReadReports(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyDictionary<string, string?> assessments, StoreReferences references)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT ReportId, Kind, AssessmentId, length(ReportJson), ReportJson, " +
                              "length(EvidenceJson), EvidenceJson " +
                              "FROM Reports ORDER BY ReportId;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!references.CountRecord()) break;
            var reportId = reader.GetString(0);
            var kind = reader.IsDBNull(1) ? null : reader.GetString(1);
            var assessmentId = reader.IsDBNull(2) ? null : reader.GetString(2);
            var reportJson = reader.IsDBNull(4) ? null : reader.GetString(4);
            var evidenceJson = reader.IsDBNull(6) ? null : reader.GetString(6);
            if (assessmentId is not null)
                AddReportCitation(reportId, assessmentId, "unknown-report-assessment", assessments, references);

            switch (kind)
            {
                case ParsimonyReportProducer.KindName:
                    ReadParsimonyReportCitations(reportId,
                        reader.IsDBNull(3) ? -1 : reader.GetInt64(3), reportJson,
                        reader.IsDBNull(5) ? -1 : reader.GetInt64(5), evidenceJson, assessments, references);
                    break;
                case CoverageReportProducer.KindName:
                case CorrectnessReportProducer.KindName:
                case DifferenceReportProducer.KindName:
                    if (assessmentId is null)
                        references.IncompleteReasons.Add("report-assessment-missing");
                    break;
                case null:
                    if (assessmentId is null && (reportJson is not null || evidenceJson is not null))
                        references.IncompleteReasons.Add("unknown-legacy-report-citation");
                    break;
                default:
                    references.IncompleteReasons.Add("unknown-report-kind");
                    break;
            }
        }
    }

    private static void ReadParsimonyReportCitations(string reportId, long reportJsonLength, string? reportJson,
        long evidenceJsonLength, string? evidenceJson, IReadOnlyDictionary<string, string?> assessments,
        StoreReferences references)
    {
        if (reportJson is null || reportJsonLength > MaximumEvidenceJsonCharacters)
        {
            references.IncompleteReasons.Add(reportJson is null
                ? "frozen-report-reference-unresolved"
                : "frozen-report-json-size-limit");
        }
        else
        {
            try
            {
                foreach (var assessmentId in ReadParsimonyReportJsonAssessmentIds(reportId, reportJson))
                    AddReportCitation(reportId, assessmentId, "unknown-report-assessment", assessments, references);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException or
                                                InvalidOperationException)
            {
                references.IncompleteReasons.Add("frozen-report-reference-unresolved");
            }
        }

        if (evidenceJson is null || evidenceJsonLength < 0)
        {
            references.IncompleteReasons.Add("frozen-report-reference-unresolved");
            return;
        }
        if (evidenceJsonLength > MaximumEvidenceJsonCharacters)
        {
            references.IncompleteReasons.Add("frozen-report-evidence-size-limit");
            return;
        }
        try
        {
            foreach (var assessmentId in ReadFrozenParsimonyEvidenceAssessmentIds(evidenceJson))
                AddReportCitation(reportId, assessmentId, "unknown-frozen-report-assessment", assessments, references);
            references.IncompleteReasons.Add("frozen-parser-run-references-unresolved");
            references.IncompleteReasons.Add("frozen-report-baseline-unresolved");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or
                                            InvalidOperationException)
        {
            references.IncompleteReasons.Add("frozen-report-reference-unresolved");
        }
    }

    private static IReadOnlyCollection<string> ReadParsimonyReportJsonAssessmentIds(string reportId,
        string reportJson)
    {
        using var document = JsonDocument.Parse(reportJson);
        RequireKnownObjectProperties(document.RootElement, "Parsimony Report",
        [
            "reportId", "inputs", "findings", "text", "assessmentIds", "measureRuns", "joinQuality",
            "dispositionProjection", "notes",
        ]);
        if (!document.RootElement.TryGetProperty("inputs", out _))
            throw new JsonException("A Parsimony Report must include its frozen inputs.");
        var report = JsonSerializer.Deserialize<ParsimonyReportResponse>(reportJson, ParsimonyJsonOptions) ??
            throw new JsonException("The Parsimony Report is empty.");
        if (report.Inputs is null) throw new JsonException("The Parsimony Report inputs are missing.");
        if (!string.Equals(report.ReportId, reportId, StringComparison.Ordinal))
            throw new JsonException("The Parsimony Report id does not match its stored row.");

        var ids = CitationIds(report.AssessmentIds, "Parsimony Report Assessment ids");
        ids.UnionWith(CitationIds(report.Inputs.AssessmentIds, "Parsimony input Assessment ids"));
        return ids;
    }

    private static IReadOnlyCollection<string> ReadFrozenParsimonyEvidenceAssessmentIds(string evidenceJson)
    {
        using var document = JsonDocument.Parse(evidenceJson);
        RequireKnownObjectProperties(document.RootElement, "frozen Parsimony evidence",
            ["inputs", "grammarFactsPath", "evidencePath", "findings", "joinQuality"]);
        var evidence = JsonSerializer.Deserialize<FrozenParsimonyEvidence>(evidenceJson, ParsimonyJsonOptions) ??
            throw new JsonException("Frozen Parsimony evidence is empty.");
        if (evidence.Inputs is null || string.IsNullOrWhiteSpace(evidence.GrammarFactsPath) ||
            string.IsNullOrWhiteSpace(evidence.EvidencePath) || evidence.Findings is null ||
            evidence.Findings.Any(finding => finding is null || string.IsNullOrWhiteSpace(finding.FindingId) ||
                string.IsNullOrWhiteSpace(finding.EvidenceDigest)))
            throw new JsonException("Frozen Parsimony evidence is incomplete.");
        return CitationIds(evidence.Inputs.AssessmentIds, "frozen Parsimony input Assessment ids");
    }

    private static HashSet<string> CitationIds(IReadOnlyList<string>? candidates, string source)
    {
        if (candidates is null || candidates.Any(string.IsNullOrWhiteSpace))
            throw new JsonException($"{source} are missing or malformed.");
        return candidates.ToHashSet(StringComparer.Ordinal);
    }

    private static void RequireKnownObjectProperties(JsonElement element, string source,
        IReadOnlyCollection<string> expectedProperties)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new JsonException($"{source} must be a JSON object.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)) throw new JsonException($"{source} repeats '{property.Name}'.");
            if (!expectedProperties.Contains(property.Name))
                throw new JsonException($"{source} has unknown property '{property.Name}'.");
        }
    }

    private static void AddReportCitation(string reportId, string assessmentId, string unknownReason,
        IReadOnlyDictionary<string, string?> assessments, StoreReferences references)
    {
        if (!assessments.TryGetValue(assessmentId, out var invocationId))
            references.IncompleteReasons.Add(unknownReason);
        else if (invocationId is not null)
            references.AddInvocationRetainer(invocationId, "report-citation", reportId);
    }

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record FrozenParsimonyEvidence(
        [property: JsonRequired] ParsimonyReportInputs Inputs,
        [property: JsonRequired] string GrammarFactsPath,
        [property: JsonRequired] string EvidencePath,
        [property: JsonRequired] IReadOnlyList<FrozenParsimonyFinding> Findings,
        [property: JsonRequired] ParsimonyJoinQuality? JoinQuality);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record FrozenParsimonyFinding(
        [property: JsonRequired] string FindingId,
        [property: JsonRequired] string EvidenceDigest);

    private static void ReadDifferences(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyDictionary<string, string?> assessments, StoreReferences references)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT DifferenceId, BeforeAssessmentId, AfterAssessmentId " +
                              "FROM ProposalDifferences ORDER BY DifferenceId;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!references.CountRecord()) break;
            var differenceId = reader.GetString(0);
            foreach (var ordinal in new[] { 1, 2 })
            {
                var assessmentId = reader.GetString(ordinal);
                if (!assessments.TryGetValue(assessmentId, out var invocationId))
                {
                    references.IncompleteReasons.Add("unknown-difference-assessment");
                    continue;
                }
                if (invocationId is not null)
                    references.AddInvocationRetainer(invocationId, "difference-citation", differenceId);
            }
        }
    }

    private static void ReadRetainedInvocations(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlySet<string> invocations, StoreReferences references)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT InvocationId, ArtifactInvocationId, BaselineRootDirectory, BaselineFwDataPath " +
                              "FROM RetainedInvocations ORDER BY InvocationId;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!references.CountRecord()) break;
            var recordId = reader.GetString(0);
            var artifactInvocationId = reader.GetString(1);
            if (!invocations.Contains(artifactInvocationId))
                references.IncompleteReasons.Add("retained-invocation-artifacts-missing");
            references.AddInvocationRetainer(artifactInvocationId, "retained-invocation", recordId);
            references.AddBaselinePath(reader.GetString(2), "retained-invocation", recordId);
            references.AddBaselinePath(reader.GetString(3), "retained-invocation", recordId);
        }
    }

    private static void ReadRetainedMembers(SqliteConnection connection, SqliteTransaction transaction,
        IReadOnlyDictionary<string, string?> assessments, StoreReferences references)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT InvocationId, AssessmentId FROM RetainedInvocationMembers " +
                              "ORDER BY InvocationId, Kind;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!references.CountRecord()) break;
            var recordId = reader.GetString(0);
            var assessmentId = reader.GetString(1);
            if (!assessments.TryGetValue(assessmentId, out var invocationId))
            {
                references.IncompleteReasons.Add("unknown-retained-assessment");
                continue;
            }
            if (invocationId is not null)
                references.AddInvocationRetainer(invocationId, "retained-invocation", recordId);
        }
    }

    private static Dictionary<BaselineToken, IReadOnlyList<BaselineReference>> ReadBaselines(
        SqliteConnection connection, SqliteTransaction transaction, StoreReferences references)
    {
        var baselines = new Dictionary<BaselineToken, List<BaselineReference>>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT ProjectKey, ProjectIdentity, SemanticSnapshotDigest, ProjectionVersion, " +
                              "CapturedUtc, BundleDigest, CapturedHostSessionId, CapturedEditGeneration, " +
                              "RootDirectory, FwDataPath FROM Baselines ORDER BY ProjectKey;";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!references.CountRecord()) break;
            var recordId = reader.GetString(0);
            var rootDirectory = reader.GetString(8);
            var fwDataPath = reader.GetString(9);
            references.AddBaselinePath(rootDirectory, "baseline-record", recordId);
            references.AddBaselinePath(fwDataPath, "baseline-record", recordId);
            try
            {
                var token = new BaselineToken(reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetString(4), reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetInt64(7));
                if (!IsAbsolutePath(rootDirectory) || !IsAbsolutePath(fwDataPath)) continue;
                if (!baselines.TryGetValue(token, out var matches)) baselines[token] = matches = [];
                matches.Add(new BaselineReference(rootDirectory, fwDataPath));
            }
            catch (Exception exception) when (exception is ArgumentException or FormatException or
                                                InvalidOperationException or InvalidCastException)
            {
                references.IncompleteReasons.Add("malformed-baseline-record");
            }
        }

        return baselines.ToDictionary(pair => pair.Key,
            pair => (IReadOnlyList<BaselineReference>)pair.Value.ToArray());
    }

    private static void ReadParsimonyArtifactRoots(SqliteConnection connection, SqliteTransaction transaction,
        StoreReferences references)
    {
        using (var bundles = connection.CreateCommand())
        {
            bundles.Transaction = transaction;
            bundles.CommandText = "SELECT BundleId, BaselineRootDirectory FROM ParsimonyBundles ORDER BY BundleId;";
            using var reader = bundles.ExecuteReader();
            while (reader.Read())
            {
                if (!references.CountRecord()) break;
                references.AddBaselinePath(reader.GetString(1), "parsimony-bundle", reader.GetString(0));
            }
        }

        using var claims = connection.CreateCommand();
        claims.Transaction = transaction;
        claims.CommandText = "SELECT BundleId, BaselineRootDirectory FROM ParsimonyBuildClaims ORDER BY BundleId;";
        using var claimReader = claims.ExecuteReader();
        while (claimReader.Read())
        {
            if (!references.CountRecord()) break;
            references.AddBaselinePath(claimReader.GetString(1), "parsimony-build-claim", claimReader.GetString(0));
        }
    }

    private static void ReadOpaqueReferences(SqliteConnection connection, SqliteTransaction transaction,
        StoreReferences references)
    {
        ReadIds(connection, transaction, "SELECT JobId FROM Jobs ORDER BY JobId;", "job-evidence-unresolved", references);
        ReadIds(connection, transaction, "SELECT BundleId FROM CurrentParsimonyBundles ORDER BY BundleId;",
            "current-parsimony-reference-unresolved", references);
        ReadIds(connection, transaction, "SELECT BundleId FROM ParsimonyBundleReferences ORDER BY BundleId;",
            "parsimony-reference-unresolved", references);
        ReadIds(connection, transaction, "SELECT BundleId FROM ParsimonyReaderLeases ORDER BY BundleId;",
            "parsimony-reader-unresolved", references);
        ReadIds(connection, transaction, "SELECT ReceiptId FROM Receipts ORDER BY ReceiptId;",
            "receipt-reference-unresolved", references);
        ReadIds(connection, transaction, "SELECT ProposalId FROM AppliedIndex ORDER BY ProposalId;",
            "applied-record-reference-unresolved", references);
        ReadIds(connection, transaction, "SELECT BaselineToken FROM GrammarChecks ORDER BY BaselineToken;",
            "grammar-baseline-reference-unresolved", references);
        ReadIds(connection, transaction, "SELECT ProposalId FROM Proposals ORDER BY ProposalId;",
            "proposal-baseline-reference-unresolved", references);
    }

    private static void ReadIds(SqliteConnection connection, SqliteTransaction transaction, string sql,
        string reason, StoreReferences references, bool addDiagnostic = true)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (!references.CountRecord()) break;
            if (reader.IsDBNull(0))
            {
                references.IncompleteReasons.Add("malformed-reference-row");
                continue;
            }
            if (addDiagnostic) references.IncompleteReasons.Add(reason);
        }
    }

    private static ArtifactInventoryEntry[] ScanArtifacts(string root, ArtifactReferences references,
        bool domainComplete, List<ArtifactInventoryDiagnostic> diagnostics, int scanEntryBudget)
    {
        var rows = new Dictionary<string, ArtifactInventoryEntry>(PathComparer);
        var entriesSeen = 0;
        foreach (var family in Families)
        {
            var familyRoot = Path.Combine(root, family);
            if (!EntryExists(familyRoot)) continue;
            if (IsLink(familyRoot))
            {
                AddRow(familyRoot, family, null, null, "Unknown", "link-refused", [], [], domainComplete, rows);
                diagnostics.Add(new("link-refused", familyRoot));
                continue;
            }
            if (!Directory.Exists(familyRoot))
            {
                var bytes = ReadIdentity(familyRoot).Length;
                AddRow(familyRoot, family, bytes, null, "Unknown", "path-not-directory", [], [], domainComplete, rows);
                diagnostics.Add(new("path-not-directory", familyRoot));
                continue;
            }
            Walk(root, familyRoot, familyRoot, family, 0, ref entriesSeen, references, domainComplete,
                diagnostics, rows, scanEntryBudget);
        }

        return rows.Values.OrderBy(item => item.Path, PathComparer).ToArray();
    }

    private static void Walk(string workerRoot, string familyRoot, string directory, string family, int depth,
        ref int entriesSeen,
        ArtifactReferences references, bool domainComplete, List<ArtifactInventoryDiagnostic> diagnostics,
        Dictionary<string, ArtifactInventoryEntry> rows, int scanEntryBudget)
    {
        if (depth >= MaximumDepth)
        {
            diagnostics.Add(new("scan-depth-limit", directory));
            AddRow(directory, family, null, null, "Unknown", "scan-depth-limit", [], [], domainComplete, rows);
            return;
        }

        var remaining = scanEntryBudget - entriesSeen;
        if (remaining <= 0)
        {
            diagnostics.Add(new("scan-entry-limit", directory));
            AddRow(directory, family, null, null, "Unknown", "scan-entry-limit", [], [], domainComplete, rows);
            return;
        }

        FileIdentity directoryBefore;
        try
        {
            directoryBefore = ReadDirectoryIdentity(directory);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("path-unreadable", directory));
            AddRow(directory, family, null, null, "Unknown", "path-unreadable", [], [], domainComplete, rows);
            return;
        }
        string[] children;
        try
        {
            children = Directory.EnumerateFileSystemEntries(directory)
                .Take(remaining + 1).Order(PathComparer).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("path-unreadable", directory));
            AddRow(directory, family, null, null, "Unknown", "path-unreadable", [], [], domainComplete, rows);
            return;
        }

        if (children.Length == 0)
        {
            AddPathRow(workerRoot, directory, family, 0, references, domainComplete, rows);
            CheckDirectoryStable(directory, directoryBefore, family, domainComplete, diagnostics, rows);
            return;
        }

        var omittedChildren = children.Length > remaining;
        if (omittedChildren) children = children.Take(remaining).ToArray();
        foreach (var child in children)
        {
            if (entriesSeen >= scanEntryBudget)
            {
                diagnostics.Add(new("scan-entry-limit", child));
                AddRow(child, family, null, null, "Unknown", "scan-entry-limit", [], [], domainComplete, rows);
                return;
            }
            entriesSeen++;
            bool childIsLink;
            try
            {
                childIsLink = IsLink(child);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                AddRow(child, family, null, null, "Unknown", "path-unreadable", [], [], domainComplete, rows);
                diagnostics.Add(new("path-unreadable", child));
                continue;
            }
            if (childIsLink)
            {
                AddRow(child, family, null, null, "Unknown", "link-refused", [], [], domainComplete, rows);
                diagnostics.Add(new("link-refused", child));
                continue;
            }
            if (Directory.Exists(child))
            {
                Walk(workerRoot, familyRoot, child, family, depth + 1, ref entriesSeen, references, domainComplete,
                    diagnostics, rows, scanEntryBudget);
                continue;
            }

            try
            {
                var before = ReadIdentity(child);
                if ((before.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    AddRow(child, family, null, null, "Unknown", "link-refused", [], [], domainComplete, rows);
                    diagnostics.Add(new("link-refused", child));
                    continue;
                }
                var physicalBefore = ReadPhysicalFileIdentity(child);
                RequireSingleLink(physicalBefore);
                using var file = new FileStream(child, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete, bufferSize: 1, FileOptions.None);
                var physicalOpened = ReadPhysicalFileIdentity(file.SafeFileHandle);
                RequireSingleLink(physicalOpened);
                var after = ReadIdentity(child);
                var physicalAfterHandle = ReadPhysicalFileIdentity(file.SafeFileHandle);
                var physicalAfterPath = ReadPhysicalFileIdentity(child);
                RequireSingleLink(physicalAfterHandle);
                RequireSingleLink(physicalAfterPath);
                if (before != after || physicalBefore != physicalOpened ||
                    physicalBefore != physicalAfterHandle || physicalBefore != physicalAfterPath)
                {
                    AddRow(child, family, before.Length, null, "Busy", "changed-during-inventory", [], [], domainComplete, rows);
                    diagnostics.Add(new("changed-during-inventory", child));
                    continue;
                }
                AddPathRow(workerRoot, child, family, before.Length, references, domainComplete, rows);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                var code = exception switch
                {
                    IOException when exception.Message == PhysicalAliasRefused => PhysicalAliasRefused,
                    NotSupportedException => FileIdentityUnavailable,
                    _ => "path-unreadable",
                };
                AddRow(child, family, null, null, "Unknown", code, [], [], domainComplete, rows);
                diagnostics.Add(new(code, child));
            }
        }
        if (omittedChildren)
        {
            diagnostics.Add(new("scan-entry-limit", directory));
            AddRow(directory, family, null, null, "Unknown", "scan-entry-limit", [], [], domainComplete, rows);
        }

        CheckDirectoryStable(directory, directoryBefore, family, domainComplete, diagnostics, rows);
    }

    private static void CheckDirectoryStable(string directory, FileIdentity before, string family,
        bool domainComplete, List<ArtifactInventoryDiagnostic> diagnostics,
        Dictionary<string, ArtifactInventoryEntry> rows)
    {
        try
        {
            if (before == ReadDirectoryIdentity(directory)) return;
            diagnostics.Add(new("changed-during-inventory", directory));
            AddRow(directory, family, null, null, "Busy", "changed-during-inventory", [], [], domainComplete, rows);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new("path-unreadable", directory));
            AddRow(directory, family, null, null, "Unknown", "path-unreadable", [], [], domainComplete, rows);
        }
    }

    private static void AddPathRow(string root, string path, string family, long bytes, ArtifactReferences references,
        bool domainComplete, Dictionary<string, ArtifactInventoryEntry> rows)
    {
        var normalized = Path.GetFullPath(path);
        var ownerIds = references.OwnersForPath(root, normalized);
        var retainers = references.RetainersForPath(root, normalized, family, ownerIds);
        var reason = references.ReasonForPath(root, normalized, family, ownerIds, domainComplete);
        var disposition = retainers.Count > 0 ? "Retained" : "Unknown";
        AddRow(normalized, family, bytes, null, disposition, reason, ownerIds, retainers,
            domainComplete, rows);
    }

    private static void AddRow(string path, string family, long? bytes, long? allocatedBytes,
        string disposition, string reason, IReadOnlyList<string> ownerIds,
        IReadOnlyList<string> retainingIds, bool domainComplete,
        Dictionary<string, ArtifactInventoryEntry> rows)
    {
        var normalized = Path.GetFullPath(path);
        if (rows.TryGetValue(normalized, out var existing))
        {
            var aliasCollision = !string.Equals(existing.Path, normalized, StringComparison.Ordinal);
            rows[normalized] = existing with
            {
                LogicalBytes = aliasCollision ? null : existing.LogicalBytes == bytes ? bytes : null,
                OwnerInvocationIds = existing.OwnerInvocationIds.Concat(ownerIds).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).ToArray(),
                RetainingRecordIds = existing.RetainingRecordIds.Concat(retainingIds).Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal).ToArray(),
                ReferenceDomainComplete = existing.ReferenceDomainComplete && domainComplete,
                Disposition = aliasCollision ? "Unknown" :
                    existing.Disposition == "Retained" || disposition == "Retained" ? "Retained" : existing.Disposition,
                ReasonCode = aliasCollision ? "path-alias" :
                    existing.Disposition == "Retained" ? existing.ReasonCode : reason,
            };
            return;
        }
        rows.Add(normalized, new ArtifactInventoryEntry(normalized, family, bytes, allocatedBytes,
            ownerIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), disposition,
            reason, retainingIds.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
            domainComplete));
    }

    private static bool IsCompleteEvidence(BatchInvocationEvidence evidence) =>
        IsAbsolutePath(evidence.SourcePath) && IsAbsolutePath(evidence.WordsPath) &&
        IsAbsolutePath(evidence.TsvPath) && IsAbsolutePath(evidence.StandardErrorPath) &&
        IsDigest(evidence.SourceBytesSha256) && IsDigest(evidence.WordsSha256) &&
        IsDigest(evidence.TsvSha256) && IsDigest(evidence.StandardErrorSha256) &&
        (evidence.AnalysesPath is null && evidence.AnalysesSha256 is null ||
         IsAbsolutePath(evidence.AnalysesPath) && IsDigest(evidence.AnalysesSha256));

    private static IEnumerable<string> EvidencePaths(BatchInvocationEvidence evidence)
    {
        yield return Path.GetFullPath(evidence.SourcePath);
        yield return Path.GetFullPath(evidence.WordsPath);
        yield return Path.GetFullPath(evidence.TsvPath);
        yield return Path.GetFullPath(evidence.StandardErrorPath);
        if (evidence.AnalysesPath is not null) yield return Path.GetFullPath(evidence.AnalysesPath);
    }

    private static bool IsDigest(string? value) => value is { Length: 71 } &&
        value.StartsWith("sha256:", StringComparison.Ordinal) &&
        value.AsSpan(7).IndexOfAnyExcept("0123456789abcdef") < 0;

    private static bool IsAbsolutePath(string? value) => !string.IsNullOrWhiteSpace(value) &&
        Path.IsPathFullyQualified(value) && string.Equals(Path.GetFullPath(value), value, PathComparer == StringComparer.Ordinal
            ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    private static ParsedManifest ReadManifest(string path)
    {
        var before = ReadIdentity(path);
        if (before.Length <= 0 || before.Length > MaximumManifestBytes)
            throw new InvalidDataException("The stores manifest must be between one byte and one megabyte.");
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                   bufferSize: 81920, FileOptions.SequentialScan))
        using (var buffer = new MemoryStream())
        {
            var chunk = new byte[81920];
            int count;
            while ((count = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + count > MaximumManifestBytes)
                    throw new InvalidDataException("The stores manifest exceeds one megabyte.");
                buffer.Write(chunk, 0, count);
            }
            bytes = buffer.ToArray();
        }
        if (before != ReadIdentity(path))
            throw new IOException("The stores manifest changed while it was read.");
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 8 });
        var root = document.RootElement;
        RequireObject(root, "manifest");
        RequireProperties(root, ["schemaVersion", "referenceDomainComplete", "stores"], "manifest");
        var schemaVersion = root.GetProperty("schemaVersion");
        if (schemaVersion.ValueKind != JsonValueKind.Number || !schemaVersion.TryGetInt32(out var version))
            throw new InvalidDataException("The stores manifest version must be an integer.");
        if (version != 1)
            throw new NotSupportedException("The stores manifest version is not supported.");
        var completeElement = root.GetProperty("referenceDomainComplete");
        if (completeElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException("The referenceDomainComplete field must be boolean.");
        var declaredComplete = completeElement.GetBoolean();
        var storesElement = root.GetProperty("stores");
        if (storesElement.ValueKind != JsonValueKind.Array || storesElement.GetArrayLength() is < 1 or > MaximumStores)
            throw new InvalidDataException("The manifest must name between one and one hundred exact project stores.");
        var stores = new List<StoreLocator>();
        var paths = new HashSet<string>(PathComparer);
        foreach (var element in storesElement.EnumerateArray())
        {
            RequireObject(element, "store locator");
            RequireProperties(element, ["databasePath", "projectPath", "projectIdentity"], "store locator");
            var databaseElement = element.GetProperty("databasePath");
            var projectElement = element.GetProperty("projectPath");
            var identityElement = element.GetProperty("projectIdentity");
            if (databaseElement.ValueKind != JsonValueKind.String ||
                projectElement.ValueKind != JsonValueKind.String || identityElement.ValueKind != JsonValueKind.String)
                throw new InvalidDataException("Store locators must use string values.");
            var databasePath = databaseElement.GetString();
            var projectPath = projectElement.GetString();
            var projectIdentity = identityElement.GetString();
            if (!IsAbsolutePath(databasePath) || !IsAbsolutePath(projectPath) ||
                string.IsNullOrWhiteSpace(projectIdentity))
                throw new InvalidDataException("Each store locator needs canonical absolute paths and a project identity.");
            RejectExistingPathLinks(databasePath!);
            RejectExistingPathLinks(projectPath!);
            if (!paths.Add(databasePath!))
                throw new InvalidDataException("The manifest repeats a project store path.");
            stores.Add(new StoreLocator(databasePath!, new ProjectLocator(projectPath!, projectIdentity!)));
        }

        return new ParsedManifest(declaredComplete, stores,
            "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }

    internal static string RequireDirectoryPath(string value, string label)
    {
        if (!IsAbsolutePath(value)) throw new InvalidDataException($"The {label} path must be absolute and canonical.");
        RejectExistingPathLinks(value);
        if (!Directory.Exists(value)) throw new DirectoryNotFoundException($"The {label} directory does not exist.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(value));
    }

    private static string RequireFilePath(string value, string label)
    {
        if (!IsAbsolutePath(value)) throw new InvalidDataException($"The {label} path must be absolute and canonical.");
        RejectExistingPathLinks(value);
        if (!File.Exists(value)) throw new FileNotFoundException($"The {label} does not exist.", value);
        return Path.GetFullPath(value);
    }

    private static bool HasWorkerMarker(string root)
    {
        var marker = Path.Combine(root, ".motif-worker-root");
        try
        {
            if (!File.Exists(marker) || IsLink(marker)) return false;
            var before = ReadIdentity(marker);
            if (before.Length > 128) return false;
            var value = File.ReadAllText(marker);
            return before == ReadIdentity(marker) && value.Length <= 128 &&
                   string.Equals(value.Trim(), WorkerMarker, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void RejectExistingPathLinks(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var current = fullPath;
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && IsLink(current))
                throw new InvalidDataException("A path containing a symbolic link or reparse point is refused.");
            current = Path.GetDirectoryName(current)!;
        }
    }

    private static void RejectLink(string path)
    {
        if (IsLink(path)) throw new InvalidDataException("A symbolic link or reparse point is refused.");
    }

    private static bool IsLink(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static bool EntryExists(string path)
    {
        try
        {
            _ = File.GetAttributes(path);
            return true;
        }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private static void RequireObject(JsonElement element, string label)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"The {label} must be a JSON object.");
    }

    private static void RequireProperties(JsonElement element, IReadOnlyCollection<string> expected, string label)
    {
        var expectedSet = expected.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!expectedSet.Contains(property.Name) || !seen.Add(property.Name))
                throw new InvalidDataException($"The {label} has an unknown or repeated field.");
        }
        if (!expectedSet.SetEquals(seen)) throw new InvalidDataException($"The {label} is missing a required field.");
    }

    private static void RequireAllowedProperties(JsonElement element, IReadOnlyCollection<string> allowed,
        IReadOnlyCollection<string> required, string label)
    {
        RequireObject(element, label);
        var allowedSet = allowed.ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
            if (!allowedSet.Contains(property.Name) || !seen.Add(property.Name))
                throw new InvalidDataException($"The {label} has an unknown or repeated field.");
        if (!required.All(seen.Contains))
            throw new InvalidDataException($"The {label} is missing a required field.");
    }

    private static bool IsInventoryStoreFailure(Exception exception) => exception is IOException or
        UnauthorizedAccessException or InvalidDataException or NotSupportedException or SqliteException or
        ArgumentException or InvalidOperationException or InvalidCastException or FormatException or
        System.Security.SecurityException;

    private static string StoreFailureCode(Exception exception) => exception switch
    {
        MotifStoreLockException => "active-store-use",
        NotSupportedException when exception.Message == FileIdentityUnavailable => FileIdentityUnavailable,
        NotSupportedException => "unsupported-store-schema",
        InvalidDataException => exception.Message == "snapshot-size-limit" ? "snapshot-size-limit" : "invalid-store",
        IOException when exception.Message == PhysicalAliasRefused => PhysicalAliasRefused,
        IOException when exception.Message == "snapshot-source-changed" => "changed-during-snapshot",
        IOException => "snapshot-failed",
        UnauthorizedAccessException => "store-unreadable",
        SqliteException => "snapshot-database-invalid",
        _ => "snapshot-failed",
    };

    private static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions ParsimonyJsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };
    private static readonly JsonSerializerOptions BaselineTokenJsonOptions = MotifJson.CreateOptions();

    private sealed record ParsedManifest(bool ReferenceDomainComplete, IReadOnlyList<StoreLocator> Stores,
        string Sha256);
    private sealed record StoreLocator(string DatabasePath, ProjectLocator Project);
    private sealed record BaselineReference(string RootDirectory, string FwDataPath);
    private sealed record StoreSnapshot(string DatabasePath, string Sha256);
    internal sealed record SnapshotFile(string SourcePath, string Name, string Sha256, FileIdentity Identity,
        PhysicalFileIdentity PhysicalIdentity);
    internal sealed record FileIdentity(long Length, long CreationUtcTicks, long LastWriteUtcTicks,
        FileAttributes Attributes);
    internal readonly record struct PhysicalFileIdentity(ulong VolumeId, ulong FileId, uint LinkCount);

    private sealed class ReferenceBudget
    {
        private int _records;

        public bool TryCount()
        {
            if (_records >= MaximumReferenceRecords) return false;
            _records++;
            return true;
        }
    }

    internal sealed class SnapshotBudget
    {
        private long _reservedBytes;

        public void Reserve(long bytes)
        {
            if (bytes < 0 || bytes > MaximumSnapshotFileBytes ||
                _reservedBytes > MaximumSnapshotTotalBytes - bytes)
                throw new InvalidDataException("snapshot-size-limit");
            _reservedBytes += bytes;
        }
    }

    internal sealed class PrivateSnapshotDirectory : IDisposable
    {
        public PrivateSnapshotDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "motif-artifact-snapshot-" +
                Guid.NewGuid().ToString("N"));
            _ = CreatePrivateSnapshotRoot(Path);
        }

        public string Path { get; }
        public string PathFor(int index) => System.IO.Path.Combine(Path, index.ToString("D3",
            System.Globalization.CultureInfo.InvariantCulture));

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed class StoreReferences
    {
        private readonly ReferenceBudget _budget;

        public StoreReferences(ReferenceBudget budget) => _budget = budget;

        public HashSet<string> InvocationRows { get; } = new(StringComparer.Ordinal);
        public HashSet<string> KnownInvocations { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> InvocationRetainers { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> InvocationReasons { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, HashSet<string>> PathOwners { get; } = new(PathComparer);
        public Dictionary<string, HashSet<string>> PathRetainers { get; } = new(PathComparer);
        public Dictionary<string, HashSet<string>> PathReasons { get; } = new(PathComparer);
        public Dictionary<string, HashSet<string>> BaselinePathRetainers { get; } = new(PathComparer);
        public Dictionary<string, HashSet<string>> BaselinePathReasons { get; } = new(PathComparer);
        public HashSet<string> IncompleteReasons { get; } = new(StringComparer.Ordinal);

        public bool CountRecord()
        {
            if (!_budget.TryCount())
            {
                IncompleteReasons.Add("reference-row-limit");
                return false;
            }
            return true;
        }

        public void AddPathOwner(string path, string invocationId) => Add(PathOwners,
            Path.GetFullPath(path), invocationId);

        public void AddPathRetainer(string path, string reason, string recordId)
        {
            if (!IsAbsolutePath(path))
            {
                IncompleteReasons.Add("malformed-artifact-path");
                return;
            }
            var fullPath = Path.GetFullPath(path);
            Add(PathRetainers, fullPath, recordId);
            Add(PathReasons, fullPath, reason);
        }

        public void AddBaselinePath(string path, string reason, string recordId)
        {
            if (!IsAbsolutePath(path))
            {
                IncompleteReasons.Add("malformed-baseline-path");
                return;
            }
            var fullPath = Path.GetFullPath(path);
            Add(BaselinePathRetainers, fullPath, recordId);
            Add(BaselinePathReasons, fullPath, reason);
        }

        public void AddInvocationRetainer(string invocationId, string reason, string recordId)
        {
            Add(InvocationRetainers, invocationId, recordId);
            Add(InvocationReasons, invocationId, reason);
        }

        public void Merge(StoreReferences other)
        {
            InvocationRows.UnionWith(other.InvocationRows);
            KnownInvocations.UnionWith(other.KnownInvocations);
            MergeSets(InvocationRetainers, other.InvocationRetainers);
            MergeSets(InvocationReasons, other.InvocationReasons);
            MergeSets(PathOwners, other.PathOwners);
            MergeSets(PathRetainers, other.PathRetainers);
            MergeSets(PathReasons, other.PathReasons);
            MergeSets(BaselinePathRetainers, other.BaselinePathRetainers);
            MergeSets(BaselinePathReasons, other.BaselinePathReasons);
            IncompleteReasons.UnionWith(other.IncompleteReasons);
        }

        private static void MergeSets(Dictionary<string, HashSet<string>> target,
            Dictionary<string, HashSet<string>> source)
        {
            foreach (var (key, values) in source)
                foreach (var value in values) Add(target, key, value);
        }

        private static void Add(Dictionary<string, HashSet<string>> values, string key, string value)
        {
            if (!values.TryGetValue(key, out var set)) values[key] = set = new HashSet<string>(StringComparer.Ordinal);
            set.Add(value);
        }
    }

    private sealed class ArtifactReferences
    {
        private readonly HashSet<string> _invocationRows = new(StringComparer.Ordinal);
        private readonly HashSet<string> _knownInvocations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _invocationRetainers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _invocationReasons = new(StringComparer.Ordinal);
        private readonly Dictionary<string, HashSet<string>> _pathOwners = new(PathComparer);
        private readonly Dictionary<string, HashSet<string>> _pathRetainers = new(PathComparer);
        private readonly Dictionary<string, HashSet<string>> _pathReasons = new(PathComparer);
        private readonly Dictionary<string, HashSet<string>> _baselinePathRetainers = new(PathComparer);
        private readonly Dictionary<string, HashSet<string>> _baselinePathReasons = new(PathComparer);
        private readonly HashSet<string> _incompleteReasons = new(StringComparer.Ordinal);

        public IReadOnlyCollection<string> IncompleteReasons => _incompleteReasons;

        public void Merge(StoreReferences store)
        {
            _invocationRows.UnionWith(store.InvocationRows);
            _knownInvocations.UnionWith(store.KnownInvocations);
            _incompleteReasons.UnionWith(store.IncompleteReasons);
            MergeSets(_invocationRetainers, store.InvocationRetainers);
            MergeSets(_invocationReasons, store.InvocationReasons);
            MergeSets(_pathOwners, store.PathOwners);
            MergeSets(_pathRetainers, store.PathRetainers);
            MergeSets(_pathReasons, store.PathReasons);
            MergeSets(_baselinePathRetainers, store.BaselinePathRetainers);
            MergeSets(_baselinePathReasons, store.BaselinePathReasons);
        }

        public IReadOnlyList<string> OwnersForPath(string root, string path)
        {
            return _pathOwners.TryGetValue(path, out var owners)
                ? owners.Order(StringComparer.Ordinal).ToArray()
                : [];
        }

        public IReadOnlyList<string> RetainersForPath(string root, string path, string family,
            IReadOnlyList<string> ownerIds)
        {
            var results = new HashSet<string>(StringComparer.Ordinal);
            if (_pathRetainers.TryGetValue(path, out var direct)) results.UnionWith(direct);
            if (family == "baselines")
            {
                foreach (var (baselinePath, retainers) in _baselinePathRetainers)
                    if (IsSameOrChild(path, baselinePath) || IsSameOrChild(baselinePath, path))
                        results.UnionWith(retainers);
            }
            var runInvocation = RunInvocationId(root, path);
            if (runInvocation is not null && _invocationRows.Contains(runInvocation) &&
                _invocationRetainers.TryGetValue(runInvocation, out var runRetainers))
                results.UnionWith(runRetainers);
            foreach (var ownerId in ownerIds)
                if (_invocationRetainers.TryGetValue(ownerId, out var retained))
                    results.UnionWith(retained);
            return results.Order(StringComparer.Ordinal).ToArray();
        }

        public string ReasonForPath(string root, string path, string family, IReadOnlyList<string> ownerIds,
            bool domainComplete)
        {
            if (family == "baselines")
            {
                foreach (var baselinePath in _baselinePathRetainers.Keys)
                    if (IsSameOrChild(path, baselinePath) || IsSameOrChild(baselinePath, path))
                        return _baselinePathReasons.TryGetValue(baselinePath, out var baselineReasons)
                            ? BestReason(baselineReasons) : "baseline-record";
                return domainComplete ? "unknown-ownership" : "reference-domain-incomplete";
            }
            if (_pathReasons.TryGetValue(path, out var pathReasons))
                return BestReason(pathReasons);
            foreach (var ownerId in ownerIds)
                if (_invocationReasons.TryGetValue(ownerId, out var invocationReasons))
                    return BestReason(invocationReasons);
            var runInvocation = RunInvocationId(root, path);
            if (runInvocation is not null && _invocationReasons.TryGetValue(runInvocation, out var runReasons))
                return BestReason(runReasons);
            if (!domainComplete) return "reference-domain-incomplete";
            if (runInvocation is not null && _knownInvocations.Contains(runInvocation) &&
                _pathOwners.TryGetValue(path, out var declaredPaths) && declaredPaths.Contains(runInvocation))
                return "orphan-recorded-invocation";
            return "unknown-ownership";
        }

        private static void MergeSets(Dictionary<string, HashSet<string>> target,
            Dictionary<string, HashSet<string>> source)
        {
            foreach (var (key, values) in source)
            {
                if (!target.TryGetValue(key, out var current))
                    target[key] = current = new HashSet<string>(StringComparer.Ordinal);
                current.UnionWith(values);
            }
        }

        private static string BestReason(IEnumerable<string> reasons)
        {
            var set = reasons.ToHashSet(StringComparer.Ordinal);
            foreach (var reason in new[] { "current", "report-citation", "difference-citation", "assessment-pin",
                         "retained-invocation", "assessment-history", "baseline-record", "parsimony-bundle",
                         "parsimony-build-claim" })
                if (set.Contains(reason)) return reason;
            return set.Order(StringComparer.Ordinal).FirstOrDefault() ?? "unknown-ownership";
        }

        private static string? RunInvocationId(string root, string path)
        {
            var relative = Path.GetRelativePath(root, path);
            var segments = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            return segments.Length > 1 && string.Equals(segments[0], "assessment-runs",
                PathComparer == StringComparer.Ordinal ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
                ? segments[1] : null;
        }

        private static bool IsSameOrChild(string path, string parent)
        {
            var relative = Path.GetRelativePath(parent, path);
            return relative == "." || relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar,
                OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && !Path.IsPathRooted(relative);
        }
    }
}
