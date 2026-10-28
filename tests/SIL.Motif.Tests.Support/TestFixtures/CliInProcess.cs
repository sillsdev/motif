using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Runs <c>Program.Run</c> in the test process with the same isolated environment <see cref="CliProcess"/> would give a
/// child, so a test that asserts only stdout, stderr, exit code and files does not pay for a process start.
/// </summary>
public static class CliInProcess
{
    public static CliProcessResult RunCommandLine(
        string workerRoot, string? parserPath, bool developerCommands, string commandLine)
    {
        return Run(workerRoot, parserPath, developerCommands, SplitCommandLine(commandLine));
    }

    public static CliProcessResult Run(
        string workerRoot, string? parserPath, bool developerCommands, params string[] arguments)
    {
        var start = CliProcess.CreateStartInfo(workerRoot, parserPath, developerCommands);
        if (developerCommands) CliProcess.EnableAdvancedAiMode(start, workerRoot);
        var result = Run(start, arguments);
        return new CliProcessResult(result.ExitCode, result.Output, result.Error);
    }

    /// <summary>The in-process counterpart of <see cref="CliProcess.RunAsync(string, string?, bool, string[])"/>.</summary>
    public static Task<CliProcessResult> RunAsync(
        string workerRoot, string? parserPath, bool developerCommands, params string[] arguments)
    {
        var result = Run(CliProcess.CreateStartInfo(workerRoot, parserPath, developerCommands), arguments);
        return Task.FromResult(new CliProcessResult(result.ExitCode, result.Output, result.Error));
    }

    /// <summary>The in-process counterpart of <see cref="CliProcess.RunAsync(ProcessStartInfo)"/>.</summary>
    public static Task<(int ExitCode, string Output, string Error)> RunAsync(ProcessStartInfo start) =>
        Task.FromResult(Run(start, start.ArgumentList.ToArray()));

    /// <summary>Runs a command line with the environment of <paramref name="start"/>.</summary>
    public static (int ExitCode, string Output, string Error) RunCommandLine(ProcessStartInfo start, string commandLine) =>
        Run(start, SplitCommandLine(commandLine));

    public static (int ExitCode, string Output, string Error) Run(ProcessStartInfo start, params string[] arguments)
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in start.Environment)
            environment[entry.Key] = entry.Value;

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = global::SIL.Motif.Cli.Program.Run(
            arguments, output, error, environment, static (_, _) => { });
        return (exitCode, output.ToString(), error.ToString());
    }

    public static string[] SplitCommandLine(string commandLine)
    {
        var arguments = new List<string>();
        var token = new System.Text.StringBuilder();
        var quoted = false;
        var hasToken = false;

        foreach (var character in commandLine)
        {
            if (character == '"')
            {
                quoted = !quoted;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(character) && !quoted)
            {
                if (hasToken)
                {
                    arguments.Add(token.ToString());
                    token.Clear();
                    hasToken = false;
                }
            }
            else
            {
                token.Append(character);
                hasToken = true;
            }
        }

        if (quoted) throw new ArgumentException("A quoted CLI argument was not closed.", nameof(commandLine));
        if (hasToken) arguments.Add(token.ToString());
        return arguments.ToArray();
    }
}
