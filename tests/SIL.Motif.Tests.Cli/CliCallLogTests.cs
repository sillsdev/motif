using System.Text.Json.Nodes;
using SIL.Motif.Cli;
using Xunit;

namespace SIL.Motif.Tests.Cli;

[Trait("MotifTestLevel", "Unit")]
public sealed class CliCallLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "motif-cli-call-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private IReadOnlyDictionary<string, string?> Environment(string? setting = null) => new Dictionary<string, string?>
    {
        ["MOTIF_CALL_LOG"] = setting ?? _directory,
        ["MOTIF_LANE"] = "lane-a",
    };

    [Fact]
    public void ACliInvocationIsLoggedWithItsExitCodeAndLane()
    {
        var exit = CliCallNote.Run(Environment(), ["query", "pairs"], () =>
        {
            CliCallNote.Current!.Name("query pairs", new Dictionary<string, string> { ["project"] = "p" }, ["x"]);
            return 2;
        });

        Assert.Equal(2, exit);
        var line = JsonNode.Parse(File.ReadAllLines(Directory.GetFiles(_directory).Single()).Single())!;
        Assert.Equal("cli", (string)line["surface"]!);
        Assert.Equal("query pairs", (string)line["tool"]!);
        Assert.Equal("lane-a", (string)line["lane"]!);
        Assert.Equal(2, (int)line["exit"]!);
        Assert.Equal("user_error", (string)line["outcome"]!);
        Assert.Equal(["$0", "project"], line["argKeys"]!.AsArray().Select(key => (string)key!));
    }

    [Fact]
    public void AnExceptionIsLoggedAsAnInternalErrorAndStillPropagates()
    {
        Assert.Throws<InvalidOperationException>(() =>
            CliCallNote.Run(Environment(), ["x"], () => throw new InvalidOperationException()));

        var line = JsonNode.Parse(File.ReadAllLines(Directory.GetFiles(_directory).Single()).Single())!;
        Assert.Equal("internal_error", (string)line["outcome"]!);
    }

    [Fact]
    public void OffLeavesTheInvocationUnlogged()
    {
        Assert.Equal(0, CliCallNote.Run(Environment("off"), ["x"], () => 0));
        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public void AnUnknownCommandIsLoggedAsUnknownWithoutItsTextOrValues()
    {
        const string privateText = "PRIVATE-CLI-IDENTIFIER-47F2";
        var environment = Environment();
        string[] args = [$"command-{privateText}", $"--field-{privateText}", privateText];
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exit = CliCallNote.Run(environment, args,
            () => global::SIL.Motif.Cli.Program.Run(args, output, error, environment, static (_, _) => { }));

        Assert.NotEqual(0, exit);
        var text = File.ReadAllText(Directory.GetFiles(_directory).Single());
        Assert.DoesNotContain(privateText, text, StringComparison.Ordinal);
        Assert.Equal("unknown", (string)JsonNode.Parse(text.Split('\n')[0])!["tool"]!);
    }
}
