using SIL.Motif.App.GuideQueries;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Gdr;
using SIL.Motif.Contract.Responses;
using Xunit;
using static SIL.Motif.Tests.App.GuideQueryTestSupport;

namespace SIL.Motif.Tests.App;

/// <summary>A Guide block whose run line is a v2 <c>motif query corpus forms</c> line: shown from a read-only evaluator, never stored, never blocking.</summary>
public sealed class GuideEvidenceBlockTests
{
    private const string Run = "motif query corpus forms --corpus words.tsv --grammar g.fwdata forms where 'readings > 0'";

    private static readonly string[] Lines =
    [
        "```motif-query", "id: ev", "title: Evidence", "run: " + Run, "```",
        "```motif-show", "query: ev", "show: table", "limit: 1", "```",
        "```motif-show", "query: ev", "show: counts", "```",
    ];

    private static GuideEvidenceResult Result(GuideEvidenceStatus status, GuideEvidenceTable? table, string message, string? code = null, string? command = null) =>
        new(status, table, message, code, command, 1, new Dictionary<string, double>());

    private static async Task<(GuideQueryBlockViewModel Block, List<(string, string)> Calls)> Shown(Func<GuideEvidenceResult> answer)
    {
        var calls = new List<(string, string)>();
        var v1Runs = 0;
        var tables = new GuideQueryTablesViewModel(GuideQueryContext.Baseline, () => "/proj/p.fwdata", (_, _) => { v1Runs++; return Task.FromResult(Answer(BaselineAnswer())); }, null)
        {
            EvidenceRun = (project, line, _) => { calls.Add((project, line)); return Task.FromResult(answer()); },
        };
        tables.Show(GuideQueryDocument.From(Lines));
        await tables.Running;
        Assert.Equal(0, v1Runs);
        return (Assert.Single(tables.Blocks), calls);
    }

    private static GuideEvidenceTable Table(bool complete) =>
        new(["form", "tokens"], [["solo", "10"], ["pair", "5"]], (complete ? "" : "at least ") + "2 word types (15 tokens)", complete, complete ? null : "the parse is incomplete", 2, 15, 0);

    [Fact]
    public async Task AnIncompleteAnswerShowsAtLeastInEveryDisplayAndRunsTheLineOnce()
    {
        var (block, calls) = await Shown(() => Result(GuideEvidenceStatus.Ready, Table(false), "x"));
        Assert.Equal([("/proj/p.fwdata", Run)], calls);
        Assert.True(block.IsEvidence);
        Assert.All(block.EvidenceDisplays, display => Assert.StartsWith("at least 2 word types", display.Headline));
        Assert.Equal(["form | tokens", "solo | 10"], block.EvidenceDisplays[0].Lines.Take(2));
        Assert.Empty(block.EvidenceDisplays[1].Lines);
    }

    [Fact]
    public async Task SaveTableStagesOnlyWhenPressedAndNeverWhenShown()
    {
        var staged = new List<string>();
        var tables = new GuideQueryTablesViewModel(GuideQueryContext.Baseline, () => "/proj/p.fwdata", (_, _) => Task.FromResult(Answer(BaselineAnswer())), null)
        {
            EvidenceRun = (_, _, _) => Task.FromResult(Result(GuideEvidenceStatus.Ready, Table(true), "x")),
            SaveTable = (queryId, _) =>
            {
                staged.Add(queryId);
                return Task.FromResult(CommandOutcome<GdrRefreshTablesResponse>.Success(new GdrRefreshTablesResponse("d", [new GdrRefreshedSection("GDR-2", "GDR-2",
                    [new GdrRefreshedTable(queryId, "saved", "", 2, 2)])], 1, [])));
            },
        };
        tables.Show(GuideQueryDocument.From(Lines));
        await tables.Running;
        var block = Assert.Single(tables.Blocks);
        Assert.Empty(staged);
        Assert.True(block.OffersSaveTable);

        await block.SaveTableCommand.ExecuteAsync(null);

        Assert.Equal(["ev"], staged);
        Assert.Contains("2 of 2", block.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ABlockHasNoSaveTableWhenTheWindowCannotStageOne()
    {
        var (block, _) = await Shown(() => Result(GuideEvidenceStatus.Ready, Table(true), "x"));
        Assert.False(block.OffersSaveTable);
    }

    [Fact]
    public async Task ACompleteAnswerHasNoAtLeast()
    {
        var (block, _) = await Shown(() => Result(GuideEvidenceStatus.Ready, Table(true), "x"));
        Assert.All(block.EvidenceDisplays, display => Assert.Equal("2 word types (15 tokens)", display.Headline));
    }

    [Fact]
    public async Task ARefusalIsShownWithItsCodeAndNoTable()
    {
        var (block, _) = await Shown(() => Result(GuideEvidenceStatus.Refused, null, "query.twin-needs-regex: a twin needs a form ~ pattern.", "query.twin-needs-regex"));
        Assert.All(block.EvidenceDisplays, display =>
        {
            Assert.StartsWith("query.twin-needs-regex:", display.Headline);
            Assert.Empty(display.Lines);
        });
    }

    [Fact]
    public async Task AColdIndexSaysNotBuiltAndTheBlockIsNotLeftRunning()
    {
        var (block, _) = await Shown(() => Result(GuideEvidenceStatus.ColdIndex, null, "parse index not built: run cd '/proj' && " + Run, "query.parse-index-cold", "cd '/proj' && " + Run));
        Assert.All(block.EvidenceDisplays, display => Assert.StartsWith("parse index not built: run ", display.Headline));
        Assert.False(block.IsRunning);
    }

    [Fact]
    public async Task AnEvidenceBlockWithoutAnEvaluatorFailsWithAMessageInsteadOfRunning()
    {
        var tables = new GuideQueryTablesViewModel(GuideQueryContext.Baseline, () => "/p", (_, _) => Task.FromResult(Answer(BaselineAnswer())), null);
        tables.Show(GuideQueryDocument.From(Lines));
        await tables.Running;
        Assert.False(Assert.Single(tables.Blocks).IsRunning);
    }
}
