using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Host.Baselines;

/// <summary>Owns a private file-backed Baseline copy for one read, including its disposable cache and files.</summary>
public sealed class BaselineReadCache : IDisposable
{
    private const int OwnerRecordVersion = 1;
    private readonly string _root;
    private readonly string _ownerPath;
    private int _disposed;

    private BaselineReadCache(string root, string ownerPath, LcmCache cache)
    {
        _root = root;
        _ownerPath = ownerPath;
        Cache = cache;
    }

    /// <summary>The privately owned cache; callers project values and never save it.</summary>
    public LcmCache Cache { get; }

    /// <summary>The dedicated directory that contains disposable Baseline read copies.</summary>
    public const string CopiesFolder = "SIL.Motif.BaselineRead";

    /// <summary>The minimum time a positively dead owner must remain recorded before its copy is reclaimed.</summary>
    public static readonly TimeSpan AbandonedOwnerGracePeriod = TimeSpan.FromMinutes(10);

    /// <summary>Copies the captured project and writing systems before opening an independent reader.</summary>
    public static BaselineReadCache Open(string fwDataPath) =>
        Open(fwDataPath, BaselineReadCacheEnvironment.ForSystemTemporaryDirectory());

    /// <summary>Opens a copy with the supplied root, clock, owner inspector and record reader.</summary>
    internal static BaselineReadCache Open(string fwDataPath, BaselineReadCacheEnvironment environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fwDataPath);
        ArgumentNullException.ThrowIfNull(environment);

        var copiesRoot = Path.Combine(Path.GetFullPath(environment.TemporaryRoot), CopiesFolder);
        Directory.CreateDirectory(copiesRoot);
        if (GetEntryState(copiesRoot, out var copiesAttributes) != EntryState.Present ||
            (copiesAttributes & FileAttributes.Directory) == 0 ||
            (copiesAttributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("The Baseline read-copy root is not a regular directory.");
        SweepStale(environment);

        var (root, ownerPath) = CreateOwnedDirectory(copiesRoot, environment);
        LcmCache? cache = null;
        try
        {
            cache = new ScratchCacheFactory().CreateFromFileCopy(fwDataPath, root);
            return new BaselineReadCache(root, ownerPath, cache);
        }
        catch
        {
            try { cache?.Dispose(); }
            finally { DeleteOwnedCopy(root, ownerPath); }
            throw;
        }
    }

    /// <summary>Reclaims only recorded copies whose exact owner is dead and past the grace period.</summary>
    /// <param name="environment">The isolated root and owner probes used for this sweep.</param>
    /// <returns>The number of copy directories removed.</returns>
    internal static int SweepStale(BaselineReadCacheEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var copiesRoot = Path.Combine(Path.GetFullPath(environment.TemporaryRoot), CopiesFolder);
        if (!TryGetAttributes(copiesRoot, out var rootAttributes) ||
            (rootAttributes & FileAttributes.Directory) == 0 ||
            (rootAttributes & FileAttributes.ReparsePoint) != 0)
            return 0;

        string[] entries;
        try { entries = Directory.GetFileSystemEntries(copiesRoot); }
        catch (Exception exception) when (IsFilesystemFailure(exception)) { return 0; }

        var deleted = 0;
        foreach (var path in entries)
        {
            var copyId = Path.GetFileName(path);
            if (!IsGuidDirectoryName(copyId) || !TryGetAttributes(path, out var attributes) ||
                (attributes & FileAttributes.Directory) == 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0)
                continue;

            var ownerPath = path + ".owner";
            if (!TryGetAttributes(ownerPath, out var ownerAttributes) ||
                (ownerAttributes & FileAttributes.Directory) != 0 ||
                (ownerAttributes & FileAttributes.ReparsePoint) != 0)
                continue;

            var owner = TryReadOwner(ownerPath, copyId, environment);
            if (owner is null || GetOwnerStatus(owner, environment) != BaselineReadOwnerStatus.Dead ||
                !PastGrace(owner.CreatedUtcTicks, environment))
                continue;

            if (ContainsReparsePoint(path)) continue;

            try
            {
                Directory.Delete(path, recursive: true);
                TryDeleteRegularFile(ownerPath);
                deleted++;
            }
            catch (Exception exception) when (IsFilesystemFailure(exception)) { }
        }

        return deleted;
    }

    /// <summary>Releases LibLCM ownership before deleting this instance's private project files.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { Cache.Dispose(); }
        finally { DeleteOwnedCopy(_root, _ownerPath); }
    }

    private static (string Root, string OwnerPath) CreateOwnedDirectory(
        string copiesRoot, BaselineReadCacheEnvironment environment)
    {
        using var process = Process.GetCurrentProcess();
        var processId = process.Id;
        var processStartUtcTicks = process.StartTime.ToUniversalTime().Ticks;
        if (processStartUtcTicks <= 0) throw new InvalidOperationException("The owner process has no start identity.");

        while (true)
        {
            var copyId = Guid.NewGuid().ToString("N");
            var root = Path.Combine(copiesRoot, copyId);
            var ownerPath = root + ".owner";
            var rootState = GetEntryState(root, out _, environment.ReadEntryAttributes);
            if (rootState == EntryState.Present) continue;
            if (rootState == EntryState.Unknown)
                throw new IOException("A new Baseline copy directory cannot be inspected.");

            var ownerState = GetEntryState(ownerPath, out _, environment.ReadEntryAttributes);
            if (ownerState == EntryState.Present) continue;
            if (ownerState == EntryState.Unknown)
                throw new IOException("A new Baseline owner record cannot be inspected.");

            var createdUtcTicks = environment.UtcNow().UtcDateTime.Ticks;
            var record = new OwnerRecord(OwnerRecordVersion, copyId, processId,
                processStartUtcTicks, createdUtcTicks);
            var pendingPath = ownerPath + "." + Guid.NewGuid().ToString("N") + ".pending";
            var ownerPublished = false;
            try
            {
                WritePendingOwner(pendingPath, record);
                try
                {
                    File.Move(pendingPath, ownerPath);
                }
                catch (IOException) when (GetEntryState(ownerPath, out _, environment.ReadEntryAttributes) == EntryState.Present)
                {
                    TryDeleteRegularFile(pendingPath);
                    continue;
                }
                ownerPublished = true;

                var publishedRootState = GetEntryState(root, out var attributes, environment.ReadEntryAttributes);
                if (publishedRootState == EntryState.Present)
                {
                    TryDeleteRegularFile(ownerPath);
                    continue;
                }
                if (publishedRootState == EntryState.Unknown)
                    throw new IOException("The new Baseline copy directory cannot be inspected.");

                Directory.CreateDirectory(root);
                publishedRootState = GetEntryState(root, out attributes, environment.ReadEntryAttributes);
                if (publishedRootState == EntryState.Missing)
                    throw new IOException("The new Baseline copy directory was not created.");
                if (publishedRootState == EntryState.Unknown)
                    throw new IOException("The new Baseline copy directory cannot be inspected.");
                if ((attributes & FileAttributes.Directory) == 0 ||
                    (attributes & FileAttributes.ReparsePoint) != 0)
                {
                    TryDeleteRegularFile(ownerPath);
                    continue;
                }

                return (root, ownerPath);
            }
            catch
            {
                TryDeleteRegularFile(pendingPath);
                if (ownerPublished) DeleteOwnedCopy(root, ownerPath);
                throw;
            }
            finally
            {
                TryDeleteRegularFile(pendingPath);
            }
        }
    }

    private static void WritePendingOwner(string path, OwnerRecord owner)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 4096, FileOptions.WriteThrough);
        JsonSerializer.Serialize(stream, owner, OwnerJsonOptions);
        stream.Flush(flushToDisk: true);
    }

    private static OwnerRecord? TryReadOwner(
        string ownerPath, string copyId, BaselineReadCacheEnvironment environment)
    {
        try
        {
            using var document = JsonDocument.Parse(environment.ReadOwnerRecord(ownerPath));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !TryGetInt32(root, "version", out var versionValue) ||
                versionValue != OwnerRecordVersion ||
                !root.TryGetProperty("copyId", out var id) || id.ValueKind != JsonValueKind.String ||
                !string.Equals(id.GetString(), copyId, StringComparison.OrdinalIgnoreCase) ||
                !TryGetInt32(root, "processId", out var pid) || pid <= 0 ||
                !TryGetInt64(root, "processStartUtcTicks", out var startTicks) ||
                !IsValidUtcTicks(startTicks) ||
                !TryGetInt64(root, "createdUtcTicks", out var createdTicks) ||
                !IsValidUtcTicks(createdTicks))
                return null;

            return new OwnerRecord(versionValue, copyId, pid, startTicks, createdTicks);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or JsonException)
        {
            return null;
        }
    }

    private static bool TryGetInt32(JsonElement owner, string name, out int value)
    {
        value = default;
        return owner.TryGetProperty(name, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out value);
    }

    private static bool TryGetInt64(JsonElement owner, string name, out long value)
    {
        value = default;
        return owner.TryGetProperty(name, out var element) &&
            element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt64(out value);
    }

    private static BaselineReadOwnerStatus GetOwnerStatus(
        OwnerRecord owner, BaselineReadCacheEnvironment environment)
    {
        try { return environment.InspectOwner(owner.ProcessId, owner.ProcessStartUtcTicks); }
        catch { return BaselineReadOwnerStatus.Unknown; }
    }

    private static bool PastGrace(long createdUtcTicks, BaselineReadCacheEnvironment environment)
    {
        try
        {
            var nowTicks = environment.UtcNow().UtcDateTime.Ticks;
            return nowTicks >= createdUtcTicks && nowTicks - createdUtcTicks >= AbandonedOwnerGracePeriod.Ticks;
        }
        catch { return false; }
    }

    /// <summary>Checks liveness using both a PID and its exact recorded process start time.</summary>
    internal static BaselineReadOwnerStatus InspectSystemProcess(int processId, long expectedStartUtcTicks)
    {
        Process? process = null;
        try
        {
            process = Process.GetProcessById(processId);
            if (process.HasExited) return BaselineReadOwnerStatus.Dead;
            var actualStartUtcTicks = process.StartTime.ToUniversalTime().Ticks;
            if (process.HasExited) return BaselineReadOwnerStatus.Dead;
            return actualStartUtcTicks == expectedStartUtcTicks
                ? BaselineReadOwnerStatus.Alive
                : BaselineReadOwnerStatus.Dead;
        }
        catch (ArgumentException) { return BaselineReadOwnerStatus.Dead; }
        catch (InvalidOperationException) { return BaselineReadOwnerStatus.Unknown; }
        catch (Exception exception) when (exception is Win32Exception or IOException or UnauthorizedAccessException or SecurityException)
        {
            return BaselineReadOwnerStatus.Unknown;
        }
        finally { process?.Dispose(); }
    }

    private static bool ContainsReparsePoint(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory))
        {
            string[] entries;
            try { entries = Directory.GetFileSystemEntries(directory); }
            catch (Exception exception) when (IsFilesystemFailure(exception)) { return true; }

            foreach (var entry in entries)
            {
                if (!TryGetAttributes(entry, out var attributes)) return true;
                if ((attributes & FileAttributes.ReparsePoint) != 0) return true;
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        }

        return false;
    }

    private static bool IsGuidDirectoryName(string name) =>
        Guid.TryParseExact(name, "N", out var id) &&
        string.Equals(name, id.ToString("N"), StringComparison.OrdinalIgnoreCase);

    private static bool IsValidUtcTicks(long ticks) => ticks > 0 && ticks <= DateTime.MaxValue.Ticks;

    private static bool TryGetAttributes(string path, out FileAttributes attributes) =>
        GetEntryState(path, out attributes) == EntryState.Present;

    private static EntryState GetEntryState(
        string path, out FileAttributes attributes, Func<string, FileAttributes>? readAttributes = null)
    {
        try
        {
            attributes = readAttributes is null ? File.GetAttributes(path) : readAttributes(path);
            return EntryState.Present;
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            attributes = default;
            return EntryState.Missing;
        }
        catch (Exception exception) when (IsFilesystemFailure(exception))
        {
            attributes = default;
            return EntryState.Unknown;
        }
    }

    private static void DeleteOwnedCopy(string root, string ownerPath)
    {
        var state = GetEntryState(root, out var attributes);
        if (state == EntryState.Present)
        {
            if ((attributes & FileAttributes.Directory) == 0 ||
                (attributes & FileAttributes.ReparsePoint) != 0 ||
                ContainsReparsePoint(root))
                return;

            try { Directory.Delete(root, recursive: true); }
            catch (Exception exception) when (IsFilesystemFailure(exception)) { return; }
        }
        else if (state == EntryState.Unknown)
        {
            return;
        }

        TryDeleteRegularFile(ownerPath);
    }

    private static void TryDeleteRegularFile(string path)
    {
        if (!TryGetAttributes(path, out var attributes) ||
            (attributes & FileAttributes.Directory) != 0 ||
            (attributes & FileAttributes.ReparsePoint) != 0)
            return;
        try { File.Delete(path); }
        catch (Exception exception) when (IsFilesystemFailure(exception)) { }
    }

    private static bool IsFilesystemFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException;

    private enum EntryState
    {
        Missing,
        Present,
        Unknown
    }

    private sealed record OwnerRecord(
        int Version,
        string CopyId,
        int ProcessId,
        long ProcessStartUtcTicks,
        long CreatedUtcTicks);

    private static readonly JsonSerializerOptions OwnerJsonOptions = new(JsonSerializerDefaults.Web);
}

/// <summary>Distinguishes an exact running owner from proven death and uncertain process inspection.</summary>
internal enum BaselineReadOwnerStatus
{
    /// <summary>The process is running and its start identity matches the owner record.</summary>
    Alive,
    /// <summary>The recorded process has exited or its PID belongs to a different start identity.</summary>
    Dead,
    /// <summary>The process could not be inspected well enough to prove either state.</summary>
    Unknown
}

/// <summary>Isolated filesystem and process probes used by one Baseline read-cache owner.</summary>
internal sealed class BaselineReadCacheEnvironment
{
    /// <summary>Creates a private environment for the product temporary directory or a fixture root.</summary>
    internal BaselineReadCacheEnvironment(
        string temporaryRoot,
        Func<DateTimeOffset> utcNow,
        Func<int, long, BaselineReadOwnerStatus> inspectOwner,
        Func<string, string>? readOwnerRecord = null,
        Func<string, FileAttributes>? readEntryAttributes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryRoot);
        TemporaryRoot = temporaryRoot;
        UtcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        InspectOwner = inspectOwner ?? throw new ArgumentNullException(nameof(inspectOwner));
        ReadOwnerRecord = readOwnerRecord ?? File.ReadAllText;
        ReadEntryAttributes = readEntryAttributes ?? File.GetAttributes;
    }

    /// <summary>The parent directory under which this environment's copies are kept.</summary>
    internal string TemporaryRoot { get; }

    /// <summary>Returns current time for owner age checks and record publication.</summary>
    internal Func<DateTimeOffset> UtcNow { get; }

    /// <summary>Inspects the PID and exact start identity recorded by a copy owner.</summary>
    internal Func<int, long, BaselineReadOwnerStatus> InspectOwner { get; }

    /// <summary>Reads one matching owner record, allowing unreadable records to remain unknown.</summary>
    internal Func<string, string> ReadOwnerRecord { get; }

    /// <summary>Reads filesystem attributes for a candidate directory or owner record.</summary>
    internal Func<string, FileAttributes> ReadEntryAttributes { get; }

    /// <summary>Creates production probes rooted at the system temporary directory.</summary>
    internal static BaselineReadCacheEnvironment ForSystemTemporaryDirectory() =>
        new(Path.GetTempPath(), static () => DateTimeOffset.UtcNow,
            BaselineReadCache.InspectSystemProcess, File.ReadAllText);
}
