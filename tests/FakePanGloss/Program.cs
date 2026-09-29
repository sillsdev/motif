using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
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

    internal const string EnvironmentFileName = "_pangloss-environment.json";

    /// <summary>Beside a copy of the fake, asks it to log every command it runs to <see cref="InvocationsFileName"/>.</summary>
    internal const string RecordInvocationsSentinel = "_fake-pangloss-record-invocations";

    /// <summary>One command name per line, for each invocation of a copy that carries the sentinel.</summary>
    internal const string InvocationsFileName = "_pangloss-invocations.log";

    private sealed record Flag(string Name, bool TakesValue);

    private sealed record Command(string Name, string[] Positionals, Flag[] Flags, Func<string[], int> Run);

    private static readonly Command[] Dispatch =
    [
        new("batch", ["grammar", "words.txt", "out.tsv"],
            [new("--word-timeout-ms", true), new("--step-cap", true), new("--threads", true), new("--stats", false),
                new("--cache", true), new("--analyses", true)], RunBatch),
        new("import", ["project.fwdata", "out.json"], [], RunImport),
        new("describe", [], [], RunDescription),
        new("stats", ["project-or-grammar"],
            [new("--cache", true), new("--group", true), new("--format", true)], RunStats),
        new("parse", ["grammar", "word"],
            [new("--trace", true), new("--trace-format", true), new("--trace-details", false)], RunParse),
        new("grammar-health", ["grammar", "out.json"], [new("--fw-project", true)], RunGrammarHealth),
    ];

    private static int Main(string[] args)
    {
        if (args is ["--allocate-memory", var requestedBytes])
            return ProbeMemoryLimit(requestedBytes);
        if (args is ["--allocate-memory", var delayedRequestedBytes, var holdMilliseconds])
            return ProbeMemoryLimit(delayedRequestedBytes, holdMilliseconds);
        // Dies from an unhandled exception on purpose: the suite proves no crash dialog holds such a process.
        if (args is ["--crash-unhandled"]) throw new InvalidOperationException("The fake parser was told to crash.");
        if (args.Length == 0)
        {
            Console.Error.WriteLine("usage: pangloss <batch|import|stats> ...");
            return 64;
        }
        var name = args[0] == "--describe" ? "describe" : args[0];
        var command = Dispatch.FirstOrDefault(command => command.Name == name);
        return command is null ? Unrecognised(args[0]) : command.Run(args);
    }

    private static int ProbeMemoryLimit(string requestedBytes, string? holdMilliseconds = null)
    {
        if (!int.TryParse(requestedBytes, NumberStyles.None, CultureInfo.InvariantCulture, out var length) || length <= 0)
            return 64;
        var delay = 0;
        if (holdMilliseconds is not null &&
            (!int.TryParse(holdMilliseconds, NumberStyles.None, CultureInfo.InvariantCulture, out delay) || delay < 0))
            return 64;
        try
        {
            var allocation = new byte[length];
            for (var index = 0; index < allocation.Length; index += 4096) allocation[index] = 1;
            if (holdMilliseconds is not null)
                Thread.Sleep(delay);
            GC.KeepAlive(allocation);
            return 0;
        }
        catch (OutOfMemoryException)
        {
            Console.Error.WriteLine("allocation-refused");
            return 73;
        }
    }

    private static int RunDescription(string[] args)
    {
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "_fake-pangloss-wrong-description")))
        {
            Console.WriteLine(JsonSerializer.Serialize(new { schema_version = 999, binary = "not-pangloss" }));
            return 0;
        }
        // These named sentinels isolate --describe failure modes from the default valid surface.
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "_fake-pangloss-describe-malformed")))
        {
            Console.WriteLine("not-json");
            return 0;
        }
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "_fake-pangloss-describe-fail")))
        {
            Console.Error.WriteLine("the fake --describe command was told to fail");
            return 17;
        }
        // This sentinel makes --describe exceed the caller's timeout without affecting normal commands.
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "_fake-pangloss-describe-hang")))
        {
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }

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
        string? analysesPath = null;
        for (var i = 4; i < args.Length; i++)
        {
            if (args[i] == "--cache" && i + 1 < args.Length) cachePath = args[++i];
            else if (args[i] == "--analyses" && i + 1 < args.Length) analysesPath = args[++i];
        }
        var directory = Path.GetDirectoryName(Path.GetFullPath(projectPath));
        RecordArgv(directory, args);
        var behaviour = Behaviour.Read(directory, "batch");
        if (behaviour.StartedPath is { } startedPath) File.WriteAllText(startedPath, string.Empty);
        if (behaviour.HoldUntilPath is { } holdUntilPath)
            while (!File.Exists(holdUntilPath)) Thread.Sleep(10);
        if (behaviour.HeartbeatPath is { } heartbeat)
        {
            using var wordsHandle = File.Open(wordsPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Tick(heartbeat, behaviour.ProcessIdPath);
        }
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
                if (behaviour.StreamProgress)
                {
                    var rows = BatchTsv(behaviour, words).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    using var stream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream) { AutoFlush = true };
                    for (var index = 0; index < words.Length; index++)
                    {
                        writer.WriteLine($"{index}\t{words[index]}\tSTARTED");
                        Thread.Sleep(behaviour.DelayMilliseconds);
                        writer.WriteLine(rows[index]);
                    }
                }
                else File.WriteAllText(outPath, BatchTsv(behaviour, words));
                if (analysesPath is not null) File.WriteAllText(analysesPath, BatchMorphology(behaviour, words));
                if (cachePath is not null)
                    StatsCache.Write(cachePath, words.Where(word => !string.IsNullOrWhiteSpace(word)).ToArray());
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
            var status = known?.Outcome switch
            {
                "capped" => "CAP",
                "timed-out" => "TIMEOUT",
                _ => "ok",
            };
            var signature = known?.Signature ?? (known is { Outcome: "complete" } ? words[i] + "-sig" : "-");
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
        // A published Baseline's layout allows no extra files, so importing from one records nothing beside it.
        RecordArgv(IsBaselinePublication(directory) ? null : directory, args);
        var behaviour = Behaviour.Read(directory, "import");

        if (behaviour.HeartbeatPath is { } heartbeat) return Tick(heartbeat, behaviour.ProcessIdPath);

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
        var behaviour = Behaviour.Read(directory, "stats");

        if (behaviour.HeartbeatPath is { } heartbeat) return Tick(heartbeat, behaviour.ProcessIdPath);

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
        var groupIndex = Array.IndexOf(forwarded, "--group");
        var wordGroup = groupIndex >= 0 && groupIndex + 1 < forwarded.Length && forwarded[groupIndex + 1] == "word";

        Console.Out.Write(wordGroup && jsonl ? StatsWordJsonl(behaviour) : jsonl ? StatsJsonl(behaviour) : StatsText(behaviour));
        return behaviour.ExitCode;
    }

    // parse <grammar> <word> --trace --trace-format json --trace-details
    private static int RunParse(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("usage: pangloss parse <grammar> <word> --trace --trace-format json --trace-details");
            return 64;
        }
        var grammarPath = args[1];
        var word = args[2];
        var directory = Path.GetDirectoryName(Path.GetFullPath(grammarPath));
        RecordArgv(directory, args);
        var behaviour = Behaviour.Read(directory, "parse");

        if (behaviour.HeartbeatPath is { } heartbeat) return Tick(heartbeat, behaviour.ProcessIdPath);

        if (behaviour.DelayMilliseconds > 0)
            Thread.Sleep(behaviour.DelayMilliseconds);

        if (behaviour.Mode == "fail")
        {
            Console.Error.WriteLine(behaviour.StandardError ?? "the fake parser was told to fail");
            return behaviour.ExitCode == 0 ? 1 : behaviour.ExitCode;
        }

        var signature = behaviour.TraceSignature ?? word + "-sig";
        // Raw UTF-8 bytes, as serde_json writes them: Console.Out would encode through the console code page.
        var envelope = TraceEnvelope(word, signature, behaviour.TraceJson, behaviour.TraceCapped,
            behaviour.TraceTimedOut);
        using (var stdout = Console.OpenStandardOutput())
            stdout.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(envelope));
        return behaviour.ExitCode;
    }

    private static readonly JsonSerializerOptions Unescaped =
        new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    // The pangloss.trace-details.v1 document, with the tree embedded verbatim so a malformed tree stays malformed.
    private static string TraceEnvelope(string word, string signature, string? treeJson, bool capped, bool timedOut) =>
        "{\"schemaVersion\":\"pangloss.trace-details.v1\",\"word\":" + JsonSerializer.Serialize(word, Unescaped) +
        ",\"search\":{\"completed\":" + (capped || timedOut ? "false" : "true") +
        ",\"capped\":" + (capped ? "true" : "false") +
        ",\"timedOut\":" + (timedOut ? "true" : "false") +
        ",\"invalidShape\":false,\"steps\":42,\"elapsedNs\":1500000}" +
        ",\"result\":{\"signature\":" + JsonSerializer.Serialize(signature) + ",\"guessed\":false,\"analyses\":[]}" +
        ",\"categories\":{\"morphRule\":{\"attempts\":3,\"work\":12,\"outputs\":2,\"notApplied\":1,\"noRoot\":0," +
        "\"surfaceMismatch\":0,\"uses\":1,\"timingAvailable\":true,\"selfElapsedNs\":48700}," +
        "\"phonRule\":{\"attempts\":0,\"work\":0,\"outputs\":0,\"notApplied\":0,\"noRoot\":0," +
        "\"surfaceMismatch\":0,\"uses\":0,\"timingAvailable\":false,\"selfElapsedNs\":null}}" +
        ",\"trace\":" + (treeJson ?? "null") + "}";

    // grammar-health <grammar> [<out.json>] [--fw-project <project>]
    private static int RunGrammarHealth(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: pangloss grammar-health <grammar> [<out.json>] [--fw-project <project>]");
            return 64;
        }
        var grammarPath = args[1];
        var outPath = args.Length > 2 ? args[2] : null;
        var directory = Path.GetDirectoryName(Path.GetFullPath(grammarPath));
        RecordArgv(directory, args);
        var behaviour = Behaviour.Read(directory, "grammar-health");

        if (behaviour.StartedPath is { } startedPath) File.WriteAllText(startedPath, string.Empty);
        if (behaviour.HoldUntilPath is { } holdUntilPath)
            while (!File.Exists(holdUntilPath)) Thread.Sleep(10);

        if (behaviour.HeartbeatPath is { } heartbeat) return Tick(heartbeat, behaviour.ProcessIdPath);
        if (behaviour.DelayMilliseconds > 0) Thread.Sleep(behaviour.DelayMilliseconds);

        if (behaviour.Mode == "fail")
        {
            Console.Error.WriteLine(behaviour.StandardError ?? "the fake parser was told to fail");
            return behaviour.ExitCode == 0 ? 1 : behaviour.ExitCode;
        }

        var isFwData = Path.GetExtension(grammarPath).Equals(".fwdata", StringComparison.OrdinalIgnoreCase);
        var projectArgumentIndex = Array.IndexOf(args, "--fw-project");
        var explicitProjectName = projectArgumentIndex >= 0 && projectArgumentIndex + 1 < args.Length
            ? args[projectArgumentIndex + 1]
            : null;
        var projectName = explicitProjectName ?? (isFwData ? Path.GetFileNameWithoutExtension(grammarPath) : null);
        var projectSource = explicitProjectName is not null ? "argument" : isFwData ? "fwdata_path" : null;
        var encodedProjectName = projectName is null ? null
            : Uri.EscapeDataString(projectName).Replace("%20", "+", StringComparison.Ordinal);
        var openGuid = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb";
        object fieldworks = encodedProjectName is null
            ? new { status = "unavailable", reason = "missing_project", guid = openGuid }
            : new
            {
                status = "available",
                guid = openGuid,
                tool = "phonemeEdit",
                url = $"silfw://localhost/link?database={encodedProjectName}&tool=phonemeEdit&guid={openGuid}&tag=",
            };
        var json = behaviour.GrammarHealthReportJson ?? JsonSerializer.Serialize(new
        {
            schema_version = 2,
            fieldworks_project = new { name = projectName, source = projectSource },
            summary = new[]
            {
                new { code = "fwdata.empty-representation", group_name = "Empty item representation", level = "warning", count = 1 },
                new { code = "fwdata.only-first-used", group_name = "Only first list item is used", level = "info", count = 1 },
                new { code = "hc-duplicate-feature-bundle", group_name = "Duplicate segment features", level = "warning", count = 1 },
            },
            diagnostics = new object[]
            {
                new
                {
                    level = "warning",
                    code = "fwdata.empty-representation",
                    group_name = "Empty item representation",
                    origin = "import",
                    description = "A boundary marker has no representation.",
                    guidance = "Add a representation to the named boundary marker.",
                    subjects = new object[]
                    {
                        new
                        {
                            kind = "PhBdryMarker",
                            title = "+",
                            subtitle = (string?)null,
                            guid = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
                            internal_id = (string?)null,
                            opens_in = new { tool = "phonemeEdit", guid = openGuid },
                            fieldworks,
                        },
                    },
                },
                new
                {
                    level = "info",
                    code = "fwdata.only-first-used",
                    group_name = "Only first list item is used",
                    origin = "import",
                    description = "Only the first phoneme set is loaded.",
                    guidance = "Check the project's phoneme sets.",
                    subjects = Array.Empty<object>(),
                },
                new
                {
                    level = "warning",
                    code = "hc-duplicate-feature-bundle",
                    group_name = "Duplicate segment features",
                    origin = "check",
                    description = "Phonemes /n/ and /s/ share the same feature values.",
                    guidance = "Assign distinct feature values to these phonemes.",
                    subjects = new object[]
                    {
                        new
                        {
                            kind = "PhPhonemeSet",
                            title = "Main phoneme set",
                            subtitle = (string?)null,
                            guid = (string?)null,
                            internal_id = "table#0:table1",
                            fieldworks = new { status = "unavailable", reason = "guid_not_recorded", guid = (string?)null },
                        },
                    },
                },
            },
        });
        if (outPath is not null) File.WriteAllText(outPath, json);
        else Console.Out.Write(json);
        Console.Error.WriteLine("grammar-health complete: 2 warning(s), 1 info");
        return behaviour.ExitCode;
    }

    // Publications are folders named by their bundle's 64-hex-digit SHA-256 digest.
    private static bool IsBaselinePublication(string? directory) =>
        directory is not null && Path.GetFileName(directory) is { Length: 64 } name && name.All(char.IsAsciiHexDigit);

    private static void RecordArgv(string? directory, string[] args)
    {
        // Only a copy carrying the sentinel logs, so the shared fake never accumulates a record.
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, RecordInvocationsSentinel)))
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, InvocationsFileName), args[0] + "\n");
        var serialized = JsonSerializer.Serialize(args);
        if (directory is null) return;
        File.WriteAllText(Path.Combine(directory, ArgvFileName), serialized);
        File.WriteAllText(Path.Combine(directory, EnvironmentFileName), JsonSerializer.Serialize(
            Environment.GetEnvironmentVariables().Keys.Cast<string>().OrderBy(name => name)));
        if (Path.GetFileName(directory).StartsWith("motif-stats-replay-", StringComparison.Ordinal))
            File.WriteAllText(Path.Combine(Path.GetTempPath(), ArgvFileName), serialized);
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

    // Mirrors the real `stats --group word --format jsonl` shape: one meta line, then one row per word.
    private static string StatsWordJsonl(Behaviour behaviour)
    {
        var lines = new List<string>
        {
            JsonSerializer.Serialize(new { meta = true, orientation = "word" }),
        };
        foreach (var word in behaviour.Words)
        {
            lines.Add(JsonSerializer.Serialize(new
            {
                form = word.Word,
                elapsed_ns = 3_000_000,
                attempts = word.Attempts ?? 0,
                passes = word.Passes ?? 0,
                capped = word.Outcome == "capped",
                timed_out = word.Outcome == "timed-out",
            }));
        }
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    /// Ticks forever so a caller can prove that cancelling it actually stops the process.
    private static int Tick(string heartbeatPath, string? processIdPath)
    {
        if (processIdPath is not null)
            File.WriteAllText(processIdPath, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        for (var counter = 1; ; counter++)
        {
            File.WriteAllText(heartbeatPath, counter.ToString(CultureInfo.InvariantCulture));
            Thread.Sleep(50);
        }
    }

    private static string BatchMorphology(Behaviour behaviour, IReadOnlyList<string> words) =>
        string.Join("\n", words.Select((word, index) =>
        {
            var known = behaviour.Words.FirstOrDefault(item => item.Word == word);
            var hasFindings = known?.Signature is { } signature ? signature != "-" : known?.Outcome == "complete";
            return JsonSerializer.Serialize(new
            {
                schema = "fieldworks-parse-analysis/v1", index, word, elapsedMs = 3,
                capped = known?.Outcome == "capped", timedOut = known?.Outcome == "timed-out", invalidShape = false,
                analyses = known?.Analyses ?? [],
                unavailable = hasFindings && known?.Analyses is not { Count: > 0 }
                    ? new[] { "Fake result has no authored source identity." } : Array.Empty<string>(),
            });
        }));

    private sealed record FakeWord(string Word, string Outcome, string? Signature = null,
        IReadOnlyList<JsonElement>? Analyses = null, int? Attempts = null, int? Passes = null);

    private sealed record Behaviour
    {
        public string Mode { get; init; } = "succeed";
        public int ExitCode { get; init; }
        public int DelayMilliseconds { get; init; }
        public bool StreamProgress { get; init; }
        public string? HeartbeatPath { get; init; }
        public string? StartedPath { get; init; }
        public string? HoldUntilPath { get; init; }
        public string? ProcessIdPath { get; init; }
        public string? StandardError { get; init; }
        public string SemanticDigest { get; init; } = "sha256:" + new string('b', 64);
        public string SourceSha256 { get; init; } = "sha256:" + new string('c', 64);
        public string ModelFingerprint { get; init; } = "fp-1";
        public IReadOnlyList<FakeWord> Words { get; init; } = [new FakeWord("motifa", "complete")];
        public string? TraceSignature { get; init; }
        public string? TraceJson { get; init; }
        public bool TraceCapped { get; init; }
        public bool TraceTimedOut { get; init; }
        public string? GrammarHealthReportJson { get; init; }

        internal static Behaviour Read(string? directory, string? subcommand = null)
        {
            if (directory is null) return new Behaviour();
            var besideGrammar = Path.Combine(directory, BehaviourFileName);
            // A copy of the fake can carry its own behaviour, for a candidate exported where no test can reach.
            var path = Environment.GetEnvironmentVariable("FAKE_PANGLOSS_BEHAVIOUR_PATH")
                ?? (File.Exists(besideGrammar) ? besideGrammar : Path.Combine(AppContext.BaseDirectory, BehaviourFileName));
            if (!File.Exists(path)) return new Behaviour();
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (subcommand is not null && root.TryGetProperty("subcommands", out var bySubcommand) &&
                bySubcommand.ValueKind == JsonValueKind.Object &&
                bySubcommand.EnumerateObject().FirstOrDefault(property =>
                    string.Equals(property.Name, subcommand, StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: JsonValueKind.Object } selected)
            {
                var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in root.EnumerateObject())
                    if (!string.Equals(property.Name, "subcommands", StringComparison.OrdinalIgnoreCase))
                        values[property.Name] = property.Value;
                foreach (var property in selected.Value.EnumerateObject()) values[property.Name] = property.Value;
                return JsonSerializer.Deserialize<Behaviour>(JsonSerializer.Serialize(values),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Behaviour();
            }
            return JsonSerializer.Deserialize<Behaviour>(root.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Behaviour();
        }
    }
}
