using System.Text.Json.Nodes;

namespace SIL.Motif.Tests.Cli;

/// <summary>One line of the call log a spawned CLI wrote: the command and the names of its arguments.</summary>
internal sealed record RecordedCall(string Command, IReadOnlyList<string> ArgumentShape);

internal static class CallLogReader
{
    /// <summary>Every call recorded under <paramref name="workerRoot"/>/calls, oldest first.</summary>
    public static IReadOnlyList<RecordedCall> ReadAll(string workerRoot)
    {
        var directory = Path.Combine(workerRoot, "calls");
        if (!Directory.Exists(directory)) return [];
        return Directory.GetFiles(directory, "calls-*.jsonl").Order().SelectMany(File.ReadAllLines)
            .Select(line => JsonNode.Parse(line)!)
            .Select(line => new RecordedCall((string)line["tool"]!,
                line["argKeys"]!.AsArray().Select(key => (string)key!).ToArray()))
            .ToArray();
    }
}
