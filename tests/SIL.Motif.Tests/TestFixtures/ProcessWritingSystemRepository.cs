using System.Reflection;
using System.Runtime.CompilerServices;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.Utils;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Points every <c>LcmCache</c> this test process opens at a writing-system repository of its own,
/// instead of the machine-wide <c>%ProgramData%\SIL\WritingSystemRepository</c> store.
/// </summary>
/// <remarks>
/// <para>
/// The machine-wide store is shared mutable state across processes. Every <c>LcmCache.Dispose()</c>
/// saves into it, and each save first stashes the live LDML aside and then moves it back. Two
/// processes saving at once collide on that stash, so each save retries for about 1.85 seconds and then
/// fails silently. The suite runs as several concurrent test processes, so without this every shard
/// would slow its neighbours down. It would also leave in-flight stash files behind that trip
/// <see cref="PristineProjectFixture"/>'s stale-file guard. A private store also keeps test runs from
/// rewriting the developer's own shared writing systems.
/// </para>
/// <para>
/// It is installed as a module initializer, so it is already in place before any test, fixture or
/// in-process product code can open the first cache. LibLCM looks the repository up in
/// <see cref="SingletonsContainer"/> under its type name and creates the default one only when that slot
/// is empty. Child processes the suite launches, such as <c>motif.exe</c>, still use the machine-wide store.
/// </para>
/// </remarks>
internal static class ProcessWritingSystemRepository
{
    private static readonly string Key = typeof(CoreGlobalWritingSystemRepository).FullName!;

    /// <summary>The directory this process's repository lives in.</summary>
    internal static string BasePath { get; } = Path.Combine(
        Path.GetTempPath(), "SIL.Motif.Tests.WritingSystems", Environment.ProcessId + "-" + Guid.NewGuid().ToString("N"));

    /// <summary>The repository installed for this process, or <see langword="null"/> before installation.</summary>
    internal static CoreGlobalWritingSystemRepository? Installed { get; private set; }

    /// <summary>The repository LibLCM will hand the next cache it opens.</summary>
    internal static object? Current => SingletonsContainer.Item(Key);

    [ModuleInitializer]
    internal static void Install()
    {
        Directory.CreateDirectory(BasePath);
        if (SingletonsContainer.Item(Key) is not null) SingletonsContainer.Remove(Key);
        Installed = CreateAt(BasePath);
        SingletonsContainer.Add(Key, Installed);
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(BasePath, recursive: true); }
            catch { /* best effort: a held handle at exit must not fail the run */ }
        };
    }

    // LibLCM keeps the path-taking constructor internal; ProcessWritingSystemRepositoryTests pins that it works.
    private static CoreGlobalWritingSystemRepository CreateAt(string basePath) =>
        (CoreGlobalWritingSystemRepository)Activator.CreateInstance(
            typeof(CoreGlobalWritingSystemRepository),
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null, args: [basePath], culture: null)!;
}
