using SIL.Motif.App.GuideQueries;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>A Guide query run through the product's command client over a project with a captured Baseline.</summary>
[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class GuideQueryEndToEndTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task ABaselineQueryBlockFillsItsTableFromTheRealQueryCommandAndSavesNoRows()
    {
        var path = GroupCorpusFixture.Create(pristine);
        var root = Path.Combine(Path.GetDirectoryName(path)!, "guide-query-managed");
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var client = RealCommandClient.Create(root);

        var tables = new GuideQueryTablesViewModel(GuideQueryContext.Baseline, () => path, client.RunGuideQueryAsync, null);
        tables.Show(GuideQueryDocument.From(
        [
            "```motif-query", "id: alternates", "title: Alternates", "run: motif query forms --where kind=alternate --sort form", "```",
        ]));
        await tables.Running;

        var block = Assert.Single(tables.Blocks);
        Assert.True(block.Notice.Length == 0, block.Notice);
        var display = block.Displays[0];
        Assert.NotEmpty(display.Rows);
        Assert.Contains(display.Rows, row => row.BaselineWord == "kaonan" && row.BaselineState.Length > 0);
        Assert.False(display.HasBaselinePending);
    }

    [Fact]
    public async Task AProposalContextThatHasNoProposalShowsTheCommandsRefusalInTheBlock()
    {
        var path = GroupCorpusFixture.Create(pristine);
        var root = Path.Combine(Path.GetDirectoryName(path)!, "guide-query-refused");
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), root).Succeeded);
        var client = RealCommandClient.Create(root);

        var tables = new GuideQueryTablesViewModel(new GuideQueryContext("no-such-proposal"), () => path, client.RunGuideQueryAsync, null);
        tables.Show(GuideQueryDocument.From(["```motif-query", "id: a", "run: motif query forms", "```"]));
        await tables.Running;

        Assert.NotEmpty(Assert.Single(tables.Blocks).Notice);
    }
}
