using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// Drives <c>list</c>, <c>show</c>, <c>new</c> and <c>compose-batch</c> against a real project: a large store prints
/// one bounded page that names the total and the next step, and every page flag has one rule.
/// </summary>
[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class OutputBoundsCliTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-output-bounds-" + Guid.NewGuid().ToString("N"));
    private readonly string _project;

    public OutputBoundsCliTests(PristineProjectFixture pristine)
    {
        Directory.CreateDirectory(_root);
        using var scratch = pristine.NewScratch();
        _project = scratch.ProjectId.Path;
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private CliProcessResult Run(params string[] arguments) =>
        CliInProcess.Run(Path.Combine(_root, "machine"), FakeParser.ExecutablePath, true, arguments);

    private string IntentsFile(int count)
    {
        var path = Path.Combine(_root, "intents.json");
        File.WriteAllText(path, JsonSerializer.Serialize(new
        {
            intents = Enumerable.Range(0, count).Select(i => new
            {
                verb = "compose-author-phoneme",
                intent = new { name = "p" + i, representations = new[] { "p" + i } },
            }),
        }));
        return path;
    }

    [Fact]
    public void ListReturnsOnePageThatNamesTheTotalAndConcatenatesToTheFullList()
    {
        for (var i = 0; i < 30; i++)
            Assert.Equal(0, Run("new", "--project", _project, "--draft", "draft" + i).ExitCode);

        var first = JsonNode.Parse(Run("list", "--project", _project, "--json").Output)!;
        Assert.Equal(25, first["proposals"]!.AsArray().Count);
        Assert.Equal(30, first["total"]!.GetValue<int>());
        Assert.Equal(25, first["nextOffset"]!.GetValue<int>());

        var second = JsonNode.Parse(Run("list", "--project", _project, "--offset", "25", "--json").Output)!;
        Assert.Equal(5, second["proposals"]!.AsArray().Count);
        Assert.Null(second["nextOffset"]);

        var all = first["proposals"]!.AsArray().Concat(second["proposals"]!.AsArray())
            .Select(item => item!["proposalId"]!.GetValue<string>()).ToArray();
        Assert.Equal(30, all.Distinct().Count());

        var pastEnd = JsonNode.Parse(Run("list", "--project", _project, "--offset", "500", "--json").Output)!;
        Assert.Empty(pastEnd["proposals"]!.AsArray());
        Assert.Equal(30, pastEnd["total"]!.GetValue<int>());

        var text = Run("list", "--project", _project, "--limit", "10");
        Assert.Contains("Showing 1-10 of 30.", text.Output, StringComparison.Ordinal);
        Assert.Contains("Next: motif list --project " + _project + " --offset 10 --limit 10", text.Output, StringComparison.Ordinal);
        var allStatuses = Run("list", "--project", _project, "--all", "--limit", "10").Output;
        Assert.Contains(" --all --offset 10 --limit 10", allStatuses, StringComparison.Ordinal);
        var pastEndText = Run("list", "--project", _project, "--offset", "100").Output;
        Assert.Contains("0 shown; 30 proposals; last offset 29", pastEndText, StringComparison.Ordinal);
        Assert.DoesNotContain("No proposals in store", pastEndText, StringComparison.Ordinal);
        var byPage = Run("list", "--project", _project, "--limit", "10", "--page", "3");
        Assert.Contains("Showing 21-30 of 30.", byPage.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheNextStepQuotesAPathWithSpacesAndKeepsOtherFlags()
    {
        var flags = new Dictionary<string, string>
            { ["project"] = "/tmp/my project/a.fwdata", ["kind"] = "bad-code", ["left-out"] = "true", ["limit"] = "50", ["json"] = "true" };

        var step = SIL.Motif.Cli.PagingFlags.NextCommand("warnings", [], flags, 50, 50);

        Assert.Equal("motif warnings --project \"/tmp/my project/a.fwdata\" --kind bad-code --left-out --offset 50 --limit 50", step);
    }

    [Theory]
    [InlineData("--limit", "0")]
    [InlineData("--limit", "201")]
    [InlineData("--offset", "-1")]
    [InlineData("--limit", "many")]
    [InlineData("--page", "0")]
    public void AnOutOfRangeWindowIsRefusedWithTheOneRegisteredCode(string flag, string value)
    {
        Assert.Equal(0, Run("new", "--project", _project, "--draft", "d").ExitCode);

        foreach (var verb in new[] { new[] { "list" }, ["show", "d"] })
        {
            var result = Run([.. verb, "--project", _project, flag, value, "--json"]);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("paging.invalid", result.Output + result.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void FlagsThatContradictEachOtherAreRefused()
    {
        Assert.Equal(0, Run("new", "--project", _project, "--draft", "d").ExitCode);

        var offsetAndPage = Run("show", "d", "--project", _project, "--offset", "5", "--page", "2", "--json");
        var allAndLimit = Run("show", "d", "--project", _project, "--all", "--limit", "5", "--json");

        Assert.Contains("paging.invalid", offsetAndPage.Output + offsetAndPage.Error, StringComparison.Ordinal);
        Assert.Contains("paging.invalid", allAndLimit.Output + allAndLimit.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ShowPagesOperationsAndComposeBatchReportsABoundedSummary()
    {
        Assert.Equal(0, Run("new", "--project", _project, "--draft", "d").ExitCode);

        var batch = Run("compose-batch", "--project", _project, "--draft", "d", "--intents", IntentsFile(40), "--json");

        Assert.True(batch.ExitCode == 0, batch.Error + batch.Output);
        var report = JsonNode.Parse(batch.Output)!;
        var added = report["operationCount"]!.GetValue<int>();
        Assert.True(added > 25);
        Assert.Equal(40, report["intents"]!.AsArray().Count);
        Assert.Equal(added, report["intents"]!.AsArray().Sum(intent => intent!["operations"]!.AsArray().Count));
        Assert.Null(report["nextStep"]);
        Assert.Equal(added, report["operationCount"]!.GetValue<int>());

        var whole = JsonNode.Parse(Run("show", "d", "--project", _project, "--json").Output)!;
        Assert.Equal(added, whole["operations"]!.AsArray().Count);
        Assert.Equal(added, whole["operationTotal"]!.GetValue<int>());
        Assert.Null(whole["operationsTruncated"]);
        Assert.Null(whole["nextOffset"]);

        var first = JsonNode.Parse(Run("show", "d", "--project", _project, "--limit", "10", "--json").Output)!;
        Assert.Equal(10, first["operations"]!.AsArray().Count);
        Assert.Equal(added, first["operationTotal"]!.GetValue<int>());
        Assert.True(first["operationsTruncated"]!.GetValue<bool>());
        Assert.Equal(10, first["nextOffset"]!.GetValue<int>());

        var seen = new List<string>();
        for (int? offset = 0; offset is { } at;)
        {
            var page = JsonNode.Parse(Run("show", "d", "--project", _project, "--offset", at.ToString(), "--limit", "25", "--json").Output)!;
            seen.AddRange(page["operations"]!.AsArray().Select(op => op!["operationId"]!.GetValue<string>()));
            offset = page["nextOffset"]?.GetValue<int>();
        }

        var every = JsonNode.Parse(Run("show", "d", "--project", _project, "--all", "--json").Output)!;
        Assert.Equal(added, every["operationTotal"]!.GetValue<int>());
        Assert.Null(every["nextOffset"]);
        Assert.Equal(every["operations"]!.AsArray().Select(op => op!["operationId"]!.GetValue<string>()), seen);
        Assert.Equal(whole["operations"]!.AsArray().Select(op => op!["operationId"]!.GetValue<string>()), seen);

        var text = Run("show", "d", "--project", _project);
        Assert.Contains($"operations (showing 1-10 of {added})", text.Output, StringComparison.Ordinal);
        Assert.Contains("more operations: motif show d --project " + _project + " --offset 10 --limit 10, or --all or --json for every operation",
            text.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void NewAndComposeBatchHonourJson()
    {
        var created = Run("new", "--project", _project, "--draft", "d", "--label", "L", "--json");
        Assert.Equal(0, created.ExitCode);
        var draft = JsonNode.Parse(created.Output)!;
        Assert.Equal("d", draft["draftName"]!.GetValue<string>());
        Assert.Equal("L", draft["label"]!.GetValue<string>());

        var text = Run("compose-batch", "--project", _project, "--draft", "d", "--intents", IntentsFile(2));
        Assert.Contains("Composed 2 intent(s) against draft 'd'", text.Output, StringComparison.Ordinal);
    }
}
