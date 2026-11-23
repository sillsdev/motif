using System.Collections.Concurrent;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace SIL.Motif.Tests.App;

/// <summary>
/// A <see cref="FactAttribute"/> whose test runs on one thread with a <see cref="SynchronizationContext"/>, as a page
/// model does on the real UI thread: every await in the test and in the code it drives resumes on that thread, one at a time.
/// </summary>
/// <remarks>
/// xUnit installs no context, so without this an awaited continuation runs on whichever pool thread completed the task.
/// A page model written for the UI thread then changes its state on two threads at once.
/// </remarks>
[XunitTestCaseDiscoverer("SIL.Motif.Tests.App.UiThreadFactDiscoverer", "SIL.Motif.Tests.App")]
public sealed class UiThreadFactAttribute : FactAttribute;

/// <summary>A <see cref="TheoryAttribute"/> that runs each case on the one thread <see cref="UiThreadFactAttribute"/> describes.</summary>
[XunitTestCaseDiscoverer("SIL.Motif.Tests.App.UiThreadTheoryDiscoverer", "SIL.Motif.Tests.App")]
public sealed class UiThreadTheoryAttribute : TheoryAttribute;

public sealed class UiThreadFactDiscoverer(IMessageSink diagnosticMessageSink) : FactDiscoverer(diagnosticMessageSink)
{
    protected override IXunitTestCase CreateTestCase(ITestFrameworkDiscoveryOptions discoveryOptions,
        ITestMethod testMethod, IAttributeInfo factAttribute) =>
        new UiThreadTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(),
            discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod);
}

public sealed class UiThreadTheoryDiscoverer(IMessageSink diagnosticMessageSink) : TheoryDiscoverer(diagnosticMessageSink)
{
    protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(
        ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute,
        object[] dataRow) =>
        [new UiThreadTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(),
            discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, dataRow)];

    protected override IEnumerable<IXunitTestCase> CreateTestCasesForTheory(
        ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute) =>
        [new UiThreadTheoryTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(),
            discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod)];
}

public sealed class UiThreadTestCase : XunitTestCase
{
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes", true)]
    public UiThreadTestCase() { }

    public UiThreadTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay,
        TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod, object[]? testMethodArguments = null)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments) { }

    public override Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus,
        object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
        UiThread.RunAsync(() => base.RunAsync(diagnosticMessageSink, messageBus, constructorArguments, aggregator,
            cancellationTokenSource));
}

public sealed class UiThreadTheoryTestCase : XunitTheoryTestCase
{
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes", true)]
    public UiThreadTheoryTestCase() { }

    public UiThreadTheoryTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay,
        TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod) { }

    public override Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus,
        object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
        UiThread.RunAsync(() => base.RunAsync(diagnosticMessageSink, messageBus, constructorArguments, aggregator,
            cancellationTokenSource));
}

/// <summary>One thread that runs what is posted to it in order, standing in for the UI thread.</summary>
internal sealed class UiThread : SynchronizationContext
{
    private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
    private Thread? _thread;

    public bool IsCurrentThread => Thread.CurrentThread == _thread;

    public override void Post(SendOrPostCallback d, object? state) => _queue.TryAdd((d, state));

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (IsCurrentThread) { d(state); return; }
        using var done = new ManualResetEventSlim();
        Exception? failure = null;
        Post(_ =>
        {
            try { d(state); }
            catch (Exception exception) { failure = exception; }
            finally { done.Set(); }
        }, null);
        done.Wait();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>Starts <paramref name="work"/> on a fresh thread with this context installed, and serves it until it completes.</summary>
    public static Task<T> RunAsync<T>(Func<Task<T>> work)
    {
        var context = new UiThread();
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        context._thread = new Thread(() =>
        {
            SetSynchronizationContext(context);
            try
            {
                var task = work();
                task.ContinueWith(static (_, state) => ((UiThread)state!)._queue.CompleteAdding(), context,
                    CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                foreach (var (callback, state) in context._queue.GetConsumingEnumerable()) callback(state);
                task.GetAwaiter().GetResult();
                result.SetResult(task.Result);
            }
            catch (Exception exception)
            {
                result.SetException(exception);
            }
        })
        { Name = "UI thread (test)", IsBackground = true };
        context._thread.Start();
        return result.Task;
    }
}
