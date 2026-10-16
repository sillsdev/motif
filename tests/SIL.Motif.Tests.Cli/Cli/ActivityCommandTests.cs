using System.Text.Json;
using SIL.Motif.Cli;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class ActivityCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-activity-cli-" + Guid.NewGuid().ToString("N"));

    public ActivityCommandTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string WriteLog()
    {
        var path = Path.Combine(_root, "activity.jsonl");
        File.WriteAllLines(path,
        [
            "{\"tool\":\"motif_overview\",\"isError\":true,\"code\":\"baseline.missing\",\"followedNext\":null}",
            "{\"tool\":\"motif_capture_baseline\",\"isError\":false,\"code\":null,\"followedNext\":true}",
            "{\"tool\":\"motif_overview\",\"isError\":false,\"code\":null,\"followedNext\":false}",
        ]);
        return path;
    }

    private (int ExitCode, string Output, string Error) Run(string arguments)
    {
        var result = CliInProcess.RunCommandLine(Path.Combine(_root, "machine"), FakeParser.ExecutablePath, false, arguments);
        return (result.ExitCode, result.Output, result.Error);
    }

    [Fact]
    public void ActivityIsAReleasedReadCommandThatIsNotASecondMcpTool()
    {
        var command = Assert.Single(CommandCatalog.All, candidate => candidate.Name == "activity");

        Assert.Equal(CommandSurface.Released, command.Surface);
        Assert.False(command.IsAgentTool);
    }

    [Fact]
    public void JsonReportsCallsByToolErrorsRefusalsByCodeAndFollowedAndIgnoredNext()
    {
        var result = Run($"activity --log \"{WriteLog()}\" --json");

        Assert.Equal(0, result.ExitCode);
        using var json = JsonDocument.Parse(result.Output);
        var root = json.RootElement;
        Assert.Equal(3, root.GetProperty("total").GetInt32());
        Assert.Equal(1, root.GetProperty("errors").GetInt32());
        Assert.Equal(2, root.GetProperty("byTool").GetProperty("motif_overview").GetInt32());
        Assert.Equal(1, root.GetProperty("refusalsByCode").GetProperty("baseline.missing").GetInt32());
        Assert.Equal(1, root.GetProperty("followedNext").GetInt32());
        Assert.Equal(1, root.GetProperty("ignoredNext").GetInt32());
    }

    [Fact]
    public void TextNamesEachMeasure()
    {
        var result = Run($"activity --log \"{WriteLog()}\"");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("baseline.missing", result.Output);
        Assert.Contains("motif_overview", result.Output);
        Assert.Contains("followed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ALogThatIsNotThereIsARefusalNotACrash()
    {
        var result = Run($"activity --log \"{Path.Combine(_root, "absent.jsonl")}\" --json");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("activity.log-unreadable", result.Error);
    }
}
