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
{
    [Fact]
    public async Task FakeDescriptionNamesExactlyItsDispatchCommands()
    {
        using var description = await Describe(FakeParser.ExecutablePath);
        Assert.Equal(1, description.RootElement.GetProperty("schema_version").GetInt32());
        Assert.Equal("pangloss", description.RootElement.GetProperty("binary").GetString());
        var commands = Commands(description);
        Assert.Equal(new[] { "batch", "describe", "import", "stats" }, commands.Keys.OrderBy(name => name));
        AssertRequestsMatch(commands);
    }

    [RealParserFact]
    public async Task RealDescriptionContainsEveryTypedRequestAndFakeCommand()
    {
        using var realDescription = await Describe(PanGlossExecutable.TryLocate()!);
        using var fakeDescription = await Describe(FakeParser.ExecutablePath);
        Assert.Equal(1, realDescription.RootElement.GetProperty("schema_version").GetInt32());
        var commands = Commands(realDescription);
        AssertRequestsMatch(commands);
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
        ];
        var requestTypes = typeof(PanGlossRequest).GetNestedTypes()
            .Where(type => typeof(PanGlossRequest).IsAssignableFrom(type)).OrderBy(type => type.Name);
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
}
