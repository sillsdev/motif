using SIL.Motif.Mcp;
using SIL.Motif.Commands.Preferences;
using SIL.Motif.Worker;

namespace SIL.Motif.Cli;

/// <summary>
/// <c>motif mcp</c>: serves Motif to an AI agent over standard input and output. It is a server rather than a
/// catalogued command, so it has no JSON result and no help entry of its own; the protocol owns standard
/// output, and every diagnostic goes to standard error.
/// </summary>
internal static class McpCommand
{
    private const string Usage =
        "Usage: motif mcp --project <path-to-.fwdata | Known project name> [--profile <file | shipped name>] " +
        "[--activity-log <file.jsonl>]";

    public static int Run(string[] args, TextWriter error)
    {
        if (!FileAdvancedAiModePreferenceStore.ForInstallation().IsEnabled)
        {
            error.WriteLine(FileAdvancedAiModePreferenceStore.EnableInstruction);
            return 2;
        }

        string? project = null, profile = null, activity = null;
        for (var index = 0; index < args.Length; index++)
        {
            var flag = args[index];
            var value = index + 1 < args.Length ? args[index + 1] : null;
            switch (flag)
            {
                case "--project" when value is not null: project = value; index++; break;
                case "--profile" when value is not null: profile = value; index++; break;
                case "--activity-log" when value is not null: activity = value; index++; break;
                default:
                    error.WriteLine(Usage);
                    return 1;
            }
        }
        if (project is null)
        {
            error.WriteLine(Usage);
            return 1;
        }

        try
        {
            var resolved = ProjectResolver.Resolve(project, RunnerOptions.ResolveRoot());
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
            MotifMcpServer.RunAsync(new McpLaunchOptions(resolved, profile, activity, AdvancedAiModeEnabled: true),
                Console.OpenStandardInput(),
                Console.OpenStandardOutput(), error, cancellation.Token).GetAwaiter().GetResult();
            return 0;
        }
        catch (ProfileException exception)
        {
            error.WriteLine("error: " + exception.Message);
            return 1;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
    }
}
