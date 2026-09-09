using System.Globalization;
using System.Text.Json;

namespace SIL.Motif.FakePanGloss;

/// <summary>
/// Stands in for the real <c>pangloss</c> executable at Motif's process boundary.
/// </summary>
/// <remarks>
/// <para>
/// The fake answers the three subcommands Motif sends the shipped binary — <c>batch</c>, <c>import</c>,
/// <c>stats</c> — plus their <c>describe</c> declaration, exercising the real process
/// boundary without a Rust build.
/// </para>
/// <para>
/// Behaviour is read from <c>_fake-pangloss.json</c> beside the grammar source rather than from the
/// environment. A test owns the candidate directory it exports into, so control travels with the
/// invocation instead of leaking through a variable that outlives it — which matters because xUnit runs
/// test classes in parallel and an environment variable is process-wide.
/// </para>
/// </remarks>
internal static class Program
{
    internal const string BehaviourFileName = "_fake-pangloss.json";

    /// <summary>
    /// Where the exact argv this invocation received is recorded, beside whatever this command's own
    /// behaviour file lives beside — the same convention, so a forwarding test can read back precisely
    /// what reached the process boundary.
    /// </summary>
    internal const string ArgvFileName = "_pangloss-argv.json";

    private sealed record Flag(string Name, bool TakesValue);

    private sealed record Command(string Name, string[] Positionals, Flag[] Flags, Func<string[], int> Run);

    private static readonly Command[] Dispatch =
    [
        new("batch", ["grammar", "words.txt", "out.tsv"],
            [new("--word-timeout-ms", true), new("--threads", true), new("--stats", false),
                new("--cache", true)], RunBatch),
        new("import", ["project.fwdata", "out.json"], [], RunImport),
        new("describe", [], [], RunDescription),
        new("stats", ["project-or-grammar"],
            [new("--cache", true), new("--group", true), new("--format", true)], RunStats),
    ];

    private static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: pangloss <batch|import|stats> ...");
            return 64;
        }
        var name = args[0] == "--describe" ? "describe" : args[0];
        var command = Dispatch.FirstOrDefault(command => command.Name == name);
        return command is null ? Unrecognised(args[0]) : command.Run(args);
    }

    private static int RunDescription(string[] args)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema_version = 1,
            binary = "pangloss",
            commands = Dispatch.Select(command => new
            {
                name = command.Name,
                summary = "Controlled test implementation of " + command.Name,
                hidden = false,
                positionals = command.Positionals,
                flags = command.Flags.Select(flag => new
                {
                    name = flag.Name,
                    takes_value = flag.TakesValue,
                    summary = "Test invocation option",
                }),
            }),
        }));
        return 0;
    }

    private static int Unrecognised(string command)
    {
        Console.Error.WriteLine($"usage: pangloss <batch|import|stats> ... (got '{command}')");
        return 64;
    }

    // batch <project> <words.txt> <out.tsv> [--word-timeout-ms N] [--threads N] [--stats] [--cache <path>]
    private static int RunBatch(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine(
                "usage: pangloss batch <grammar> <words.txt> <out.tsv> [--word-timeout-ms N] [--threads N] [--stats] [--cache <path>]");
            return 64;
        }
        var projectPath = args[1];
        var wordsPath = args[2];
        var outPath = args[3];
        string? cachePath = null;
        for (var i = 4; i < args.Length; i++)
        {
            if (args[i] == "--cache" && i + 1 < args.Length) cachePath = args[++i];
        }
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
        RecordArgv(directory, args);
        var behaviour = Behaviour.Read(directory);
        if (behaviour.HeartbeatPath is { } heartbeat) return Tick(heartbeat);
        if (behaviour.DelayMilliseconds > 0)
            Thread.Sleep(behaviour.DelayMilliseconds);
        switch (behaviour.Mode)
        {
            case "noReport":
                // Exits cleanly having written nothing: the caller must not read success from the code alone.
                return behaviour.ExitCode;
            case "fail":
                Console.Error.WriteLine(behaviour.StandardError ?? "the fake parser was told to fail");
                return behaviour.ExitCode == 0 ? 1 : behaviour.ExitCode;
            default:
                var words = File.Exists(wordsPath) ? File.ReadAllLines(wordsPath) : Array.Empty<string>();
                File.WriteAllText(outPath, BatchTsv(behaviour, words));
                // Motif digests the cache and never reads it, so any bytes stand in for PanGloss's SQLite.
                if (cachePath is not null) File.WriteAllText(cachePath, "fake stats cache");
                return behaviour.ExitCode;
        }
    }

    // idx\tword\tms\tstatus\tsignature — the row shape Motif's BatchTsvParser reads.
    private static string BatchTsv(Behaviour behaviour, IReadOnlyList<string> words)
    {
        var builder = new System.Text.StringBuilder();
        for (var i = 0; i < words.Count; i++)
        {
            var known = behaviour.Words.FirstOrDefault(w => w.Word == words[i]);
            var status = "ok";
            var signature = known is { Outcome: "complete" } ? words[i] + "-sig" : "-";
            builder.Append(i).Append('\t').Append(words[i]).Append('\t').Append(3).Append('\t')
                .Append(status).Append('\t').Append(signature).Append('\n');
        }
        return builder.ToString();
    }

    private static int RunImport(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("usage: pangloss import <fwDataPath> <grammarJsonPath>");
            return 64;
        }

        var fwDataPath = args[1];
        var grammarJsonPath = args[2];
        var directory = Path.GetDirectoryName(Path.GetFullPath(fwDataPath));
        RecordArgv(directory, args);
        var behaviour = Behaviour.Read(directory);

        if (behaviour.HeartbeatPath is { } heartbeat) return Tick(heartbeat);

        if (behaviour.DelayMilliseconds > 0)
            Thread.Sleep(behaviour.DelayMilliseconds);

        switch (behaviour.Mode)
        {
            case "noReport":
                // Exits cleanly having written nothing: the caller must not read success from the code alone.
                return behaviour.ExitCode;
            case "malformedReport":
                File.WriteAllText(grammarJsonPath, "{ this is not json");
                return behaviour.ExitCode;
            case "fail":
                Console.Error.WriteLine(behaviour.StandardError ?? "the fake parser was told to fail");
                return behaviour.ExitCode == 0 ? 1 : behaviour.ExitCode;
            default:
                File.WriteAllText(grammarJsonPath, GrammarJson(behaviour));
                return behaviour.ExitCode;
        }
    }

    private static int RunStats(string[] args)
    {
        if (args.Length < 4 || args[2] != "--cache")
        {
            Console.Error.WriteLine("usage: pangloss stats <grammarPath> --cache <cachePath> [-- <forwarded>...]");
            return 64;
        }

        var grammarPath = args[1];
        var forwarded = args[4..];
        var directory = Path.GetDirectoryName(Path.GetFullPath(grammarPath));
        RecordArgv(directory, args);
        var behaviour = Behaviour.Read(directory);

        if (behaviour.HeartbeatPath is { } heartbeat) return Tick(heartbeat);

        if (behaviour.DelayMilliseconds > 0)
            Thread.Sleep(behaviour.DelayMilliseconds);

        if (behaviour.Mode == "fail")
        {
            Console.Error.WriteLine(behaviour.StandardError ?? "the fake parser was told to fail");
            return behaviour.ExitCode == 0 ? 1 : behaviour.ExitCode;
        }

        // Only the fake reads its own argv for a format; the Motif seam that built it never does.
        var formatIndex = Array.IndexOf(forwarded, "--format");
        var jsonl = formatIndex >= 0 && formatIndex + 1 < forwarded.Length
            && forwarded[formatIndex + 1] == "jsonl";

        Console.Out.Write(jsonl ? StatsJsonl(behaviour) : StatsText(behaviour));
        return behaviour.ExitCode;
    }

    private static void RecordArgv(string? directory, string[] args)
    {
        if (directory is null) return;
        File.WriteAllText(Path.Combine(directory, ArgvFileName), JsonSerializer.Serialize(args));
    }

    private static string GrammarJson(Behaviour behaviour) =>
        JsonSerializer.Serialize(new
        {
            semanticDigest = behaviour.SemanticDigest,
            sourceSha256 = behaviour.SourceSha256,
            modelFingerprint = behaviour.ModelFingerprint,
            rules = Array.Empty<object>(),
        }, new JsonSerializerOptions { WriteIndented = true });

    private static string StatsText(Behaviour behaviour) =>
        "group    key             count" + Environment.NewLine +
        $"word     {behaviour.Words[0].Word,-15} 3" + Environment.NewLine +
        "word     beta            1" + Environment.NewLine;

    private static string StatsJsonl(Behaviour behaviour) =>
        JsonSerializer.Serialize(new { group = "word", key = behaviour.Words[0].Word, count = 3 }) +
        Environment.NewLine +
        JsonSerializer.Serialize(new { group = "word", key = "beta", count = 1 }) + Environment.NewLine;

    /// Ticks forever so a caller can prove that cancelling it actually stops the process.
    private static int Tick(string heartbeatPath)
    {
        for (var counter = 1; ; counter++)
        {
            File.WriteAllText(heartbeatPath, counter.ToString(CultureInfo.InvariantCulture));
            Thread.Sleep(50);
        }
    }

    private sealed record FakeWord(string Word, string Outcome);

    private sealed record Behaviour
    {
        public string Mode { get; init; } = "succeed";
        public int ExitCode { get; init; }
        public int DelayMilliseconds { get; init; }
        public string? HeartbeatPath { get; init; }
        public string? StandardError { get; init; }
        public string SemanticDigest { get; init; } = "sha256:" + new string('b', 64);
        public string SourceSha256 { get; init; } = "sha256:" + new string('c', 64);
        public string ModelFingerprint { get; init; } = "fp-1";
        public IReadOnlyList<FakeWord> Words { get; init; } = [new FakeWord("motifa", "complete")];

        internal static Behaviour Read(string? directory)
        {
            if (directory is null) return new Behaviour();
            var path = Path.Combine(directory, BehaviourFileName);
            if (!File.Exists(path)) return new Behaviour();
            return JsonSerializer.Deserialize<Behaviour>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Behaviour();
        }
    }
}
