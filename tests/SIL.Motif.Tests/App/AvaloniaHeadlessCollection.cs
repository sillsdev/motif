using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Headless;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Groups every test class that needs a headless Avalonia application into one xUnit collection, so the
/// process-wide Avalonia platform is stood up exactly once.
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
/// is deliberately a plain work queue rather than a dispatcher loop: the smoke test's contract is that a
/// window can be constructed without ever starting one.
/// </para>
/// </remarks>
public sealed class AvaloniaHeadlessFixture : IDisposable
{
    private readonly BlockingCollection<(Action Work, TaskCompletionSource Completion)> _queue = new();
    private readonly Thread _thread;

    public AvaloniaHeadlessFixture()
    {
        var ready = new TaskCompletionSource();
        _thread = new Thread(() => Run(ready)) { IsBackground = true, Name = "Avalonia headless" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.Task.GetAwaiter().GetResult();
    }

    /// <summary>Runs <paramref name="work"/> on the Avalonia thread and rethrows whatever it threw.</summary>
    public void Invoke(Action work)
    {
        var completion = new TaskCompletionSource();
        _queue.Add((work, completion));
        completion.Task.GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join(TimeSpan.FromSeconds(30));
        _queue.Dispose();
    }

    private void Run(TaskCompletionSource ready)
    {
        try
        {
            AppBuilder.Configure<SIL.Motif.App.App>()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions())
                .SetupWithoutStarting();
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
