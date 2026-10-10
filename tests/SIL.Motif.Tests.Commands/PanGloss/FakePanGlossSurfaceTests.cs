using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Generator;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>Checks emitted requests against the executable's declared surface without opening a project.</summary>
public sealed class FakePanGlossSurfaceTests
    : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "motif-pangloss-surface-" + Guid.NewGuid().ToString("N"));

    public FakePanGlossSurfaceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task MissingExecutableIsInvalidThroughTheSurfaceCheck()
    {
        var result = await CheckSurfaceAsync(
            Path.Combine(_root, "missing", FakeParser.ExecutableFileName), CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("could not start --describe", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APathThatCannotStartIsInvalidThroughTheSurfaceCheck()
    {
        var directory = Path.Combine(_root, "not-an-executable");
        Directory.CreateDirectory(directory);

        var result = await CheckSurfaceAsync(directory, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("could not start --describe", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADescribeTimeoutIsInvalidAndTheChildIsContained()
    {
        var executable = FakeParser.CopyWithSentinel(
            Path.Combine(_root, "describe-hang"), "_fake-pangloss-describe-hang");

        Assert.Equal(15, PanGlossSurface.DefaultDescriptionCapSeconds);
        var result = await CheckSurfaceAsync(executable, CancellationToken.None,
            descriptionCap: TimeSpan.FromSeconds(1));

        Assert.False(result.IsValid);
        Assert.Contains("did not finish within 1 second", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANonZeroDescribeExitIsInvalidThroughTheSurfaceCheck()
    {
        var executable = FakeParser.CopyWithSentinel(
            Path.Combine(_root, "describe-fail"), "_fake-pangloss-describe-fail");

        var result = await CheckSurfaceAsync(executable, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("--describe exited 17", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnparseableDescribeOutputIsInvalidThroughTheSurfaceCheck()
    {
        var executable = FakeParser.CopyWithSentinel(
            Path.Combine(_root, "describe-malformed"), "_fake-pangloss-describe-malformed");

        var result = await CheckSurfaceAsync(executable, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("returned invalid JSON", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWrongDescriptionIsInvalidThroughTheSurfaceCheck()
    {
        var executable = FakeParser.CopyWithWrongDescription(Path.Combine(_root, "wrong-description"));

        var result = await CheckSurfaceAsync(executable, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("schema version 1", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AValidDescriptionIsValidThroughTheSurfaceCheck()
    {
        var executable = FakeParser.Copy(Path.Combine(_root, "valid"));

        var result = await CheckSurfaceAsync(executable, CancellationToken.None);

        Assert.True(result.IsValid, result.Message);
        Assert.Equal(string.Empty, result.Message);
    }

    [Fact]
    public async Task APreCancelledTokenIsPropagatedThroughTheSurfaceCheck()
    {
        var executable = FakeParser.Copy(Path.Combine(_root, "cancelled"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CheckSurfaceAsync(executable, cancellation.Token));
    }

    [Fact]
    public async Task FakeDescriptionNamesExactlyItsDispatchCommands()
    {
        using var description = await Describe(FakeParser.ExecutablePath);
        Assert.Equal(1, description.RootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal("pangloss", description.RootElement.GetProperty("binary").GetString());
        var commands = Commands(description);
        Assert.Equal(new[] { "batch", "describe", "grammar-health", "import", "parse", "stats" },
            commands.Keys.OrderBy(name => name));
        AssertRequestsMatch(commands);
        AssertTraceCommandIsDeclared(commands);
    }


    [Fact]
    public void GrammarHealthRequestWritesItsReportToStandardOutput()
    {
        var request = new PanGlossRequest.GrammarHealth("project.fwdata", "project");
        var start = new ProcessStartInfo();

        request.AddArguments(start, "scratch");

        Assert.Equal(new[] { "grammar-health", "project.fwdata", "--fw-project", "project" },
            start.ArgumentList);
    }

    internal static Dictionary<string, JsonElement> Commands(JsonDocument document) =>
        document.RootElement.GetProperty("commands").EnumerateArray()
            .ToDictionary(command => command.GetProperty("name").GetString()!, command => command);

    internal static void AssertRequestsMatch(IReadOnlyDictionary<string, JsonElement> commands)
    {
        PanGlossRequest[] requests =
        [
            new PanGlossRequest.Batch("project.fwdata", ["motifa"], TimeSpan.FromSeconds(1), "cache.sqlite"),
            new PanGlossRequest.Stats("project.fwdata", "cache.sqlite", ["--group", "word", "--format", "jsonl"]),
            new PanGlossRequest.Import("project.fwdata", "grammar.json"),
            new PanGlossRequest.GrammarHealth("project.fwdata", "project"),
            new PanGlossRequest.Trace("project.fwdata", "motifa")
                { StepLimit = SIL.Motif.Contract.Assess.StepCap.Default },
        ];
        // Trace is checked by AssertTraceCommandIsDeclared; this walker cannot express an "=file" flag.
        var requestTypes = typeof(PanGlossRequest).GetNestedTypes()
            .Where(type => typeof(PanGlossRequest).IsAssignableFrom(type) && type != typeof(PanGlossRequest.Trace))
            .OrderBy(type => type.Name);
        Assert.Equal(requestTypes, requests.Where(request => request is not PanGlossRequest.Trace)
            .Select(request => request.GetType()).OrderBy(type => type.Name));
        using var pin = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoPaths.FindRepoRoot(), "pangloss-release.json")));
        var pinnedRequests = pin.RootElement.GetProperty("interfaces").GetProperty("requests");
        Assert.Equal(requests.Select(request => request.Subcommand).OrderBy(name => name, StringComparer.Ordinal),
            pinnedRequests.EnumerateObject().Select(command => command.Name).OrderBy(name => name, StringComparer.Ordinal));
        foreach (var request in requests)
        {
            var start = new ProcessStartInfo();
            request.AddArguments(start, "scratch");
            Assert.Equal(request.Subcommand, start.ArgumentList[0]);
            Assert.True(commands.TryGetValue(request.Subcommand, out var command));
            Assert.False(command.GetProperty("hidden").GetBoolean());
            var flags = command.GetProperty("flags").EnumerateArray()
                .ToDictionary(flag => flag.GetProperty("name").GetString()!, flag => flag);
            var pinnedFlags = pinnedRequests.GetProperty(request.Subcommand);
            Assert.Equal(flags.Keys.Where(name => request.Subcommand == "stats"
                        ? name is "--cache" or "--group" or "--format"
                        : request.Subcommand == "batch"
                            ? name is "--word-timeout-ms" or "--step-cap" or "--threads" or "--analyses" or "--stats" or "--cache"
                            : request.Subcommand == "parse"
                                ? name is "--trace" or "--trace-format" or "--trace-details" or "--step-cap"
                                : request.Subcommand == "grammar-health" && name == "--fw-project")
                    .OrderBy(name => name, StringComparer.Ordinal),
                pinnedFlags.EnumerateObject().Select(flag => flag.Name).OrderBy(name => name, StringComparer.Ordinal));
            foreach (var pinnedFlag in pinnedFlags.EnumerateObject())
            {
                Assert.True(flags.TryGetValue(pinnedFlag.Name, out var declaredFlag),
                    $"'{request.Subcommand}' does not declare pinned flag '{pinnedFlag.Name}'.");
                Assert.Equal(pinnedFlag.Value.GetBoolean(), declaredFlag.GetProperty("takes_value").GetBoolean());
            }
            if (request is PanGlossRequest.Trace) continue;
            var positionals = 0;
            for (var index = 1; index < start.ArgumentList.Count; index++)
            {
                var argument = start.ArgumentList[index];
                if (!argument.StartsWith("--", StringComparison.Ordinal))
                {
                    positionals++;
                    continue;
                }
                Assert.True(flags.TryGetValue(argument, out var flag),
                    $"'{request.Subcommand}' does not declare emitted flag '{argument}'.");
                if (!flag.GetProperty("takes_value").GetBoolean()) continue;
                Assert.True(++index < start.ArgumentList.Count, $"'{argument}' lacks its declared value.");
                Assert.False(start.ArgumentList[index].StartsWith("--", StringComparison.Ordinal));
            }
            var declaredPositionals = command.GetProperty("positionals").EnumerateArray()
                .Select(positional => positional.GetString()!)
                .ToArray();
            var requiredPositionals = declaredPositionals.Count(positional =>
                !positional.EndsWith("?", StringComparison.Ordinal));
            Assert.InRange(positionals, requiredPositionals, declaredPositionals.Length);
        }
    }

    /// A narrower check for `parse`, skipping AssertRequestsMatch's value-adjacency walk (see its comment).
    internal static void AssertTraceCommandIsDeclared(IReadOnlyDictionary<string, JsonElement> commands)
    {
        Assert.True(commands.TryGetValue("parse", out var parse), "the description does not expose 'parse'.");
        Assert.False(parse.GetProperty("hidden").GetBoolean());
        Assert.Equal(2, parse.GetProperty("positionals").GetArrayLength());
        var flags = parse.GetProperty("flags").EnumerateArray()
            .ToDictionary(flag => flag.GetProperty("name").GetString()!, flag => flag);
        Assert.True(flags.TryGetValue("--trace", out var trace));
        Assert.True(trace.GetProperty("takes_value").GetBoolean());
        Assert.True(flags.TryGetValue("--trace-format", out var traceFormat),
            "the description does not declare 'parse --trace-format'.");
        Assert.True(traceFormat.GetProperty("takes_value").GetBoolean());
        Assert.True(flags.TryGetValue("--trace-details", out var traceDetails),
            "the description does not declare 'parse --trace-details'.");
        Assert.False(traceDetails.GetProperty("takes_value").GetBoolean());
    }

    internal static async Task<JsonDocument> Describe(string executable)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        start.ArgumentList.Add("--describe");
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try { await process.WaitForExitAsync(deadline.Token); }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        Assert.True(process.ExitCode == 0, $"--describe exited {process.ExitCode}: {await error}");
        return JsonDocument.Parse(await output);
    }

    private static async Task<PanGlossSurfaceCheck> CheckSurfaceAsync(string executable,
        CancellationToken cancellationToken, TimeSpan? descriptionCap = null)
    {
        using var containment = PanGlossContainment.CreateJob();
        return await PanGlossSurface.CheckAsync(executable, containment, cancellationToken, descriptionCap);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
