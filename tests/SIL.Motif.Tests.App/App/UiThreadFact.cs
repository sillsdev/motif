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
        UiThreadRun.RunAsync(this, DisplayName, messageBus, () => base.RunAsync(diagnosticMessageSink, messageBus,
            constructorArguments, aggregator, cancellationTokenSource));
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
        UiThreadRun.RunAsync(this, DisplayName, messageBus, () => base.RunAsync(diagnosticMessageSink, messageBus,
            constructorArguments, aggregator, cancellationTokenSource));
}

/// <summary>Runs a test case on the pump and turns a hang into one failed test that names it.</summary>
internal static class UiThreadRun
{
    public static async Task<RunSummary> RunAsync(IXunitTestCase testCase, string displayName, IMessageBus messageBus,
        Func<Task<RunSummary>> run)
    {
        try { return await SingleThreadPump.RunAsync(run, displayName); }
        catch (TimeoutException timeout)
        {
            var test = new XunitTest(testCase, displayName);
            messageBus.QueueMessage(new TestFailed(test, 0M, null, timeout));
            return new RunSummary { Total = 1, Failed = 1 };
        }
    }
}
