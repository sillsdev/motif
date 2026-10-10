using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using SIL.Motif.App.Services;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Groups every test class that needs a headless Avalonia application into one xUnit collection, so the
/// process-wide Avalonia platform is stood up exactly once on first use.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class AvaloniaHeadlessCollection : ICollectionFixture<AvaloniaHeadlessFixture>
{
    public const string Name = "Avalonia headless tests (serialized: platform setup runs once per process)";
}

/// <summary>
/// Owns the one thread Avalonia's headless platform is set up on, and runs UI work there.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia binds its platform and dispatcher to whichever thread calls <c>SetupWithoutStarting</c>, and a
/// UI object may only be touched from that thread. xUnit gives no guarantee that a collection fixture's
/// constructor and the test bodies that follow run on the same thread, so setting the platform up inline
/// and then building a window from the test body reaches Avalonia from an arbitrary thread. That happens
/// to work when this collection runs alone and takes the whole test host down when other collections run
/// beside it.
/// </para>
/// <para>
/// So the platform gets a thread of its own here, and <see cref="Invoke"/> is the only way onto it. This
/// remains a plain work queue rather than a dispatcher loop for the smoke tests; asynchronous callers use
/// <see cref="RunUntilComplete"/> when they need dispatcher jobs pumped between awaits.
/// <see cref="RunUntilComplete"/> runs a dispatcher frame that the work's completion or its deadline ends,
/// so the thread sleeps between jobs instead of spinning a core the awaited work needs. The permanent
/// continuation check
/// <see cref="Walkthrough.AvaloniaHeadlessPlatformTests.TaskRunContinuationResumesOnAvaloniaThreadWhenPumped"/>
/// pins that frame as sufficient for an awaited continuation to resume on the Avalonia thread.
/// </para>
/// </remarks>
public sealed class AvaloniaHeadlessFixture : IDisposable
{
    /// <summary>Runs <paramref name="work"/> on the Avalonia thread and rethrows whatever it threw.</summary>
    public void Invoke(Action work)
        => AvaloniaHeadlessPlatform.Invoke(work);

    /// <summary>
    /// Runs work on the Avalonia thread and pumps the dispatcher until it completes or the deadline passes.
    /// </summary>
    public static void RunUntilComplete(Func<Task> work, TimeSpan timeout) =>
        AvaloniaHeadlessPlatform.RunUntilComplete(work, timeout);

    public void Dispose() { }
}

internal static class AvaloniaHeadlessPlatform
{
    private static readonly MethodInfo ResetDispatcher = typeof(Dispatcher).GetMethod(
        "ResetBeforeUnitTests", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(Dispatcher).FullName, "ResetBeforeUnitTests");

    private static readonly Lazy<PlatformThread> Shared = new(
        static () => new PlatformThread(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static void Initialize() => _ = Shared.Value;

    public static bool IsCurrentThread => Shared.IsValueCreated && Shared.Value.IsCurrentThread;

    public static void Invoke(Action work) => Shared.Value.Invoke(work);

    public static void RunUntilComplete(Func<Task> work, TimeSpan timeout) =>
        Shared.Value.RunUntilComplete(work, timeout);

    private sealed class PlatformThread
    {
        private readonly BlockingCollection<(Action Work, TaskCompletionSource Completion)> _queue = new();
        private readonly Thread _thread;

        public PlatformThread()
        {
            // Reset the static dispatcher so setup binds it here; pinned by DedicatedThreadOwnsAvaloniaDispatcher.
            ResetDispatcher.Invoke(null, null);
            var ready = new TaskCompletionSource();
            _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Avalonia headless" };
            if (OperatingSystem.IsWindows())
                _thread.SetApartmentState(ApartmentState.STA);
            using (ExecutionContext.SuppressFlow())
                _thread.Start();
            ready.Task.GetAwaiter().GetResult();
        }

        public bool IsCurrentThread => Thread.CurrentThread == _thread;

        public void Invoke(Action work)
        {
            ArgumentNullException.ThrowIfNull(work);
            var completion = new TaskCompletionSource();
            _queue.Add((work, completion));
            completion.Task.GetAwaiter().GetResult();
        }

        public void RunUntilComplete(Func<Task> work, TimeSpan timeout)
        {
            ArgumentNullException.ThrowIfNull(work);
            if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));

            Invoke(() =>
            {
                var task = work();
                if (!task.IsCompleted)
                {
                    // Blocks in a dispatcher frame rather than spinning, so a two-core runner keeps a core free.
                    var frame = new DispatcherFrame();
                    using var expiry = new CancellationTokenSource(timeout);
                    using var expired = expiry.Token.Register(() => frame.Continue = false);
                    task.ContinueWith(static (_, state) => ((DispatcherFrame)state!).Continue = false, frame,
                        CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                    Dispatcher.UIThread.PushFrame(frame);
                }

                Dispatcher.UIThread.RunJobs();
                if (!task.IsCompleted)
                    throw new TimeoutException($"Avalonia work did not complete within {timeout}.");
                task.GetAwaiter().GetResult();
            });
        }

        private void Run(TaskCompletionSource ready)
        {
            try
            {
                if (!Dispatcher.UIThread.CheckAccess())
                    throw new InvalidOperationException("Avalonia's UI dispatcher belongs to another thread.");

                var previousUiCulture = CultureInfo.CurrentUICulture;
                try
                {
                    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
                    AppBuilder.Configure<SIL.Motif.App.App>()
                        .With(TextStyles.InterfaceFontOptionsForLanguage("en"))
                        .UseSkia()
                        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                        .SetupWithoutStarting();
                }
                finally
                {
                    CultureInfo.CurrentUICulture = previousUiCulture;
                }
                ready.SetResult();
            }
            catch (Exception exception)
            {
                ready.SetException(exception);
                return;
            }

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
}
