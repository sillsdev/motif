using System.Diagnostics;
using System.Text.Json;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>Checks emitted requests against the executable's declared surface without opening a project.</summary>
public sealed class PanGlossSurfaceTests
    : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "motif-pangloss-surface-" + Guid.NewGuid().ToString("N"));

    public PanGlossSurfaceTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task MissingExecutableIsInvalidThroughTheSurfaceCheck()
    {
        var result = await PanGlossSurface.CheckAsync(
            Path.Combine(_root, "missing", "pangloss.exe"), _ => { }, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("could not start --describe", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APathThatCannotStartIsInvalidThroughTheSurfaceCheck()
    {
        var directory = Path.Combine(_root, "not-an-executable");
        Directory.CreateDirectory(directory);

        var result = await PanGlossSurface.CheckAsync(directory, _ => { }, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("could not start --describe", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADescribeTimeoutIsInvalidAndTheChildIsContained()
    {
        var executable = FakeParser.CopyWithSentinel(
            Path.Combine(_root, "describe-hang"), "_fake-pangloss-describe-hang");

        var result = await PanGlossSurface.CheckAsync(executable, _ => { }, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("did not finish within 15 seconds", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANonZeroDescribeExitIsInvalidThroughTheSurfaceCheck()
    {
        var executable = FakeParser.CopyWithSentinel(
            Path.Combine(_root, "describe-fail"), "_fake-pangloss-describe-fail");

        var result = await PanGlossSurface.CheckAsync(executable, _ => { }, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("--describe exited 17", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnparseableDescribeOutputIsInvalidThroughTheSurfaceCheck()
    {
        var executable = FakeParser.CopyWithSentinel(
            Path.Combine(_root, "describe-malformed"), "_fake-pangloss-describe-malformed");

        var result = await PanGlossSurface.CheckAsync(executable, _ => { }, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("returned invalid JSON", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWrongDescriptionIsInvalidThroughTheSurfaceCheck()
    {
        var executable = FakeParser.CopyWithWrongDescription(Path.Combine(_root, "wrong-description"));

        var result = await PanGlossSurface.CheckAsync(executable, _ => { }, CancellationToken.None);

        Assert.False(result.IsValid);
        Assert.Contains("schema version 1", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AValidDescriptionIsValidThroughTheSurfaceCheck()
    {
        var executable = FakeParser.Copy(Path.Combine(_root, "valid"));

        var result = await PanGlossSurface.CheckAsync(executable, _ => { }, CancellationToken.None);

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
            PanGlossSurface.CheckAsync(executable, _ => { }, cancellation.Token));
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

    [RealParserFact]
    public async Task RealDescriptionContainsEveryTypedRequestAndFakeCommand()
    {
        using var realDescription = await Describe(PanGlossExecutable.TryLocate()!);
        using var fakeDescription = await Describe(FakeParser.ExecutablePath);
        Assert.Equal(1, realDescription.RootElement.GetProperty("schema_version").GetInt32());
        var commands = Commands(realDescription);
        AssertRequestsMatch(commands);
        AssertTraceCommandIsDeclared(commands);
        foreach (var fake in Commands(fakeDescription))
        {
            Assert.True(commands.TryGetValue(fake.Key, out var real), $"FakePanGloss invented '{fake.Key}'.");
            Assert.False(real.GetProperty("hidden").GetBoolean());
            Assert.Equal(real.GetProperty("positionals").GetArrayLength(),
                fake.Value.GetProperty("positionals").GetArrayLength());
            var realFlags = real.GetProperty("flags").EnumerateArray()
                .ToDictionary(flag => flag.GetProperty("name").GetString()!, flag => flag);
            foreach (var fakeFlag in fake.Value.GetProperty("flags").EnumerateArray())
            {
                var name = fakeFlag.GetProperty("name").GetString()!;
                Assert.True(realFlags.TryGetValue(name, out var realFlag),
                    $"FakePanGloss invented '{fake.Key} {name}'.");
                Assert.Equal(realFlag.GetProperty("takes_value").GetBoolean(),
                    fakeFlag.GetProperty("takes_value").GetBoolean());
            }
        }
        Assert.DoesNotContain("assess", Commands(fakeDescription).Keys);
    }

    private static Dictionary<string, JsonElement> Commands(JsonDocument document) =>
        document.RootElement.GetProperty("commands").EnumerateArray()
            .ToDictionary(command => command.GetProperty("name").GetString()!, command => command);

    private static void AssertRequestsMatch(IReadOnlyDictionary<string, JsonElement> commands)
    {
        PanGlossRequest[] requests =
        [
            new PanGlossRequest.Batch("project.fwdata", ["motifa"], TimeSpan.FromSeconds(1), "cache.sqlite"),
            new PanGlossRequest.Stats("project.fwdata", "cache.sqlite", ["--group", "word", "--format", "jsonl"]),
            new PanGlossRequest.Import("project.fwdata", "grammar.json"),
            new PanGlossRequest.GrammarHealth("project.fwdata"),
        ];
        // Trace is checked by AssertTraceCommandIsDeclared; this walker cannot express an "=file" flag.
        var requestTypes = typeof(PanGlossRequest).GetNestedTypes()
            .Where(type => typeof(PanGlossRequest).IsAssignableFrom(type) && type != typeof(PanGlossRequest.Trace))
            .OrderBy(type => type.Name);
        Assert.Equal(requestTypes, requests.Select(request => request.GetType()).OrderBy(type => type.Name));
        foreach (var request in requests)
        {
            var start = new ProcessStartInfo();
            request.AddArguments(start, "scratch");
            Assert.Equal(request.Subcommand, start.ArgumentList[0]);
            Assert.True(commands.TryGetValue(request.Subcommand, out var command));
            Assert.False(command.GetProperty("hidden").GetBoolean());
            var flags = command.GetProperty("flags").EnumerateArray()
                .ToDictionary(flag => flag.GetProperty("name").GetString()!, flag => flag);
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
            Assert.Equal(command.GetProperty("positionals").GetArrayLength(), positionals);
        }
    }

    /// A narrower check for `parse`, skipping AssertRequestsMatch's value-adjacency walk (see its comment).
    private static void AssertTraceCommandIsDeclared(IReadOnlyDictionary<string, JsonElement> commands)
    {
        Assert.True(commands.TryGetValue("parse", out var parse), "the description does not expose 'parse'.");
        Assert.False(parse.GetProperty("hidden").GetBoolean());
        Assert.Equal(2, parse.GetProperty("positionals").GetArrayLength());
        var flags = parse.GetProperty("flags").EnumerateArray()
            .ToDictionary(flag => flag.GetProperty("name").GetString()!, flag => flag);
        Assert.True(flags.ContainsKey("--trace"), "the description does not declare 'parse --trace'.");
        Assert.True(flags.TryGetValue("--trace-format", out var traceFormat),
            "the description does not declare 'parse --trace-format'.");
        Assert.True(traceFormat.GetProperty("takes_value").GetBoolean());
        Assert.True(flags.TryGetValue("--trace-details", out var traceDetails),
            "the description does not declare 'parse --trace-details'.");
        Assert.False(traceDetails.GetProperty("takes_value").GetBoolean());
    }

    private static async Task<JsonDocument> Describe(string executable)
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

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
