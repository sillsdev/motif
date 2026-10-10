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
/// The fake answers each subcommand used by Motif, including <c>facts</c>, and its <c>describe</c>
/// declaration, exercising the real process
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

    private const string DescribeOmissionsFileName = "_fake-pangloss-describe-omissions.json";
    private const string AdditionalStatsFlagsFileName = "_fake-pangloss-additional-stats-flags.json";

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
        new("facts", ["snapshot.json"],
            [new("--out", true), new("--context", true), new("--stats", true),
                new("--stats-manifest", true), new("--json", false)], RunFacts),
        new("import", ["project.fwdata", "out.json"], [], RunImport),
        new("describe", [], [], RunDescription),
        new("stats", ["project-or-grammar"],
            [new("--cache", true), new("--group", true), new("--format", true)], RunStats),
        new("parse", ["grammar", "word"],
            [new("--trace", true), new("--trace-format", true), new("--trace-details", false),
                new("--step-cap", true)], RunParse),
        new("grammar-health", ["grammar", "out.json?"], [new("--fw-project", true)], RunGrammarHealth),
    ];

    private static int Main(string[] args)
    {
        // Emulate a legacy console in a private fake copy so encoding regressions reproduce on every OS.
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "_fake-pangloss-legacy-console")))
            Console.OutputEncoding = Encoding.ASCII;
        // Match PanGloss's UTF-8 stdout and stderr even when Windows supplies a legacy console code page.
        Console.OutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        if (args is ["--probe-stream-encoding", var text])
        {
            Console.Out.Write(text);
            Console.Error.Write(text);
            return 0;
        }
        if (args is ["--version"])
        {
            Console.WriteLine("pangloss 0.8.2");
            return 0;
        }
        if (args is ["--allocate-memory", var requestedBytes])
            return ProbeMemoryLimit(requestedBytes);
        if (args is ["--allocate-memory", var delayedRequestedBytes, var holdMilliseconds])
            return ProbeMemoryLimit(delayedRequestedBytes, holdMilliseconds);
        if (args is ["--reserve-thread-stacks", var threads, var stackBytes])
            return ReserveThreadStacks(threads, stackBytes);
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

    // Runs every thread at once on a stack it reserves and barely touches, as PanGloss's parser threads do.
    private static int ReserveThreadStacks(string requestedThreads, string requestedStackBytes)
    {
        if (!int.TryParse(requestedThreads, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
            count <= 0 ||
            !int.TryParse(requestedStackBytes, NumberStyles.None, CultureInfo.InvariantCulture, out var stackBytes) ||
            stackBytes <= 0)
            return 64;
        using var allStarted = new Barrier(count + 1);
        var threads = new List<Thread>();
        try
        {
            for (var index = 0; index < count; index++)
            {
                var thread = new Thread(() => allStarted.SignalAndWait(), stackBytes) { IsBackground = true };
                thread.Start();
                threads.Add(thread);
            }
        }
        catch (Exception exception) when (exception is OutOfMemoryException or ThreadStartException)
        {
            Console.Error.WriteLine($"thread-start-refused after {threads.Count}: {exception.Message}");
            return 73;
        }
        allStarted.SignalAndWait();
        foreach (var thread in threads) thread.Join();
        Console.WriteLine($"threads-started {threads.Count}");
        return 0;
    }

    private static int RunDescription(string[] args)
    {
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, RecordInvocationsSentinel)))
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, InvocationsFileName), "describe\n");
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
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "_fake-pangloss-describe-started"), string.Empty);
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }

        var omissionsPath = Path.Combine(AppContext.BaseDirectory, DescribeOmissionsFileName);
        var omissions = File.Exists(omissionsPath)
            ? (JsonSerializer.Deserialize<string[]>(File.ReadAllText(omissionsPath)) ?? []).ToHashSet(StringComparer.Ordinal)
            : new HashSet<string>(StringComparer.Ordinal);
        var additionalStatsFlagsPath = Path.Combine(AppContext.BaseDirectory, AdditionalStatsFlagsFileName);
        var additionalStatsFlags = File.Exists(additionalStatsFlagsPath)
            ? JsonSerializer.Deserialize<Flag[]>(File.ReadAllText(additionalStatsFlagsPath)) ?? []
            : [];

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            schema_version = 1,
            binary = "pangloss",
            facts = new
            {
                format = FactsArtifact.Format,
                schemaVersion = FactsArtifact.SchemaVersion,
                applicationId = FactsArtifact.ApplicationId,
                contextFormat = FactsArtifact.ContextFormat,
                contextVersion = FactsArtifact.ContextVersion,
                statsManifestVersion = 1,
            },
            commands = Dispatch.Where(command => !omissions.Contains(command.Name)).Select(command => new
            {
                name = command.Name,
                summary = "Controlled test implementation of " + command.Name,
                hidden = false,
                positionals = command.Positionals,
                flags = (command.Name == "stats" ? command.Flags.Concat(additionalStatsFlags) : command.Flags)
                    .Where(flag => !omissions.Contains(command.Name + " " + flag.Name)).Select(flag => new
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
        var words = File.Exists(wordsPath) ? File.ReadAllLines(wordsPath) : Array.Empty<string>();
        if (behaviour.StartedPath is { } startedPath) File.WriteAllText(startedPath, string.Empty);
        if (behaviour.ProcessIdPath is { } processIdPath)
            File.WriteAllText(processIdPath, Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
        if (behaviour.HoldUntilPath is { } holdUntilPath)
        {
            using var stream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream) { AutoFlush = true };
            if (words.Length > 0) writer.WriteLine($"0\t{words[0]}\tSTARTED");
            var holdExit = WaitForHoldRelease(holdUntilPath, behaviour.HoldTimeoutMs);
            if (holdExit != 0) return holdExit;
        }
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
            case "crash":
                throw new InvalidOperationException("The fake parser was told to crash during batch analysis.");
            case "malformedReport":
                File.WriteAllText(outPath, BatchTsv(behaviour, words));
                if (analysesPath is { } malformedPath)
                    File.WriteAllText(malformedPath,
                        "{\"schema\":\"fieldworks-parse-analysis/v1\",\"index\":0,\"word\":\"motifa\",\"elapsedMs\":3,\"capped\":false,\"timedOut\":false,\"invalidShape\":false,\"analyses\":[{\"morphs\":[]}],\"unavailable\":[]}\n");
                return behaviour.ExitCode;
            case "fail":
                Console.Error.WriteLine(behaviour.StandardError ?? "the fake parser was told to fail");
                return behaviour.ExitCode == 0 ? 1 : behaviour.ExitCode;
            default:
                if (behaviour.StreamProgress)
                {
                    var rows = BatchTsv(behaviour, words).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                    using var stream = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.ReadWrite);
                    using var writer = new StreamWriter(stream) { AutoFlush = true };
                    for (var index = 0; index < words.Length; index++)
                    {
                        writer.WriteLine($"{index}\t{words[index]}\tSTARTED");
                        if (behaviour.HoldEachWordUntil is { } wordRelease)
                        {
                            var wordExit = WaitForHoldRelease(wordRelease + "." + index, behaviour.HoldTimeoutMs);
                            if (wordExit != 0) return wordExit;
                        }
                        else Thread.Sleep(behaviour.DelayMilliseconds);
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
            builder.Append(i).Append('\t').Append(words[i]).Append('\t')
                .Append((known?.ElapsedMs ?? 3).ToString(CultureInfo.InvariantCulture)).Append('\t')
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
                var factsFixturePath = Path.Combine(AppContext.BaseDirectory, "_fake-pangloss-facts.json");
                File.WriteAllText(grammarJsonPath, File.Exists(factsFixturePath)
                    ? FactsSnapshotJson()
                    : GrammarJson(behaviour));
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

        // The fake alone interprets forwarded filters to build the predictable stats output.
        var formatIndex = Array.IndexOf(forwarded, "--format");
        var jsonl = (formatIndex >= 0 && formatIndex + 1 < forwarded.Length
            && forwarded[formatIndex + 1] == "jsonl") || Array.IndexOf(forwarded, "--format=jsonl") >= 0;
        var groupIndex = Array.IndexOf(forwarded, "--group");
        var wordGroup = (groupIndex >= 0 && groupIndex + 1 < forwarded.Length
            && forwarded[groupIndex + 1] == "word") || Array.IndexOf(forwarded, "--group=word") >= 0;

        Console.Out.Write(wordGroup && jsonl ? StatsWordJsonl(behaviour) : jsonl ? StatsJsonl(behaviour) : StatsText(behaviour));
        return behaviour.ExitCode;
    }

    private static int RunFacts(string[] args)
    {
        if (args.Length < 2)
            return FailFacts("invalid_context", "usage: facts <snapshot.json> --out <facts.sqlite> --context <context.json> [--json]");

        var snapshotPath = args[1];
        var directory = Path.GetDirectoryName(Path.GetFullPath(snapshotPath));
        RecordArgv(directory, args);
        var outputPath = default(string);
        var contextPath = default(string);
        var statsPath = default(string);
        var statsManifestPath = default(string);
        var json = false;
        for (var index = 2; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--out":
                    if (++index == args.Length) return FailFacts("invalid_context", "--out requires a path");
                    outputPath = args[index];
                    break;
                case "--context":
                    if (++index == args.Length) return FailFacts("invalid_context", "--context requires a path");
                    contextPath = args[index];
                    break;
                case "--stats":
                    if (++index == args.Length) return FailFacts("invalid_context", "--stats requires a path");
                    statsPath = args[index];
                    break;
                case "--stats-manifest":
                    if (++index == args.Length) return FailFacts("invalid_context", "--stats-manifest requires a path");
                    statsManifestPath = args[index];
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    return FailFacts("invalid_context", $"unknown facts argument {args[index]}");
            }
        }
        if (outputPath is null) return FailFacts("invalid_context", "--out is required");
        if (contextPath is null) return FailFacts("invalid_context", "--context is required");
        if ((statsPath is null) != (statsManifestPath is null))
            return FailFacts("invalid_context", "--stats and --stats-manifest must be supplied together");
        if (Path.GetExtension(snapshotPath) != ".json")
            return FailFacts("unsupported_facts_source", "facts accepts only current pg-snapshot JSON input");
        if (SamePath(snapshotPath, outputPath) || SamePath(contextPath, outputPath))
            return FailFacts("input_output_collision", "the output path resolves to an input file");
        if (File.Exists(outputPath)) return FailFacts("output_exists", "the output path already exists");

        var behaviour = Behaviour.Read(directory, "facts");
        if (behaviour.HeartbeatPath is { } heartbeat) return Tick(heartbeat, behaviour.ProcessIdPath);
        if (behaviour.DelayMilliseconds > 0) Thread.Sleep(behaviour.DelayMilliseconds);
        if (behaviour.Mode == "noReport") return behaviour.ExitCode;
        if (behaviour.Mode == "fail")
            return FailFacts("facts_write_failed", behaviour.StandardError ?? "the fake facts command was told to fail");

        try
        {
            var result = FactsArtifact.Write(snapshotPath, contextPath, outputPath, behaviour.Mode,
                Path.Combine(AppContext.BaseDirectory, "_fake-pangloss-facts.json"));
            if (json && behaviour.Mode == "missingApplicationId")
            {
                var document = JsonSerializer.SerializeToNode(result)!.AsObject();
                document.Remove("applicationId");
                Console.WriteLine(document.ToJsonString());
            }
            else if (json) Console.WriteLine(JsonSerializer.Serialize(result));
            else
                Console.WriteLine($"wrote {outputPath} ({result.outputBytes} bytes, {result.outputSha256}, " +
                    $"schema {result.schemaVersion}, compile {result.compileStatus})");
            if (result.compileStatus == "refused")
            {
                Console.Error.WriteLine(
                    "pangloss facts: compile_refused: authored and diagnostic facts were published; effective grammar is unavailable");
                return behaviour.ExitCode == 0 ? 1 : behaviour.ExitCode;
            }
            return behaviour.ExitCode;
        }
        catch (FactsArtifactException exception)
        {
            return FailFacts(exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          JsonException or Microsoft.Data.Sqlite.SqliteException)
        {
            return FailFacts("facts_write_failed", exception.Message);
        }
    }

    private static bool SamePath(string input, string output) =>
        string.Equals(Path.GetFullPath(input), Path.GetFullPath(output),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static int FailFacts(string code, string message)
    {
        Console.Error.WriteLine($"pangloss facts: {code}: {message}");
        return 1;
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
        // Emit the trace envelope as bytes, matching serde_json's unescaped UTF-8 output.
        var envelope = TraceEnvelope(word, signature, behaviour.TraceJson, behaviour.TraceCapped,
            behaviour.TraceTimedOut, behaviour.TraceAnalyses);
        using (var stdout = Console.OpenStandardOutput())
            stdout.Write(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(envelope));
        return behaviour.ExitCode;
    }

    private static readonly JsonSerializerOptions Unescaped =
        new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static int WaitForHoldRelease(string releasePath, int timeoutMs)
    {
        var deadline = Environment.TickCount64 + Math.Max(1, timeoutMs);
        while (!File.Exists(releasePath) && Environment.TickCount64 < deadline) Thread.Sleep(10);
        if (File.Exists(releasePath)) return 0;
        Console.Error.WriteLine("fake parser hold timed out waiting for release");
        return 86;
    }

    // The embedded tree stays verbatim so malformed-tree tests exercise the consumer boundary.
    private static string TraceEnvelope(string word, string signature, string? treeJson, bool capped, bool timedOut,
        IReadOnlyList<JsonElement>? analyses) =>
        "{\"schemaVersion\":\"pangloss.trace-details.v3\",\"word\":" + JsonSerializer.Serialize(word, Unescaped) +
        ",\"search\":{\"completed\":" + (capped || timedOut ? "false" : "true") +
        ",\"capped\":" + (capped ? "true" : "false") +
        ",\"timedOut\":" + (timedOut ? "true" : "false") +
        ",\"invalidShape\":false,\"steps\":42,\"elapsedNs\":1500000}" +
        ",\"result\":{\"signature\":" + JsonSerializer.Serialize(signature) + ",\"guessed\":false,\"analyses\":" +
        JsonSerializer.Serialize(analyses ?? Array.Empty<JsonElement>(), Unescaped) + "}" +
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
        var outPath = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : null;
        var directory = Path.GetDirectoryName(Path.GetFullPath(grammarPath));
        RecordArgv(directory, args);
        var behaviour = Behaviour.Read(directory, "grammar-health");

        if (behaviour.StartedPath is { } startedPath) File.WriteAllText(startedPath, string.Empty);
        if (behaviour.HoldUntilPath is { } holdUntilPath)
        {
            var holdExit = WaitForHoldRelease(holdUntilPath, behaviour.HoldTimeoutMs);
            if (holdExit != 0) return holdExit;
        }

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
            schema_version = 4, locale = "en",
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
                    title = "Empty item representation", explanation = "A boundary marker has no representation.", help_path = (string?)null, help_body = (string?)null,
                    fieldworks_places = Array.Empty<object>(), scope = "object",
                    subjects = new object[]
                    {
                        new
                        {
                            kind = "PhBdryMarker", status = "object", field = (string?)null, source_class = (string?)null,
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
                    title = "Only first list item is used", explanation = "Only the first phoneme set is loaded.", help_path = (string?)null, help_body = (string?)null,
                    fieldworks_places = Array.Empty<object>(), scope = "project_settings",
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
                    title = "Duplicate segment features", explanation = "Phonemes /n/ and /s/ share the same feature values.", help_path = (string?)null, help_body = (string?)null,
                    fieldworks_places = Array.Empty<object>(), scope = "object",
                    subjects = new object[]
                    {
                        new
                        {
                            kind = "PhPhonemeSet", status = "object", field = (string?)null, source_class = (string?)null,
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
        // A published Baseline's layout allows no extra files, so no command records anything beside one.
        if (directory is null || IsBaselinePublication(directory)) return;
        File.WriteAllText(Path.Combine(directory, ArgvFileName), serialized);
        File.WriteAllText(Path.Combine(directory, EnvironmentFileName), JsonSerializer.Serialize(
            Environment.GetEnvironmentVariables().Keys.Cast<string>().OrderBy(name => name)));
        if (Path.GetFileName(directory).StartsWith("motif-stats-replay-", StringComparison.Ordinal))
            WriteSharedTempArgv(serialized);
    }

    // A shared temp directory is written by every suite's fakes at once; only a private TEMP ever reads it back.
    private static void WriteSharedTempArgv(string serialized)
    {
        try { File.WriteAllText(Path.Combine(Path.GetTempPath(), ArgvFileName), serialized); }
        catch (IOException) { }
    }

    private static string GrammarJson(Behaviour behaviour) =>
        JsonSerializer.Serialize(new
        {
            semanticDigest = behaviour.SemanticDigest,
            sourceSha256 = behaviour.SourceSha256,
            modelFingerprint = behaviour.ModelFingerprint,
            rules = Array.Empty<object>(),
        }, new JsonSerializerOptions { WriteIndented = true });

    private static string FactsSnapshotJson() => JsonSerializer.Serialize(new
    {
        format = "pangloss-project",
        version = 1,
        project = new { name = "Fake project" },
        conversionProvenance = new { schemaVersion = 1, sourceInventoryStatus = "synthetic" },
    });

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
                schema = "fieldworks-parse-analysis/v1", index, word, elapsedMs = known?.ElapsedMs ?? 3,
                capped = known?.Outcome == "capped", timedOut = known?.Outcome == "timed-out", invalidShape = false,
                analyses = known?.Analyses ?? [],
                unavailable = hasFindings && known?.Analyses is not { Count: > 0 }
                    ? new[] { "Fake result has no authored source identity." } : Array.Empty<string>(),
            });
        }));

    private sealed record FakeWord(string Word, string Outcome, string? Signature = null,
        IReadOnlyList<JsonElement>? Analyses = null, int? Attempts = null, int? Passes = null, double ElapsedMs = 3);

    private sealed record Behaviour
    {
        public string Mode { get; init; } = "succeed";
        public int ExitCode { get; init; }
        public int DelayMilliseconds { get; init; }
        public bool StreamProgress { get; init; }
        public string? HeartbeatPath { get; init; }
        public string? StartedPath { get; init; }
        public string? HoldUntilPath { get; init; }
        // A streamed batch stays on word i until a file named this path plus ".i" exists.
        public string? HoldEachWordUntil { get; init; }
        public string? ProcessIdPath { get; init; }
        public string? StandardError { get; init; }
        public string SemanticDigest { get; init; } = "sha256:" + new string('b', 64);
        public string SourceSha256 { get; init; } = "sha256:" + new string('c', 64);
        public string ModelFingerprint { get; init; } = "fp-1";
        public IReadOnlyList<FakeWord> Words { get; init; } = [new FakeWord("motifa", "complete")];
        public string? TraceSignature { get; init; }
        public string? TraceJson { get; init; }
        public IReadOnlyList<JsonElement>? TraceAnalyses { get; init; }
        public bool TraceCapped { get; init; }
        public bool TraceTimedOut { get; init; }
        public int HoldTimeoutMs { get; init; } = 60_000;
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
            var values = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in root.EnumerateObject())
                if (property.Name is not ("subcommands" or "phases")) values[property.Name] = property.Value;
            if (subcommand is not null && root.TryGetProperty("subcommands", out var bySubcommand) &&
                bySubcommand.ValueKind == JsonValueKind.Object &&
                bySubcommand.EnumerateObject().FirstOrDefault(property =>
                    string.Equals(property.Name, subcommand, StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: JsonValueKind.Object } selected)
            {
                foreach (var property in selected.Value.EnumerateObject()) values[property.Name] = property.Value;
            }
            if (subcommand is not null && root.TryGetProperty("phases", out var byPhase) &&
                byPhase.ValueKind == JsonValueKind.Object &&
                byPhase.EnumerateObject().FirstOrDefault(property =>
                    string.Equals(property.Name, subcommand, StringComparison.OrdinalIgnoreCase)) is { Value.ValueKind: JsonValueKind.Array } sequence)
            {
                if (sequence.Value.GetArrayLength() == 0)
                    throw new InvalidDataException($"Fake parser phase sequence for '{subcommand}' is empty.");
                var countPath = Path.Combine(AppContext.BaseDirectory, $"_{subcommand}-phase-count");
                var index = int.TryParse(File.Exists(countPath) ? File.ReadAllText(countPath) : null,
                    NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : 0;
                File.WriteAllText(countPath, (index + 1).ToString(CultureInfo.InvariantCulture));
                var phase = sequence.Value[Math.Min(index, sequence.Value.GetArrayLength() - 1)];
                if (phase.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"Fake parser phase for '{subcommand}' must be an object.");
                foreach (var property in phase.EnumerateObject()) values[property.Name] = property.Value;
            }
            return JsonSerializer.Deserialize<Behaviour>(JsonSerializer.Serialize(values),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new Behaviour();
        }
    }
}
