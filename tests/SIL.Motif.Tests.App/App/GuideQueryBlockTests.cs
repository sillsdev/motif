using SIL.Motif.App.GuideQueries;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;
using static SIL.Motif.Tests.App.GuideQueryTestSupport;

namespace SIL.Motif.Tests.App;

public sealed class GuideQueryBlockTests
{
    private static readonly GuideQueryContext Proposal = new("prop-1");

    private sealed class Harness
    {
        public List<string> Lines { get; } = [];
        public Func<string, CommandOutcome<FormsQueryResponse>> Answer { get; set; } = _ => GuideQueryTestSupport.Answer(BaselineAnswer());
        public List<(string Id, string Body)> Saved { get; } = [];
        public string? SaveRefusal { get; set; }

        public GuideQueryTablesViewModel Tables(GuideQueryContext context, string? project = "/p") =>
            new(context, () => project, (request, _) =>
            {
                Lines.Add(request.Line);
                return Task.FromResult(Answer(request.Line));
            }, null)
            {
                SaveBody = (id, body) =>
                {
                    Saved.Add((id, body));
                    return Task.FromResult(SaveRefusal);
                },
            };
    }

    private static async Task<GuideQueryBlockViewModel> Shown(GuideQueryTablesViewModel tables, string[]? lines = null)
    {
        tables.Show(GuideQueryDocument.From(lines ?? SectionLines));
        await tables.Running;
        return Assert.Single(tables.Blocks);
    }

    [Fact]
    public void ABaselineContextRunsTheStoredLineAsOfTheBaseline()
    {
        var (line, refusal) = GuideQueryRunLines.Prepare("motif query forms --where kind=alternate", GuideQueryContext.Baseline);

        Assert.Null(refusal);
        Assert.Equal("motif query forms --where kind=alternate --as-of baseline", line);
    }

    [Fact]
    public void AProposalContextRunsBothSides()
    {
        var (line, _) = GuideQueryRunLines.Prepare("motif query forms", Proposal);

        Assert.Equal("motif query forms --as-of prop-1 --sides both", line);
    }

    [Theory]
    [InlineData("motif parse approve --all")]
    [InlineData("rm -rf /")]
    [InlineData("motif query forms --as-of baseline")]
    [InlineData("motif group forms")]
    public void LinesThatAreNotPlainReadsAreRefusedWithAMessage(string run)
    {
        var (line, refusal) = GuideQueryRunLines.Prepare(run, GuideQueryContext.Baseline);

        Assert.Null(line);
        Assert.NotEmpty(refusal!);
    }

    [UiThreadFact]
    public async Task ABlockRunsOnShowAndFillsItsTable()
    {
        var harness = new Harness();
        var block = await Shown(harness.Tables(GuideQueryContext.Baseline));

        Assert.Equal("Alternate forms", block.Title);
        Assert.Equal(["motif query forms --where kind=alternate --as-of baseline"], harness.Lines);
        Assert.Equal(3, block.Displays[0].Rows.Count);
        Assert.Equal("", block.Notice);
    }

    [UiThreadFact]
    public async Task ARefusalShowsInTheBlockAndEmptiesTheTable()
    {
        var harness = new Harness { Answer = _ => CommandOutcome<FormsQueryResponse>.Refused(new Refusal("x.no", FailureReason.Refused, "nope")) };
        var block = await Shown(harness.Tables(GuideQueryContext.Baseline));

        Assert.Contains("nope", block.Notice);
        Assert.Empty(block.Displays[0].Rows);
    }

    [UiThreadFact]
    public async Task ANonReadOnlyRunLineIsRefusedInTheBlockWithoutRunning()
    {
        var harness = new Harness();
        var lines = SectionLines.Select(line => line.StartsWith("run:") ? "run: motif parse approve --all" : line).ToArray();
        var block = await Shown(harness.Tables(GuideQueryContext.Baseline), lines);

        Assert.True(block.HasNotice);
        Assert.Empty(harness.Lines);
    }

    [UiThreadFact]
    public async Task SavingStagesOnlyTheQueryTextThenRerunsAndKeepsNoResults()
    {
        var harness = new Harness();
        var block = await Shown(harness.Tables(GuideQueryContext.Baseline));
        block.EditCommand.Execute(null);
        Assert.False(block.CanSave);
        Assert.NotEmpty(block.SaveReason);

        block.DraftText = "id: alternates\ntitle: Alternates\nrun: motif query forms";
        Assert.True(block.CanSave);
        await block.SaveCommand.ExecuteAsync(null);

        var saved = Assert.Single(harness.Saved);
        Assert.Equal(("alternates", "id: alternates\ntitle: Alternates\nrun: motif query forms"), saved);
        Assert.False(block.IsEditing);
        Assert.Equal(saved.Body, block.Body);
        Assert.Equal("motif query forms --as-of baseline", harness.Lines[^1]);
        Assert.DoesNotContain("kaonan", saved.Body);
    }

    [UiThreadFact]
    public async Task AnInvalidDraftCannotBeSavedAndSaysWhy()
    {
        var harness = new Harness();
        var block = await Shown(harness.Tables(GuideQueryContext.Baseline));
        block.EditCommand.Execute(null);

        block.DraftText = "title: no id or run";

        Assert.False(block.CanSave);
        Assert.NotEmpty(block.SaveReason);
        await block.RunCommand.ExecuteAsync(null);
        Assert.True(block.HasNotice);
    }

    [UiThreadFact]
    public async Task AStagingRefusalKeepsTheDraftAndShowsTheReason()
    {
        var harness = new Harness { SaveRefusal = "section is stale" };
        var block = await Shown(harness.Tables(GuideQueryContext.Baseline));
        block.EditCommand.Execute(null);
        block.DraftText = "id: alternates\ntitle: Alternates\nrun: motif query forms";
        Assert.True(block.CanSave, block.SaveReason);

        await block.SaveCommand.ExecuteAsync(null);

        Assert.True(block.IsEditing);
        Assert.Contains("section is stale", block.Notice);
    }

    [UiThreadFact]
    public async Task CancelDropsTheDraft()
    {
        var block = await Shown(new Harness().Tables(GuideQueryContext.Baseline));
        block.EditCommand.Execute(null);
        block.DraftText = "garbage";

        block.CancelCommand.Execute(null);

        Assert.False(block.IsEditing);
        Assert.Equal(block.Body, block.DraftText);
    }

    [UiThreadFact]
    public async Task AProposalContextWithNoTrialShowsPendingOnItsSide()
    {
        var harness = new Harness { Answer = _ => GuideQueryTestSupport.Answer(PendingAnswer()) };
        var block = await Shown(harness.Tables(Proposal));

        Assert.True(block.Displays[0].HasProposalPending);
        Assert.EndsWith("--as-of prop-1 --sides both", harness.Lines[0]);
    }

    [UiThreadFact]
    public async Task SavingARefusedLineWhileARunIsInFlightDoesNotLeaveTheBlockRunning()
    {
        var release = new TaskCompletionSource<CommandOutcome<FormsQueryResponse>>();
        var calls = 0;
        var tables = new GuideQueryTablesViewModel(GuideQueryContext.Baseline, () => "/p",
            (_, _) => ++calls == 1 ? release.Task : Task.FromResult(GuideQueryTestSupport.Answer(BaselineAnswer())), null)
        {
            SaveBody = (_, _) => Task.FromResult<string?>(null),
        };
        tables.Show(GuideQueryDocument.From(SectionLines));
        var block = Assert.Single(tables.Blocks);
        Assert.True(block.IsRunning, "in flight");
        block.EditCommand.Execute(null);
        block.DraftText = "id: alternates\ntitle: Alternates\nrun: motif query forms --as-of baseline";

        Assert.True(block.CanSave, "can save: " + block.SaveReason);
        await block.SaveCommand.ExecuteAsync(null);
        release.SetResult(GuideQueryTestSupport.Answer(BaselineAnswer()));
        await tables.Running;

        Assert.False(block.IsRunning, "still running");
        Assert.Empty(block.Status);
        Assert.True(block.RunCommand.CanExecute(null), "run enabled");
        Assert.True(block.HasNotice, "notice");
    }

    [UiThreadFact]
    public async Task ARefusedRunShowsTheQueryTextReadOnlyNextToTheNotice()
    {
        var lines = SectionLines.Select(line => line.StartsWith("run:") ? "run: motif group forms" : line).ToArray();
        var block = await Shown(new Harness().Tables(GuideQueryContext.Baseline), lines);

        Assert.True(block.HasNotice);
        Assert.True(block.ShowsQueryText);
        Assert.Contains("motif group forms", block.Body);
        block.EditCommand.Execute(null);
        Assert.False(block.ShowsQueryText);
    }

    [UiThreadFact]
    public async Task SectionsWithoutBlocksShowNothing()
    {
        var tables = new Harness().Tables(GuideQueryContext.Baseline);
        tables.Show(GuideQueryDocument.From(["prose"]));
        await tables.Running;

        Assert.False(tables.IsShown);
        tables.Show(null);
        Assert.Empty(tables.Blocks);
    }
}
