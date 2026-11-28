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
    public void TheNextStepQuotesAPathWithSpacesAndKeepsOtherFlags()
    {
        var flags = new Dictionary<string, string>
            { ["project"] = "/tmp/my project/a.fwdata", ["kind"] = "bad-code", ["left-out"] = "true", ["limit"] = "50", ["json"] = "true" };

        var step = SIL.Motif.Cli.PagingFlags.NextCommand("warnings", [], flags, SIL.Motif.Contract.Responses.ResultPaging.Cursor(50), 50);

        Assert.Equal("motif warnings --project \"/tmp/my project/a.fwdata\" --kind bad-code --left-out --cursor " + SIL.Motif.Contract.Responses.ResultPaging.Cursor(50) + " --limit 50", step);
    }

    [Theory]
    [InlineData("--limit", "0")]
    [InlineData("--limit", "201")]
    [InlineData("--cursor", "-1")]
    [InlineData("--limit", "many")]
    public void AnOutOfRangeWindowIsRefusedWithTheOneRegisteredCode(string flag, string value)
    {
        Assert.Equal(0, Run("new", "--project", _project, "--draft", "d").ExitCode);

        foreach (var verb in new[] { new[] { "show", "d" } })
        {
            var result = Run([.. verb, "--project", _project, flag, value, "--json"]);

            Assert.NotEqual(0, result.ExitCode);
            Assert.Contains("paging.invalid", result.Output + result.Error, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ListRequestsWalksEveryRequestByCursorToTheEndWithNoOffsetField()
    {
        for (var i = 0; i < 5; i++)
            Assert.Equal(0, Run("request", "create", "--project", _project, "--kind", "word", "--target", "w" + i, "--label", "l" + i).ExitCode);

        var ids = new List<string>();
        var pages = 0;
        for (string? cursor = null; ; pages++)
        {
            var args = new List<string> { "list", "requests", "--project", _project, "--limit", "2", "--json" };
            if (cursor is not null) args.AddRange(["--cursor", cursor]);
            var result = Run(args.ToArray());
            Assert.True(result.ExitCode == 0, result.Error + result.Output);
            var page = JsonNode.Parse(result.Output)!;
            Assert.Null(page["offset"]);
            Assert.Equal(5, page["total"]!.GetValue<int>());
            ids.AddRange(page["requests"]!.AsArray().Select(item => item!["requestId"]!.GetValue<string>()));
            cursor = page["nextCursor"]?.GetValue<string>();
            if (cursor is null) break;
        }

        Assert.Equal(3, pages + 1);
        Assert.Equal(5, ids.Distinct().Count());
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
        Assert.Null(whole["nextCursor"]);

        var first = JsonNode.Parse(Run("show", "d", "--project", _project, "--limit", "10", "--json").Output)!;
        Assert.Equal(10, first["operations"]!.AsArray().Count);
        Assert.Equal(added, first["operationTotal"]!.GetValue<int>());
        Assert.True(first["operationsTruncated"]!.GetValue<bool>());
        Assert.Equal(SIL.Motif.Contract.Responses.ResultPaging.Cursor(10), first["nextCursor"]!.GetValue<string>());

        var seen = new List<string>();
        for (string? cursor = ""; cursor is not null;)
        {
            var page = JsonNode.Parse(Run("show", "d", "--project", _project, "--cursor", cursor.Length == 0 ? SIL.Motif.Contract.Responses.ResultPaging.Cursor(0) : cursor, "--limit", "25", "--json").Output)!;
            seen.AddRange(page["operations"]!.AsArray().Select(op => op!["operationId"]!.GetValue<string>()));
            cursor = page["nextCursor"]?.GetValue<string>();
        }

        var every = JsonNode.Parse(Run("show", "d", "--project", _project, "--json").Output)!;
        Assert.Equal(added, every["operationTotal"]!.GetValue<int>());
        Assert.Null(every["nextCursor"]);
        Assert.Equal(every["operations"]!.AsArray().Select(op => op!["operationId"]!.GetValue<string>()), seen);
        Assert.Equal(whole["operations"]!.AsArray().Select(op => op!["operationId"]!.GetValue<string>()), seen);

        var text = Run("show", "d", "--project", _project);
        Assert.Contains($"operations (showing 1-10 of {added})", text.Output, StringComparison.Ordinal);
        Assert.Contains("more operations: motif show d --project " + _project + " --cursor " + SIL.Motif.Contract.Responses.ResultPaging.Cursor(10) + " --limit 10, or --json for every operation",
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
