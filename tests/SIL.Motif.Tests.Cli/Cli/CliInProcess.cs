using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.Cli;

internal static class CliInProcess
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
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in start.Environment)
            environment[entry.Key] = entry.Value;

        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = global::SIL.Motif.Cli.Program.Run(
            arguments, output, error, environment, static (_, _) => { });
        return new CliProcessResult(exitCode, output.ToString(), error.ToString());
    }

    private static string[] SplitCommandLine(string commandLine)
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
