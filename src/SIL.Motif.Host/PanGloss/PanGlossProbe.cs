using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using SIL.Motif.Contract.Jobs;

namespace SIL.Motif.Host.PanGloss;

internal static class PanGlossProbe
{
    internal static async Task<PanGlossOutcome> RunAsync(string executable, string argument,
        PanGlossContainmentJob containment, TimeSpan cap, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        PanGlossProcessEnvironment.Configure(startInfo);
        startInfo.ArgumentList.Add(argument);
        PanGlossChildProcess? process;
        try { process = containment.Start(startInfo); }
        catch (Exception exception) when (
            exception is Win32Exception or InvalidOperationException or ArgumentException or UnauthorizedAccessException)
        {
            return new PanGlossOutcome.Unavailable($"could not start {argument}: {exception.Message}");
        }
        if (process is null) return new PanGlossOutcome.Unavailable($"could not start {argument}.");
        using (process)
        {
            var elapsed = Stopwatch.StartNew();
            var output = process.ReadStandardOutputAsync();
            var error = process.ReadStandardErrorAsync();
            var outputDrained = Task.WhenAll(output, error);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(cap);
            try
            {
                await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
                await containment.WaitForTerminatedDescendantsAsync(process.Id, deadline.Token).ConfigureAwait(false);
                await process.WaitForContainmentAsync(deadline.Token).ConfigureAwait(false);
                await outputDrained.WaitAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    await containment.TerminateAndConfirmAsync(process, outputDrained).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is OperationCanceledException or Win32Exception or IOException)
                {
                    return new PanGlossOutcome.Incomplete(
                        "Parser termination could not be confirmed after requesting cancellation.", string.Empty)
                    { Cause = PanGlossIncompleteCause.Infrastructure };
                }
                cancellationToken.ThrowIfCancellationRequested();
                var seconds = cap.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);
                var unit = cap == TimeSpan.FromSeconds(1) ? "second" : "seconds";
                return new PanGlossOutcome.TimedOut(cap, $"{argument} did not finish within {seconds} {unit}.");
            }
            var standardOutput = await output.ConfigureAwait(false);
            var standardError = await error.ConfigureAwait(false);
            if (process.MemoryKill is { } memoryKill)
                return new PanGlossOutcome.MemoryLimit(memoryKill) { Containment = containment.Report };
            return process.ExitCode != 0 || process.TerminationSignal is not null
                ? new PanGlossOutcome.Refused(process.ExitCode, standardError, standardOutput,
                    $"{argument} exited {process.ExitCode}: {standardError.Trim()}")
                    { TerminationSignal = process.TerminationSignal, Containment = containment.Report }
                : new PanGlossOutcome.Completed(standardOutput, standardError, elapsed.Elapsed)
                    { Containment = containment.Report };
        }
    }

    internal static PanGlossOutcome? FatalFailure(PanGlossOutcome outcome) => outcome switch
    {
        PanGlossOutcome.MemoryLimit => outcome,
        PanGlossOutcome.Incomplete { Cause: PanGlossIncompleteCause.Infrastructure } => outcome,
        PanGlossOutcome.Refused { FailureCategory: JobFailureCategory.ParserKilled or JobFailureCategory.OutOfMemory }
            => outcome,
        _ => null,
    };
}
