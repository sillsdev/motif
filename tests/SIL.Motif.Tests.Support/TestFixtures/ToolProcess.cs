using System.Diagnostics;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Runs an external tool such as <c>git</c> or <c>python</c> and captures both output streams.</summary>
/// <remarks>
/// No tool inherits the variables that retarget git (GIT_DIR, GIT_WORK_TREE, GIT_INDEX_FILE, GIT_COMMON_DIR,
/// GIT_OBJECT_DIRECTORY): they are removed from every child's environment, including any set on the start info, because
/// a script such as pwsh or sh runs git itself. A test that wants one passes it as the <c>environment</c> argument,
/// which is applied after the scrub.
/// The tool gets an empty standard input of its own instead of the test process's. On Windows every process
/// that inherits one pipe shares it, and while any of them has a read pending on it, git and python block at
/// startup when they inspect their standard input: a suite started with a pipe for its standard input hung
/// on the first test that ran either tool, pinned by
/// `AToolReadsAnEmptyStandardInputOfItsOwnRatherThanTheTestProcesss`. Both output streams are read together,
/// so a tool that fills one while the other is being read cannot stall either.
/// </remarks>
public static class ToolProcess
{
    // Variables that retarget git at another repository.
    public static readonly IReadOnlyList<string> RepositoryTargeting =
        ["GIT_DIR", "GIT_WORK_TREE", "GIT_INDEX_FILE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY"];

    /// <summary>Removes the repository-targeting git variables from <paramref name="start"/>'s environment.</summary>
    public static void ScrubRepositoryTargeting(ProcessStartInfo start)
    {
        ArgumentNullException.ThrowIfNull(start);
        foreach (var name in RepositoryTargeting) start.Environment.Remove(name);
    }

    /// <summary>Starts <paramref name="start"/>, reads both output streams to the end, and waits for it to exit.</summary>
    public static ToolProcessResult Run(ProcessStartInfo start) => Run(start, null);

    /// <summary>As <see cref="Run(ProcessStartInfo)"/>, then applies <paramref name="environment"/> (a null value removes the variable).</summary>
    public static ToolProcessResult Run(ProcessStartInfo start, IReadOnlyDictionary<string, string?>? environment)
    {
        ArgumentNullException.ThrowIfNull(start);
        ScrubRepositoryTargeting(start);
        if (environment is not null) foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
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
