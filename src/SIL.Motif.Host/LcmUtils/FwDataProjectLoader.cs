// Adapted from languageforge-lexbox's FwDataMiniLcmBridge/LcmUtils/ProjectLoader.cs (SIL Global, MIT).

using System.Reflection;
using System.Runtime.InteropServices;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Core.WritingSystems;
using SIL.LCModel.Utils;
using SIL.WritingSystems;

namespace SIL.Motif.Host.LcmUtils;

/// <summary>
/// Opens an existing FieldWorks <c>.fwdata</c> project headlessly: performs the required ICU/SLDR
/// native initialization once per process, then loads the project via
/// <see cref="LcmCache.CreateCacheFromLocalProjectFile"/> with a non-interactive
/// <see cref="ILcmUI"/> and progress shim.
/// </summary>
public class FwDataProjectLoader
{
    private static bool _init;
    private static readonly object InitLock = new();
    private static IntPtr[] _loadedIcuLibraryHandles = [];
    private static string[] _loadedIcuLibraryPaths = [];

    /// <summary>
    /// Initializes ICU and SLDR. Idempotent — only runs once per process. Must happen before any
    /// <see cref="LcmCache"/> is created; this is the classic headless-load blocker if skipped or
    /// ordered wrong. If <c>MOTIF_WRITING_SYSTEM_REPOSITORY_PATH</c> is set before the first call,
    /// its directory becomes this process's global writing-system repository. When unset, this
    /// method leaves the current repository slot untouched. If <see cref="SldrOfflineVariable"/> is set,
    /// the SLDR answers from its local cache and never from the network.
    /// </summary>
    public static void Init()
    {
        if (_init) return;

        lock (InitLock)
        {
            if (_init) return;

            var dataDirectory = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "IcuData", "icudt70l"));

            var overrideDataPath = Path.GetFullPath(Path.Combine(
                dataDirectory, "..", "data", "UnicodeDataOverrides.txt"));
            var requiredDataFiles = new[]
            {
                Path.Combine(dataDirectory, "nfc_fw.nrm"),
                Path.Combine(dataDirectory, "nfkc_fw.nrm"),
                overrideDataPath,
            };
            var missingDataFiles = requiredDataFiles.Where(path => !File.Exists(path)).ToArray();
            if (missingDataFiles.Length > 0)
            {
                throw CreateCustomIcuFailure(dataDirectory, [],
                    "bundled FieldWorks normalization data is missing: " + string.Join(", ", missingDataFiles));
            }

            // .NET binds its own ICU on first culture use; after SIL ICU 70 is loaded that bind can abort on macOS.
            _ = string.Compare("a", "b", StringComparison.CurrentCulture);

            string[] loadedLibraries;
            try
            {
                loadedLibraries = LoadBundledIcuLibraries();
                Icu.Wrapper.DataDirectory = dataDirectory;
                RegisterCustomIcuResolver(loadedLibraries);
                InitializeCustomIcuDataDirectory(dataDirectory);
            }
            catch (Exception exception)
            {
                throw CreateCustomIcuFailure(dataDirectory, _loadedIcuLibraryPaths,
                    exception.GetType().Name + ": " + exception.Message, exception);
            }

            if (!CustomIcu.HaveCustomIcuLibrary)
            {
                throw CreateCustomIcuFailure(dataDirectory, loadedLibraries,
                    "LibLCM reports HaveCustomIcuLibrary=false and would use stock normalization");
            }

            InitializeSldr();
            InstallConfiguredGlobalWritingSystemRepository();
            _init = true;
        }
    }

    private static void InitializeCustomIcuDataDirectory(string dataDirectory)
    {
        var previousIcuDataDirectory = Environment.GetEnvironmentVariable("ICU_DATA");
        try
        {
            Environment.SetEnvironmentVariable("ICU_DATA", dataDirectory, EnvironmentVariableTarget.Process);
            CustomIcu.InitIcuDataDir();
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "ICU_DATA", previousIcuDataDirectory, EnvironmentVariableTarget.Process);
        }
    }

    private static string[] LoadBundledIcuLibraries()
    {
        var baseDirectory = AppContext.BaseDirectory;
        string libraryDirectory;
        string[] libraryNames;

        if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            libraryDirectory = Path.Combine(baseDirectory, "lib", "win-x64");
            libraryNames = ["icudt70.dll", "icuuc70.dll", "icuin70.dll", "icuio70.dll", "icutu70.dll"];
        }
        else if (OperatingSystem.IsLinux() && RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            libraryDirectory = baseDirectory;
            libraryNames =
            [
                "libicudata.so.70", "libicuuc.so.70", "libicui18n.so.70", "libicuio.so.70", "libicutu.so.70",
            ];
        }
        else if (OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture is Architecture.Arm64 or Architecture.X64)
        {
            libraryDirectory = baseDirectory;
            libraryNames =
            [
                "libicudata.70.dylib", "libicuuc.70.dylib", "libicui18n.70.dylib", "libicuio.70.dylib",
                "libicutu.70.dylib",
            ];
        }
        else
        {
            throw new PlatformNotSupportedException(
                $"SIL ICU 70 is not configured for {RuntimeInformation.ProcessArchitecture} on {RuntimeInformation.OSDescription}.");
        }

        var paths = libraryNames.Select(name => Path.Combine(libraryDirectory, name)).ToArray();
        var missingPaths = paths.Where(path => !File.Exists(path)).ToArray();
        if (missingPaths.Length > 0)
        {
            throw new DllNotFoundException(
                "Expected bundled SIL ICU libraries were not found: " + string.Join(", ", missingPaths));
        }

        var handles = new List<IntPtr>(paths.Length);
        var loadedPaths = new List<string>(paths.Length);
        try
        {
            foreach (var path in paths)
            {
                handles.Add(NativeLibrary.Load(path));
                loadedPaths.Add(path);
            }
        }
        finally
        {
            _loadedIcuLibraryHandles = handles.ToArray();
            _loadedIcuLibraryPaths = loadedPaths.ToArray();
        }

        return paths;
    }

    private static void RegisterCustomIcuResolver(IReadOnlyCollection<string> loadedLibraries)
    {
        var icuuc = loadedLibraries.First(path => Path.GetFileName(path).StartsWith(
            OperatingSystem.IsWindows() ? "icuuc" : "libicuuc", StringComparison.OrdinalIgnoreCase));
        NativeLibrary.SetDllImportResolver(typeof(CustomIcu).Assembly, (name, _, _) =>
            string.Equals(name, "icuuc70.dll", StringComparison.OrdinalIgnoreCase)
                ? NativeLibrary.Load(icuuc)
                : IntPtr.Zero);
    }

    private static InvalidOperationException CreateCustomIcuFailure(
        string dataDirectory, IReadOnlyCollection<string> loadedLibraries, string cause, Exception? innerException = null)
    {
        var loaded = loadedLibraries.Count == 0
            ? "No bundled ICU native library was loaded."
            : "Bundled ICU native libraries loaded from: " + string.Join(", ", loadedLibraries) + ".";
        return new InvalidOperationException(
            "Motif refuses to open a FieldWorks project without SIL ICU 70. " + cause + ". " + loaded + " " +
            $"Motif's bundled ICU data directory is '{dataDirectory}'. Expected nfc_fw.nrm, nfkc_fw.nrm, " +
            $"and UnicodeDataOverrides.txt beside that directory or its parent data directory.",
            innerException);
    }
    /// <summary>
    /// Keeps a process's writing-system lookups off the network, for it and every process it starts.
    /// <b>Test-only.</b>
    /// </summary>
    /// <remarks>
    /// An online SLDR lookup is an HTTPS request made while holding the SLDR cache's machine-wide lock
    /// (SIL.WritingSystems <c>Sldr.GetLdmlFile</c> and <c>Sldr.DownloadLanguageTags</c>), and the first
    /// cache opened in a process makes several of them. Concurrent processes therefore open their first
    /// cache one network round trip at a time, and a runner a test starts waits behind every other test
    /// process on the machine. A test process sets this at load so its lookups, and those of the runner and
    /// command-line processes it starts, read only the local SLDR cache, as LibLCM's own tests do. Pinned by
    /// `InitKeepsSldrLookupsOffTheNetworkWhenTheEnvironmentAsks`.
    /// </remarks>
    internal const string SldrOfflineVariable = "MOTIF_TEST_SLDR_OFFLINE";

    private static bool SldrOfflineRequested() =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(SldrOfflineVariable));

    /// <summary>
    /// Points a process's SLDR cache at a private directory instead of the machine-wide one. <b>Test-only.</b>
    /// </summary>
    /// <remarks>
    /// The machine-wide cache holds whatever SLDR data earlier FieldWorks or Motif runs on that machine
    /// downloaded, so an offline lookup there returns about 1.4 MB of LDML per writing system on one machine
    /// and nothing on a fresh one. Every writing system a test project creates then carries that data, and
    /// every later open of the project re-parses it and recompiles its non-default ICU collations, which
    /// costs several hundred milliseconds per open. A test process sets this to an empty directory, so each
    /// process gets the bare writing systems a fresh machine gets, whatever machine it runs on. Pinned by
    /// `InitReadsTheSldrCacheTheEnvironmentNames`.
    /// </remarks>
    internal const string SldrCachePathVariable = "MOTIF_TEST_SLDR_CACHE_PATH";

    private static void InitializeSldr()
    {
        var cachePath = Environment.GetEnvironmentVariable(SldrCachePathVariable);
        if (string.IsNullOrWhiteSpace(cachePath))
        {
            Sldr.Initialize(offlineTestMode: SldrOfflineRequested());
            return;
        }

        Directory.CreateDirectory(cachePath);
        // The cache-path overload is internal to SIL.WritingSystems; the pinning test fails if it is renamed.
        var initialize = typeof(Sldr).GetMethod(nameof(Sldr.Initialize), BindingFlags.Static | BindingFlags.NonPublic,
            [typeof(bool), typeof(string)]) ?? throw new InvalidOperationException(
            $"SIL.WritingSystems no longer offers Sldr.Initialize(bool, string), which '{SldrCachePathVariable}' needs.");
        initialize.Invoke(null, [SldrOfflineRequested(), Path.GetFullPath(cachePath)]);
    }

    private const string WritingSystemRepositoryPathEnvironmentVariable = "MOTIF_WRITING_SYSTEM_REPOSITORY_PATH";

    // The path constructor is internal; InitInstallsTheRepositorySelectedByTheEnvironment pins this route.
    private static void InstallConfiguredGlobalWritingSystemRepository()
    {
        var configuredPath = Environment.GetEnvironmentVariable(WritingSystemRepositoryPathEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(configuredPath)) return;

        var repositoryPath = Path.GetFullPath(configuredPath);
        Directory.CreateDirectory(repositoryPath);
        var repository = (CoreGlobalWritingSystemRepository?)Activator.CreateInstance(
            typeof(CoreGlobalWritingSystemRepository),
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null, args: [repositoryPath], culture: null);
        if (repository is null)
        {
            throw new InvalidOperationException(
                "LibLCM did not create a writing-system repository for " +
                $"'{WritingSystemRepositoryPathEnvironmentVariable}'.");
        }

        var key = typeof(CoreGlobalWritingSystemRepository).FullName!;
        if (SingletonsContainer.Item(key) is not null) SingletonsContainer.Remove(key);
        SingletonsContainer.Add(key, repository);
    }

    /// <summary>
    /// Opens an existing <c>.fwdata</c> project.
    /// </summary>
    /// <param name="fwDataFilePath">
    /// Full path to the project's .fwdata file. Expected layout is
    /// <c>{projectsFolder}/{projectName}/{projectName}.fwdata</c>, matching FieldWorks' own
    /// project-folder convention.
    /// </param>
    /// <param name="templatesFolder">
    /// Folder LibLCM expects to exist for new-project templates. Its contents are not needed to
    /// open an existing project; defaults to a scratch folder under the temp path.
    /// </param>
    /// <remarks>
    /// Which one do I want? If the caller does not own the project and intend to persist it — a
    /// copy, a temp project, a read-only analysis — use <see cref="LoadScratchCache"/> instead.
    /// </remarks>
    public virtual LcmCache LoadCache(string fwDataFilePath, string? templatesFolder = null) =>
        Serialized(() => LoadCacheCore(fwDataFilePath, templatesFolder));

    // One cache opens at a time in a process: LibLCM's startup is not thread-safe, and neither is the decoy swap.
    private static readonly object LoadGate = new();

    /// <summary>
    /// Runs <paramref name="open"/> while no other cache in this process is being opened through this type.
    /// </summary>
    /// <remarks>
    /// Two overlapping opens corrupt LibLCM's own project startup, and two overlapping scratch opens leak the
    /// writing-system decoy into the process-wide slot for good, since the second saves the first's decoy as
    /// the repository to restore. Every later real open would then silently stop persisting writing
    /// systems. Code that creates a cache through LibLCM directly must go through this gate too.
    /// Pinned by <c>ConcurrentScratchLoadsLeaveTheProcessRepositoryInstalled</c>.
    /// </remarks>
    internal static T Serialized<T>(Func<T> open)
    {
        lock (LoadGate) return open();
    }

    // Non-virtual, so LoadScratchCache bypasses the virtual LoadCache: overriding one never double-counts.
    private static LcmCache LoadCacheCore(string fwDataFilePath, string? templatesFolder)
    {
        Init();

        var projectFolder = Path.GetDirectoryName(Path.GetFullPath(fwDataFilePath))
            ?? throw new ArgumentException($"Could not determine project folder from '{fwDataFilePath}'.", nameof(fwDataFilePath));
        var projectsPath = Path.GetDirectoryName(projectFolder)
            ?? throw new ArgumentException($"Could not determine projects folder from '{fwDataFilePath}'.", nameof(fwDataFilePath));

        templatesFolder ??= Path.Combine(Path.GetTempPath(), "SIL.Motif.Templates");
        if (!Directory.Exists(projectsPath)) Directory.CreateDirectory(projectsPath);
        if (!Directory.Exists(templatesFolder)) Directory.CreateDirectory(templatesFolder);

        var lcmDirectories = new LcmDirectories(projectsPath, templatesFolder);
        var progress = new LcmThreadedProgress();
        return LcmCache.CreateCacheFromLocalProjectFile(
            fwDataFilePath,
            null,
            new HeadlessLcmUi(progress.SynchronizeInvoke),
            lcmDirectories,
            new LcmSettings(),
            progress);
    }

    /// <summary>
    /// Opens an existing <c>.fwdata</c> project the same way <see cref="LoadCache"/> does, except that
    /// disposing the returned cache cannot persist writing-system changes to the process's global repository.
    /// </summary>
    /// <remarks>
    /// For a throwaway scratch (ADR 0016), persisting writing-system changes is wrong: a Dry Run scratch
    /// is a proposal being tried and discarded. See <see cref="DiscardingGlobalWritingSystemRepository"/>
    /// for the mechanism. <see cref="LoadCache"/> remains the method for a real, persisted project open.
    /// </remarks>
    public virtual LcmCache LoadScratchCache(string fwDataFilePath, string? templatesFolder = null)
    {
        Init();
        return Serialized(() =>
        {
            using (SuppressGlobalWritingSystemPersistence())
                return LoadCacheCore(fwDataFilePath, templatesFolder);
        });
    }

    // Key SingletonsContainer stores the shared writing-system repository under (BackendProvider.cs).
    private static readonly string GlobalWritingSystemRepositoryKey =
        typeof(CoreGlobalWritingSystemRepository).FullName!;

    // One decoy for the process: the base holds a GlobalMutex, so one per scratch would leak a handle.
    private static readonly DiscardingGlobalWritingSystemRepository SharedDecoy = new();

    // Swaps the decoy in for one cache open, so that cache is wired to it for life (see the decoy's remarks).
    private static IDisposable SuppressGlobalWritingSystemPersistence()
    {
        var restore = SingletonsContainer.Item(GlobalWritingSystemRepositoryKey) as CoreGlobalWritingSystemRepository;
        if (restore is not null) SingletonsContainer.Remove(GlobalWritingSystemRepositoryKey);

        SingletonsContainer.Add(GlobalWritingSystemRepositoryKey, SharedDecoy);
        return new RestoreGlobalWritingSystemRepository(restore);
    }

    // Undoes SuppressGlobalWritingSystemPersistence: later cache opens see the real repository again.
    private sealed class RestoreGlobalWritingSystemRepository : IDisposable
    {
        private readonly CoreGlobalWritingSystemRepository? _restore;

        public RestoreGlobalWritingSystemRepository(CoreGlobalWritingSystemRepository? restore)
        {
            _restore = restore;
        }

        public void Dispose()
        {
            SingletonsContainer.Remove(GlobalWritingSystemRepositoryKey);
            if (_restore is not null) SingletonsContainer.Add(GlobalWritingSystemRepositoryKey, _restore);
        }
    }

    /// <summary>
    /// Persists every committed change in <paramref name="cache"/> to its backing <c>.fwdata</c>
    /// file, and <b>does not return until the bytes are actually there</b>. The core does not save
    /// projects itself (the Change Set contract's Application Receipt semantics):
    /// <c>SIL.Motif.Runner.Apply.ProposalApplier.Apply</c> commits its unit of work but never calls
    /// this — it is the host's job, called only after every unit of work has closed (never while a
    /// task is open — ADR 0005 decision 3; ADR 0006 decision 4).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="IActionHandler.Commit"/> call mirrors
    /// <c>FwDataMiniLcmBridge.Api.FwDataMiniLcmApi.Save()</c>
    /// (languageforge-lexbox/backend/FwLite/FwDataMiniLcmBridge/Api/FwDataMiniLcmApi.cs): for this
    /// backend, <c>Commit()</c> is both "close out the undo/redo bookkeeping since the last commit" and
    /// the save trigger — there is no separate LibLCM "save" verb to call instead.
    /// </para>
    /// <para>
    /// <b>But <c>Commit()</c> alone does not put anything on disk.</b>
    /// <c>XMLBackendProvider.PerformCommit</c> (liblcm
    /// <c>Infrastructure/Impl/XMLBackendProvider.cs:458-467</c>) only enqueues a <c>CommitWork</c> item
    /// on a background <c>ConsumerThread</c> and returns; the write lands whenever that thread gets to
    /// it. <c>IUndoStackManager.Save()</c> is no better — <c>UnitOfWorkService.SaveInternal</c> reaches
    /// the same <c>Commit</c>. Nothing in Motif noticed until the Dry Run started copying the project
    /// file immediately after saving and read a file that was still one operation behind, which
    /// surfaced as "footprint drift" on an apply where nothing had drifted.
    /// </para>
    /// <para>
    /// LibLCM's own answer is the <c>CompleteAllCommits()</c> barrier, which waits for that thread to go
    /// idle, and both places liblcm needs the file to be authoritative pair the two calls:
    /// <c>ProjectLockingService.UnlockCurrentProject</c> (<c>DomainServices/ProjectLockingService.cs:37-41</c>)
    /// and <c>ProjectBackupService</c> (<c>:61</c>). So this is the documented sequence, not an invention.
    /// </para>
    /// <para>
    /// <b>The one wart:</b> that barrier is not on Motif's side of the fence. It is declared on the
    /// <c>internal</c> <c>IDataStorer</c>, so the only reachable route is the public
    /// <c>ILcmServiceLocator.DataSetup</c> property — which returns the very same backend-provider
    /// instance (liblcm <c>LcmCache.cs:631</c> casts it: <c>var bep = (IDataStorer)m_serviceLocator.DataSetup;</c>)
    /// — and then its <c>public virtual CompleteAllCommits</c> by reflection. It is one call, resolved
    /// once, and it throws rather than degrading if a future liblcm renames it, because silently
    /// falling back to the asynchronous behaviour would restore a bug whose symptom points at the wrong
    /// component entirely. <b>Making a synchronous save publicly reachable is an upstream ask on
    /// liblcm</b> (ADR 0016), and this reflection is what should be
    /// deleted when it lands.
    /// </para>
    /// </remarks>
    public virtual void Save(LcmCache cache)
    {
        if (cache is null) throw new ArgumentNullException(nameof(cache));

        cache.ActionHandlerAccessor.Commit();
        WaitForPendingWritesToReachDisk(cache);
    }

    private static MethodInfo? _completeAllCommits;

    /// <summary>Blocks until the commit thread drains; see <see cref="Save"/>'s remarks for why.</summary>
    private static void WaitForPendingWritesToReachDisk(LcmCache cache)
    {
        var backend = cache.ServiceLocator.DataSetup;

        var flush = _completeAllCommits ??= backend.GetType().GetMethod(
            "CompleteAllCommits", BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);

        if (flush is null)
        {
            throw new InvalidOperationException(
                $"This LibLCM's backend provider ({backend.GetType().FullName}) has no public " +
                "CompleteAllCommits() to wait on, so Save cannot guarantee the .fwdata file is current. " +
                "Refusing rather than returning from a save that has not saved: a Dry Run copies the " +
                "file immediately afterwards, and a stale copy produces a false 'footprint drift' " +
                "report on apply (ADR 0016).");
        }

        flush.Invoke(backend, null);
    }
}
