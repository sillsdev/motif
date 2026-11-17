using System.ComponentModel;
using SIL.Motif.Host.LcmUtils;
using Xunit;

namespace SIL.Motif.Tests.Host;

/// <summary>Covers the headless host's answers to LibLCM's interactive callbacks.</summary>
[Collection("Headless console")]
public sealed class HeadlessLcmUiTests
{
    [Fact]
    public void LibLcmWarningsLeaveStandardOutputAvailableForJson()
    {
        var originalOutput = Console.Out;
        var originalError = Console.Error;
        using var output = new StringWriter();
        using var error = new StringWriter();
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var ui = new HeadlessLcmUi(new NoopInvoker());
            ui.DisplayMessage(SIL.LCModel.MessageType.Warning, "parser warning", "warning", "");
            ui.ReportDuplicateGuids("duplicate warning");
            ui.DisplayCircularRefBreakerReport("circular warning", "warning");
            Assert.Equal("", output.ToString());
            Assert.Contains("parser warning", error.ToString());
            Assert.Contains("duplicate warning", error.ToString());
            Assert.Contains("circular warning", error.ToString());
        }
        finally
        {
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
        }
    }

    [Fact]
    public void AConflictingSaveFailsLoudlyRatherThanSilentlyDiscardingTheWork()
    {
        var ui = new HeadlessLcmUi(new NoopInvoker());

        var failure = Record.Exception(() => ui.ConflictingSave());

        // Answering it at all is the hazard: true means revert, and Save then returns as though it worked.
        Assert.IsType<NotSupportedException>(failure);
        Assert.Contains("conflict", failure!.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class NoopInvoker : ISynchronizeInvoke
    {
        public bool InvokeRequired => false;
        public IAsyncResult BeginInvoke(Delegate method, object?[]? args) => throw new NotSupportedException();
        public object EndInvoke(IAsyncResult result) => throw new NotSupportedException();
        public object Invoke(Delegate method, object?[]? args) => method.DynamicInvoke(args)!;
    }
}

[CollectionDefinition("Headless console", DisableParallelization = true)]
public sealed class HeadlessConsoleCollection;
