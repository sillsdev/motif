using System.Diagnostics;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker;

namespace SIL.Motif.Tests.TestFixtures;

public static class CliProcess
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public static ProcessStartInfo CreateStartInfo(
        string workerRoot, string? parserPath, bool developerCommands, params string[] arguments)
    {
        var start = new ProcessStartInfo(BuildOutput.Cli)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment[RunnerOptions.RootVariable] = workerRoot;
        start.Environment[PanGlossExecutable.PathVariable] = parserPath ?? FakeParser.ExecutablePath;
        start.Environment.Remove(CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable);
        if (developerCommands)
            start.Environment[CommandSurfacePolicy.DeveloperCommandsEnvironmentVariable] = "1";
        start.Environment.Remove("FAKE_PANGLOSS_BEHAVIOUR_PATH");
        return start;
    }

    public static async Task<CliProcessResult> RunAsync(
        string workerRoot, string? parserPath, bool developerCommands, params string[] arguments)
    {
        using var process = Process.Start(CreateStartInfo(workerRoot, parserPath, developerCommands, arguments))!;
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync().WaitAsync(Timeout);
        }
        catch (TimeoutException exception)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }

            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException) { }
            var output = await outputTask;
            var error = await errorTask;
            throw new TimeoutException(
                $"The CLI did not exit within {Timeout}.{Environment.NewLine}" +
                $"Standard error:{Environment.NewLine}{error}{Environment.NewLine}" +
                $"Standard output:{Environment.NewLine}{output}", exception);
        }
        return new CliProcessResult(process.ExitCode, await outputTask, await errorTask);
    }
}

public sealed record CliProcessResult(int ExitCode, string Output, string Error)
{
    public string FailureDetails =>
        $"CLI exited {ExitCode}.{Environment.NewLine}Standard error:{Environment.NewLine}{Error}" +
        $"{Environment.NewLine}Standard output:{Environment.NewLine}{Output}";
}
