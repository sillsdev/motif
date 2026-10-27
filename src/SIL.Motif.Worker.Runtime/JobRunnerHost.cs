using SIL.Motif.Host;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Scheduling;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker;

/// <summary>
/// The composition root of one user's job runner for one root: one owner lock, one runtime registry, and the
/// per-project lanes its work is scheduled through.
/// </summary>
/// <remarks>
/// <para>
/// It answers no requests. Coordination with a <c>motif</c> invocation is the paired database and the
/// database's own owner lock, not a message, so this type has no endpoint, no connections, and no
/// protocol. What it owns is singularity — one runner per platform user and worker root, enforced by an owner lock —
/// and the lifetime of everything scheduled work needs.
/// </para>
/// <para>
/// The registry is composed once, before the runner starts, and is closed to further composition
/// afterwards. That ordering is what lets disposal be deterministic: lanes before runtimes, runtimes
/// before the lock that admitted them.
/// </para>
/// </remarks>
public sealed class JobRunnerHost : IDisposable
{
    private readonly WorkerMutexOwner _ownerMutex;
    private readonly ProjectHostRegistry _hostRegistry;
    private readonly ProjectHostReleaseCoordinator _hostReleases;
    private readonly ConcurrentDictionary<string, ProjectFreshnessTracker> _freshness =
        new ConcurrentDictionary<string, ProjectFreshnessTracker>(StringComparer.Ordinal);
    private readonly CancellationTokenSource _shutdown = new CancellationTokenSource();
    private readonly object _gate = new object();
    private ProjectRuntimeRegistry? _runtimeRegistry;
    private ProjectLaneRegistry? _projectLanes;
    private BaselineWorkspaceCatalog? _baselineWorkspaces;
    private string? _ownedTestRoot;
    private bool _ownsRuntimeRegistry;
    private Action<ProjectRuntimeRegistry>? _runtimeRegistryDisposeOverride;
    private bool _started;
    private bool _disposed;

    /// <summary>Creates a runner host for the current user.</summary>
    public JobRunnerHost()
        : this(WorkerIdentity.GetCurrentUserNamespace(), RunnerOptions.ResolveRoot())
    {
    }

    internal JobRunnerHost(string userNamespace, string? workerRoot = null)
    {
        var identity = string.IsNullOrWhiteSpace(userNamespace) ? "unknown-user" : userNamespace;
        OwnerName = workerRoot is null
            ? GetOwnerMutexNameForNamespace(identity)
            : GetOwnerMutexNameForRoot(workerRoot, identity);
        _ownerMutex = workerRoot is null
            ? new WorkerMutexOwner(OwnerName)
            : WorkerMutexOwner.ForRoot(OwnerName);
        _hostRegistry = new ProjectHostRegistry();
        _hostReleases = new ProjectHostReleaseCoordinator();
    }

    /// <summary>Creates a test runner whose owner lock is isolated to the given namespace.</summary>
    /// <remarks>
    /// Product runners use <see cref="ForRoot"/> so ownership follows the root. Tests can use this method
    /// to keep their owner lock away from a developer's default installation.
    /// </remarks>
    public static JobRunnerHost ForNamespace(string ownerNamespace)
    {
        if (string.IsNullOrWhiteSpace(ownerNamespace))
            throw new ArgumentException("A runner namespace is required.", nameof(ownerNamespace));
        return new JobRunnerHost(ownerNamespace);
    }

    /// <summary>Creates a runner whose owner lock is scoped to this user's worker root.</summary>
    /// <param name="workerRoot">The directory whose queue, Baselines, and parser artifacts this runner owns.</param>
    /// <param name="ownerNamespace">An optional identity override for tests that must avoid a user's normal lock.</param>
    public static JobRunnerHost ForRoot(string workerRoot, string? ownerNamespace = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerRoot);
        return new JobRunnerHost(ownerNamespace ?? WorkerIdentity.GetCurrentUserNamespace(), workerRoot);
    }

    /// <summary>Creates an isolated runner identity for tests only.</summary>
    internal static JobRunnerHost CreateForTests(string userNamespace, bool composeRuntime = true,
        string? workerRoot = null)
    {
        var host = new JobRunnerHost(userNamespace);
        var root = workerRoot ?? Path.Combine(
            Path.GetTempPath(), "motif-runner-test-" + Guid.NewGuid().ToString("N"));
        var ownership = WorkspaceOwnership.Bootstrap(root);
        host.ConfigureWorkspaces(ownership);
        host._ownedTestRoot = workerRoot is null ? root : null;
        if (composeRuntime)
        {
            var catalog = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, MotifProductVersion.Current);
            host.CreateRuntimeRegistry(catalog,
                (jobs, key) => new WorkerRecoveryCoordinator(new WorkerRecovery(jobs),
                    new WorkspaceCleaner(ownership)));
        }
        return host;
    }

    /// <summary>The platform lock name which serializes runner ownership for one user and worker root.</summary>
    public string OwnerName { get; }

    /// <summary>Whether this process currently owns the runner lock for its worker root.</summary>
    public bool IsOwner { get; private set; }

    internal ProjectLaneRegistry ProjectLanes => _projectLanes ??
        throw new InvalidOperationException("The project scheduler is not composed.");

    internal ProjectHostReleaseCoordinator HostReleases => _hostReleases;

    internal ProjectHostRegistry HostRegistry => _hostRegistry;

    /// <summary>Takes the root-scoped lock, making this process the one runner for that root.</summary>
    public bool TryAcquireOwnership()
    {
        lock (_gate)
        {
            if (IsOwner) return true;
            IsOwner = _ownerMutex.TryAcquire();
            return IsOwner;
        }
    }

    /// <summary>Closes composition; every later call that would add to it is refused.</summary>
    public void Start()
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            _started = true;
        }
    }

    /// <summary>Creates the runtime registry and the per-project lanes bound to it.</summary>
    internal ProjectRuntimeRegistry CreateRuntimeRegistry(ProjectDatabaseCatalog catalog,
        Func<JobRepository, string, WorkerRecoveryCoordinator> recoveryFactory,
        Func<DateTimeOffset>? now = null)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_started)
                throw new InvalidOperationException("Runtime composition is closed after the runner starts.");
            if (_runtimeRegistry is not null)
                throw new InvalidOperationException("The runner runtime registry is already composed.");
            _runtimeRegistry = new ProjectRuntimeRegistry(catalog, recoveryFactory, now, _hostRegistry);
            _projectLanes = new ProjectLaneRegistry(key =>
            {
                if (!_runtimeRegistry.TryGet(key, out var runtime))
                    throw new InvalidOperationException("A Ready project runtime is required for its lane.");
                return runtime.Baselines.GetCurrent(key)?.Token ??
                    throw new InvalidOperationException("A published Baseline is required for its lane.");
            });
            _ownsRuntimeRegistry = true;
            return _runtimeRegistry;
        }
    }

    /// <summary>Composes the Baseline workspace catalog this runner publishes into.</summary>
    internal void ConfigureWorkspaces(IWorkspaceOwnership ownership)
    {
        ArgumentNullException.ThrowIfNull(ownership);
        lock (_gate)
        {
            ThrowIfDisposed();
            if (_started)
                throw new InvalidOperationException("Workspace composition is closed after the runner starts.");
            if (_baselineWorkspaces is not null)
                throw new InvalidOperationException("The runner workspace catalog is already composed.");
            _baselineWorkspaces = new BaselineWorkspaceCatalog(ownership);
        }
    }

    /// <summary>Injects a disposal failure for testing cleanup ordering and aggregation.</summary>
    internal void SetRuntimeRegistryDisposeOverrideForTests(Action<ProjectRuntimeRegistry> dispose)
    {
        ArgumentNullException.ThrowIfNull(dispose);
        lock (_gate)
        {
            if (!_ownsRuntimeRegistry || _runtimeRegistry is null)
                throw new InvalidOperationException("Only an owned test registry can be overridden.");
            _runtimeRegistryDisposeOverride = dispose;
        }
    }

    /// <summary>Derives the runner owner lock name for the current user and configured root.</summary>
    public static string GetOwnerMutexName() => GetOwnerMutexNameForRoot(
        RunnerOptions.ResolveRoot(), WorkerIdentity.GetCurrentUserNamespace());

    internal static string GetOwnerMutexNameForRoot(string workerRoot, string userNamespace)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(userNamespace);
        var root = ResolvePhysicalPath(workerRoot);
        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            root = root.ToUpperInvariant();
        var rootDigest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)));
        return GetOwnerMutexNameForNamespace(userNamespace) + ".Root." + rootDigest;
    }

    private static string ResolvePhysicalPath(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var pathRoot = Path.GetPathRoot(fullPath) ?? throw new ArgumentException("The root path is invalid.", nameof(path));
        var current = pathRoot;
        var remaining = new LinkedList<string>(fullPath[pathRoot.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries));
        var hops = 0;
        while (remaining.First is { } first)
        {
            remaining.RemoveFirst();
            var candidate = Path.Combine(current, first.Value);
            FileSystemInfo entry = Directory.Exists(candidate) ? new DirectoryInfo(candidate) : new FileInfo(candidate);
            // One hop at a time: a target can itself pass through a linked folder, as /var does on macOS.
            var target = entry.Exists ? entry.ResolveLinkTarget(returnFinalTarget: false) : null;
            if (target is null)
            {
                current = candidate;
                continue;
            }
            if (++hops > MaximumLinkHops) throw new IOException("The runner root passes through too many symbolic links.");
            var targetPath = Path.GetFullPath(target.FullName);
            var targetRoot = Path.GetPathRoot(targetPath) ?? pathRoot;
            current = targetRoot;
            var targetSegments = targetPath[targetRoot.Length..].Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            for (var index = targetSegments.Length - 1; index >= 0; index--) remaining.AddFirst(targetSegments[index]);
        }
        return Path.TrimEndingDirectorySeparator(current);
    }

    private const int MaximumLinkHops = 40;

    internal static string GetOwnerMutexNameForNamespace(string userNamespace) => OperatingSystem.IsWindows()
        ? @"Local\SIL.Motif.Worker.Owner." + userNamespace
        : "SIL.Motif.Worker.Owner." + userNamespace;

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(JobRunnerHost));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var failures = new List<Exception>();
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _shutdown.Cancel();
        }
        // Lanes before runtimes, runtimes before the lock that admitted them.
        if (_projectLanes is not null)
        {
            try { _projectLanes.Dispose(); }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (_ownsRuntimeRegistry && _runtimeRegistry is not null)
        {
            try { (_runtimeRegistryDisposeOverride ?? (registry => registry.Dispose()))(_runtimeRegistry); }
            catch (Exception exception) { failures.Add(exception); }
        }
        if (_ownedTestRoot is not null)
        {
            try { Directory.Delete(_ownedTestRoot, true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        try { _hostRegistry.Dispose(); }
        catch (Exception exception) { failures.Add(exception); }
        try { _hostReleases.Dispose(); }
        catch (Exception exception) { failures.Add(exception); }
        _freshness.Clear();
        if (IsOwner)
        {
            try { _ownerMutex.Release(); }
            catch (Exception exception) { failures.Add(exception); }
        }
        try { _ownerMutex.Dispose(); }
        catch (Exception exception) { failures.Add(exception); }
        try { _shutdown.Dispose(); }
        catch (Exception exception) { failures.Add(exception); }
        if (failures.Count == 1) throw failures[0];
        if (failures.Count > 1)
            throw new AggregateException("Runner shutdown encountered cleanup failures.", failures);
    }
}
