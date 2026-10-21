using System.Diagnostics;
using System.Globalization;

namespace SIL.Motif.Worker;

/// <summary>A queued job's pinned parser and the project that holds the job, for the runner that should run it.</summary>
internal sealed record ParserHandover(string ParserPath, string FwDataPath);

/// <summary>Starts a runner for the parser a retiring runner's leftover work is pinned to.</summary>
/// <remarks>
/// It builds the same arguments an enqueuer's launcher gives a runner, pinned by
/// `AYieldingRunnerHandsTheOldestPinnedJobToARunnerForItsParser`, and starts this runner again: its apphost, or
/// `dotnet <dll>` when it was started that way, pinned by `ARunnerStartedThroughTheDotnetHostIsRestartedThroughIt`.
/// </remarks>
internal static class ParserHandoverLauncher
{
    /// <summary>Replaces the process spawn. <b>Test-only.</b></summary>
    internal static Action<string, IReadOnlyList<string>>? SpawnOverride { get; set; }

    internal static IReadOnlyList<string> Arguments(RunnerOptions options, ParserHandover handover)
    {
        var arguments = new List<string>
        {
            RunnerOptions.RootArgument, options.Root, RunnerOptions.ParserArgument, handover.ParserPath,
        };
        if (options.OwnerNamespace is { } ownerNamespace)
            arguments.AddRange([RunnerOptions.NamespaceArgument, ownerNamespace]);
        arguments.AddRange([RunnerOptions.IdleArgument, Milliseconds(options.IdleTimeout),
            RunnerOptions.LeaseArgument, Milliseconds(options.Lease),
            RunnerOptions.WakeProjectArgument, handover.FwDataPath]);
        return arguments;
    }

    internal static void Start(RunnerOptions options, ParserHandover handover, Action<string> reportWarning)
    {
        var (executable, prefix) = Command(Environment.ProcessPath,
            System.Reflection.Assembly.GetEntryAssembly()?.Location);
        if (executable is null) return;
        var arguments = prefix.Concat(Arguments(options, handover)).ToList();
        try
        {
            if (SpawnOverride is { } spawn)
            {
                spawn(executable, arguments);
                return;
            }
            var start = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
            };
            start.Environment.Remove("ICU_DATA");
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or
            System.ComponentModel.Win32Exception)
        {
            reportWarning("warning: could not start the runner for the parser queued work is pinned to (" +
                exception.Message + "). That work stays queued until a runner for it starts.");
        }
    }

    /// <summary>What starts this runner again: its own executable, or the dotnet host and the runner's dll.</summary>
    internal static (string? Executable, IReadOnlyList<string> Prefix) Command(string? processPath,
        string? entryAssembly)
    {
        if (processPath is null) return (null, []);
        var host = Path.GetFileNameWithoutExtension(processPath);
        return string.Equals(host, "dotnet", StringComparison.OrdinalIgnoreCase) && entryAssembly is not null
            ? (processPath, [entryAssembly])
            : (processPath, []);
    }

    private static string Milliseconds(TimeSpan value) =>
        ((long)Math.Ceiling(value.TotalMilliseconds)).ToString(CultureInfo.InvariantCulture);
}
