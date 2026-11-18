using System.Security.Cryptography;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Host.LcmUtils;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Builds an expensive saved LibLCM master once per input hash and lets every test process copy it, rather than
/// each process rebuilding its own.
/// </summary>
/// <remarks>
/// <para>
/// <c>test.ps1</c> sets <see cref="Variable"/> to a folder shared by every test process of a gate. A master lives in
/// <c>name-key</c>, where the key hashes the assemblies that write the fixture (this project, the LibLCM host
/// wrapper and LibLCM), so changing any of them builds a fresh master. The first process to need a master takes
/// a file lock, builds in a private folder and publishes it by renaming the folder. Other callers reuse the
/// completed publication. If Windows denies the rename, the builder can still copy from its private master.
/// A master is never opened again after it is saved.
/// </para>
/// <para>
/// With the variable unset (a bare <c>dotnet test</c>), nothing is shared and the caller builds and deletes its own
/// master exactly as it did before this cache existed.
/// </para>
/// </remarks>
internal static class FixtureDiskCache
{
    /// <summary>The environment variable naming the folder shared by a gate's test processes.</summary>
    public const string Variable = "MOTIF_TEST_FIXTURE_CACHE";

    private const string MarkerName = "ready.json";
    private const int WindowsAccessDenied = unchecked((int)0x80070005);
    private const int WindowsSharingViolation = unchecked((int)0x80070020);
    private static readonly TimeSpan LockBound = TimeSpan.FromMinutes(15);
    private static readonly Lazy<string> InputKey = new(ComputeInputKey);

    /// <summary>A built master: the folder holding <c>MotifTestProj/</c>, its metadata and whether it is shared.</summary>
    internal sealed record Built<T>(string Root, T Metadata, bool Shared);

    /// <summary>
    /// Returns the master called <paramref name="name"/>, building it with <paramref name="build"/> (given an empty folder
    /// to build in, returning the metadata to keep beside it) only if no process has yet.
    /// </summary>
    public static Built<T> GetOrBuild<T>(string name, Func<string, T> build) where T : class
    {
        var cacheRoot = Environment.GetEnvironmentVariable(Variable);
        if (string.IsNullOrWhiteSpace(cacheRoot))
        {
            var own = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests.Master", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(own);
            return new(own, build(own), false);
        }

        Directory.CreateDirectory(cacheRoot);
        var final = Path.Combine(cacheRoot, name + "-" + InputKey.Value);
        if (TryRead<T>(final) is { } existing) return new(final, existing, true);

        using var gate = AcquireLock(final + ".lock");
        if (TryRead<T>(final) is { } builtMeanwhile) return new(final, builtMeanwhile, true);

        var staging = final + ".building-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(staging);
        try { return Publish(staging, final, build(staging)); }
        catch
        {
            DeleteStaging(staging);
            throw;
        }
    }

    /// <summary>Publishes the finished master or returns a reusable master when the rename gets refused.</summary>
    internal static Built<T> Publish<T>(string staging, string final, T metadata) where T : class
    {
        File.WriteAllText(Path.Combine(staging, MarkerName), JsonSerializer.Serialize(metadata));
        try
        {
            Directory.Move(staging, final);
            return new(final, metadata, true);
        }
        catch (IOException exception)
        {
            if (TryRead<T>(final) is { } winner)
            {
                DeleteStaging(staging);
                return new(final, winner, true);
            }

            // An open descendant can deny the rename on Windows even though the fixture is readable.
            var renameDenied = OperatingSystem.IsWindows() &&
                exception.HResult is WindowsAccessDenied or WindowsSharingViolation;
            if (renameDenied || Directory.Exists(final)) return new(staging, metadata, false);
            throw;
        }
    }

    private static void DeleteStaging(string staging)
    {
        try { Directory.Delete(staging, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static T? TryRead<T>(string folder) where T : class
    {
        var marker = Path.Combine(folder, MarkerName);
        return File.Exists(marker) ? JsonSerializer.Deserialize<T>(File.ReadAllText(marker)) : default;
    }

    private static FileStream AcquireLock(string path)
    {
        var deadline = DateTime.UtcNow + LockBound;
        while (true)
        {
            try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < deadline) { Thread.Sleep(200); }
        }
    }

    private static string ComputeInputKey()
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var assembly in new[]
                 {
                     typeof(FixtureDiskCache).Assembly, typeof(FwDataProjectLoader).Assembly, typeof(LcmCache).Assembly,
                 })
        {
            var path = assembly.Location;
            hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetFileName(path)));
            hash.AppendData(File.ReadAllBytes(path));
        }
        return Convert.ToHexString(hash.GetHashAndReset())[..16].ToLowerInvariant();
    }
}
