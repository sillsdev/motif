using System.Diagnostics;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Runs an external tool such as <c>git</c> or <c>python</c> and captures both output streams.</summary>
/// <remarks>
/// The tool gets an empty standard input of its own instead of the test process's. On Windows every process
/// that inherits one pipe shares it, and while any of them has a read pending on it, git and python block at
/// startup when they inspect their standard input: a suite started with a pipe for its standard input hung
/// on the first test that ran either tool, pinned by
/// `AToolReadsAnEmptyStandardInputOfItsOwnRatherThanTheTestProcesss`. Both output streams are read together,
/// so a tool that fills one while the other is being read cannot stall either.
/// </remarks>
public static class ToolProcess
{
    /// <summary>Starts <paramref name="start"/>, reads both output streams to the end, and waits for it to exit.</summary>
    public static ToolProcessResult Run(ProcessStartInfo start)
    {
        ArgumentNullException.ThrowIfNull(start);
        start.UseShellExecute = false;
        start.RedirectStandardInput = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        start.CreateNoWindow = true;

        using var process = Process.Start(start)
            ?? throw new InvalidOperationException($"Could not start {start.FileName}.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new ToolProcessResult(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}

/// <summary>What <see cref="ToolProcess.Run"/> captured from one tool run.</summary>
public sealed record ToolProcessResult(int ExitCode, string Output, string Error);
