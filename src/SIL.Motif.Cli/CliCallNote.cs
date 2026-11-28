using System.Diagnostics;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Worker;

namespace SIL.Motif.Cli;

/// <summary>
/// What one CLI invocation tells the call log: the dispatcher names the command and its arguments once it has
/// parsed them and a renderer notes a refusal, and <see cref="Run"/> writes the line when the invocation ends,
/// whichever way it returns.
/// </summary>
internal sealed class CliCallNote
{
    private static readonly AsyncLocal<CliCallNote?> Active = new();

    private string? _command;
    private Dictionary<string, string> _arguments = [];
    private Refusal? _refusal;

    public static CliCallNote? Current => Active.Value;

    /// <summary>Names the command (the catalog name, or <c>help</c> or <c>unknown</c>) and what was passed to it.</summary>
    public void Name(string command, IReadOnlyDictionary<string, string> flags, IReadOnlyList<string> positionals)
    {
        _command = command;
        // An unknown command's own words, even its flag names, may be private; only the known ones are logged.
        if (command == "unknown")
        {
            _arguments = [];
            return;
        }
        _arguments = new Dictionary<string, string>(flags, StringComparer.Ordinal);
        for (var index = 0; index < positionals.Count; index++)
            _arguments["$" + index] = positionals[index];
    }

    /// <summary>Notes the refusal a command ended with, so the line carries its code and reason.</summary>
    public void Refused(Refusal? refusal) => _refusal = refusal;

    /// <summary>Runs <paramref name="invoke"/> and records the call, unless the log is off.</summary>
    public static int Run(IReadOnlyDictionary<string, string?> environment, string[] args, Func<int> invoke)
    {
        var calls = CallLog.FromEnvironment(name => environment.GetValueOrDefault(name),
            environment.TryGetValue(RunnerOptions.RootVariable, out var root) && !string.IsNullOrWhiteSpace(root)
                ? root : RunnerOptions.DefaultRoot);
        if (!calls.Enabled) return invoke();
        var note = new CliCallNote();
        Active.Value = note;
        var clock = Stopwatch.StartNew();
        var exit = 0;
        var unhandled = false;
        try
        {
            return exit = invoke();
        }
        catch
        {
            unhandled = true;
            throw;
        }
        finally
        {
            Active.Value = null;
            calls.Record(new CallRecord("cli", note._command ?? (args.Length > 0 ? "unknown" : "cli"), null, note._arguments,
                note._refusal?.Reason, note._refusal?.Code, unhandled, unhandled ? null : exit,
                clock.ElapsedMilliseconds, null, null));
        }
    }
}
