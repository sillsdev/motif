using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>Pins the <c>compose</c> verb through the in-process CLI.</summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group3)]
public sealed class ComposeCliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-compose-cli-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;

    public ComposeCliTests(PristineProjectFixture pristine)
    {
        Directory.CreateDirectory(_root);
        using var scratch = pristine.NewScratch();
        _project = scratch.ProjectId.Path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch { }
    }

    private CliProcessResult Run(params string[] arguments) =>
        CliInProcess.Run(Path.Combine(_root, "machine"), FakeParser.ExecutablePath, true, arguments);

    private static string Steps(params string[] names) => JsonSerializer.Serialize(names.Select(name => new
    {
        verb = "compose-author-phoneme",
        intent = new { name, representations = new[] { name } },
    }));

    [Fact]
    public void OneCallComposesStepsAndRemovesEarlierOperations()
    {
        Assert.Equal(0, Run("new", "--project", _project, "--draft", "d").ExitCode);

        var first = Run("compose", "--project", _project, "--proposal", "d", "--steps", Steps("p", "q"), "--json");

        Assert.True(first.ExitCode == 0, first.Error + first.Output);
        var json = JsonNode.Parse(first.Output)!;
        Assert.Equal(2, json["intents"]!.AsArray().Count);
        var removed = json["intents"]![1]!["operations"]!.AsArray().Select(node => node!["operationId"]!.GetValue<string>()).ToArray();

        var second = Run("compose", "--project", _project, "--proposal", "d", "--steps", Steps("r"),
            "--remove", string.Join(",", removed), "--json");

        Assert.True(second.ExitCode == 0, second.Error + second.Output);
        var after = JsonNode.Parse(second.Output)!;
        Assert.Single(after["intents"]!.AsArray());
        var firstCount = json["operationCount"]!.GetValue<int>();
        var removedCount = removed.Length;
        var addedCount = after["intents"]![0]!["operations"]!.AsArray().Count;
        Assert.Equal(firstCount - removedCount + addedCount, after["operationCount"]!.GetValue<int>());
        var text = Run("compose", "--project", _project, "--proposal", "d", "--steps", Steps("s"));
        Assert.True(text.ExitCode == 0, text.Error + text.Output);
        Assert.Contains("compose-author-phoneme", text.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("compose --project P --proposal d")]
    [InlineData("compose --project P --steps []")]
    public void MissingFlagsPrintUsage(string line)
    {
        var result = Run(line.Replace("P", _project).Split(' '));

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Usage: motif compose", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void MalformedStepsAreRefusedAsUsage()
    {
        var result = Run("compose", "--project", _project, "--proposal", "d", "--steps", "{\"verb\":1}");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("steps", result.Error + result.Output, StringComparison.OrdinalIgnoreCase);
    }
}
