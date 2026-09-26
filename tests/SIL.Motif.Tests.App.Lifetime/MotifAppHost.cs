using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.ExceptionServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using SIL.Motif.App.Composition;
using SIL.Motif.Commands;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.Lifetime;

/// <summary>
/// Groups every test that starts Motif through its real startup, so they share the one Avalonia setup a
/// process allows, never run at once, and end with the process's one real exit.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class MotifAppHostCollection : ICollectionFixture<MotifAppHostExit>,
    ICollectionFixture<PristineProjectFixture>
{
    public const string Name = "Real App startup (one Avalonia setup per process)";
}

/// <summary>
/// Exits Motif through the classic desktop lifetime once every test in the collection has run, and fails
/// the run if that exit does not close the session. It runs last because that shutdown also ends Avalonia's
/// dispatcher, as <see cref="MotifAppHost"/> records.
/// </summary>
public sealed class MotifAppHostExit : IDisposable
{
    public void Dispose() => MotifAppHost.Shared.ExitThroughLifetime();
}

/// <summary>
/// Starts Motif through the real App startup — the same <c>App</c>, its
/// <c>OnFrameworkInitializationCompleted</c>, and a classic desktop lifetime, under Avalonia.Headless with
/// Skia — and closes and restarts it inside one test process.
/// </summary>
/// <remarks>
/// Avalonia 12.1.2 throws on a second <c>Setup</c> (<c>src/Avalonia.Controls/AppBuilder.cs:349</c>) and a
/// second application lifetime (<c>src/Avalonia.Controls/Application.cs:189</c>), and the classic desktop
/// lifetime's shutdown also shuts the dispatcher down
/// (<c>src/Avalonia.Controls/ApplicationLifetimes/ClassicDesktopStyleApplicationLifetime.cs:232</c>). So the first <see cref="Start"/> sets the platform up with
/// <c>SetupWithLifetime</c>, which runs <c>OnFrameworkInitializationCompleted</c>; <see cref="StopAsync"/>
/// closes the session with <see cref="MotifDesktopSession.CloseAsync"/>, which is what the lifetime's exit
/// calls; and every later start composes into the same lifetime through <c>App.StartDesktop</c>, the method
/// that override consists of. The lifetime's own exit runs once, from <see cref="MotifAppHostExit"/>.
/// Everything runs on one dedicated thread, because Avalonia binds its dispatcher to the thread that set it
/// up.
/// </remarks>
internal sealed class MotifAppHost
{
    private static readonly TimeSpan ExitLimit = TimeSpan.FromSeconds(30);
    private static readonly Lazy<MotifAppHost> SharedHost = new(() => new MotifAppHost());
    private readonly BlockingCollection<(Action Work, TaskCompletionSource Completion)> _queue = new();
    private ClassicDesktopStyleApplicationLifetime? _lifetime;
    private MotifAppOptions? _options;
    private MotifDesktopSession? _session;

    private MotifAppHost()
    {
        var thread = new Thread(Run) { IsBackground = true, Name = "Motif real startup" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    public static MotifAppHost Shared => SharedHost.Value;

    /// <summary>The process's one classic desktop lifetime.</summary>
    public IClassicDesktopStyleApplicationLifetime Lifetime =>
        _lifetime ?? throw new InvalidOperationException("Motif has not been started.");

    private static SIL.Motif.App.App CurrentApp => (SIL.Motif.App.App)Application.Current!;

    /// <summary>
    /// Runs <paramref name="work"/> on the Avalonia thread, pumping the dispatcher until it completes, and
    /// fails with <paramref name="step"/> named when <paramref name="timeout"/> passes first. A session the
    /// work leaves open is closed afterwards, so one failure cannot cascade into the next test.
    /// </summary>
    public void Run(string step, TimeSpan timeout, Func<Task> work)
    {
        var completion = new TaskCompletionSource();
        _queue.Add((() =>
        {
            Exception? failure = null;
            try
            {
                // Awaits in the work must resume on this thread, which is the only one Avalonia accepts.
                AvaloniaSynchronizationContext.InstallIfNeeded();
                Pump(step, timeout, work());
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            if (_session is not null)
            {
                try
                {
                    Pump("close after '" + step + "'", timeout, StopAsync());
                }
                catch (Exception exception)
                {
                    failure = failure is null ? exception : new AggregateException(failure, exception);
                }
            }
            if (failure is not null) ExceptionDispatchInfo.Throw(failure);
        }, completion));
        completion.Task.GetAwaiter().GetResult();
    }

    /// <summary>Starts Motif with <paramref name="options"/>; call it from <see cref="Run"/>.</summary>
    public MotifDesktopSession Start(MotifAppOptions options)
    {
        if (_session is not null) throw new InvalidOperationException("Motif is already running.");
        _options = options;
        if (_lifetime is null)
        {
            _lifetime = new ClassicDesktopStyleApplicationLifetime
            {
                Args = [],
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            AppBuilder.Configure(() => new SIL.Motif.App.App(options))
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithLifetime(_lifetime);
            AvaloniaSynchronizationContext.InstallIfNeeded();
            _session = CurrentApp.Session;
        }
        else
        {
            _session = CurrentApp.StartDesktop(_lifetime, options);
        }
        var session = _session ?? throw new InvalidOperationException("Startup composed no session.");
        session.Window.Show();
        session.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return session;
    }

    /// <summary>
    /// Closes the session as Motif's exit does, and waits for startup's Known-project load and the
    /// workspace's disposal, so nothing still holds the root when the caller deletes it.
    /// </summary>
    public async Task StopAsync()
    {
        var session = _session ?? throw new InvalidOperationException("Motif has not been started.");
        _session = null;
        await session.CloseAsync();
        await session.KnownProjectsLoaded;
    }

    /// <summary>Closes Motif and starts it again with the same inputs, as closing and reopening it does.</summary>
    public async Task<MotifDesktopSession> RestartAsync()
    {
        var options = _options ?? throw new InvalidOperationException("Motif has not been started.");
        await StopAsync();
        return Start(options);
    }

    /// <summary>
    /// Starts a last session and exits through the lifetime's own shutdown, requiring its exit to close the
    /// session. Nothing can start afterwards: the shutdown ends the dispatcher.
    /// </summary>
    public void ExitThroughLifetime()
    {
        if (_lifetime is null || _options is not { } last) return;
        var root = Path.Combine(Path.GetTempPath(), "SIL.Motif.AppExit", Guid.NewGuid().ToString("N"));
        var options = last with
        {
            ManagedRoot = root,
            RunnerLauncher = new NoRunnerLauncher(last.RunnerLauncher.Options with { Root = root }),
        };
        try
        {
            Run("exit through the classic desktop lifetime", ExitLimit, async () =>
            {
                var session = Start(options);
                _session = null;
                _lifetime.Shutdown();
                await session.Closed;
                await session.KnownProjectsLoaded;
            });
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Pump(string step, TimeSpan timeout, Task task)
    {
        var deadline = Stopwatch.GetTimestamp() + (long)(timeout.TotalSeconds * Stopwatch.Frequency);
        while (!task.IsCompleted && Stopwatch.GetTimestamp() < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Yield();
        }
        Dispatcher.UIThread.RunJobs();
        if (!task.IsCompleted) throw new TimeoutException($"'{step}' did not finish within {timeout}.");
        task.GetAwaiter().GetResult();
    }

    private void Run()
    {
        foreach (var (work, completion) in _queue.GetConsumingEnumerable())
        {
            try
            {
                work();
                completion.SetResult();
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
        }
    }
}
