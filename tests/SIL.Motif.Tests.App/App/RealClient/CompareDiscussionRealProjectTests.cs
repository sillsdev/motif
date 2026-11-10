using Avalonia.Headless;
using Avalonia.Threading;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.Compare;
using SIL.Motif.App.Preview;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

/// <summary>
/// A Discussion edit that FieldWorks overtook, compared and applied in a project built at run time from
/// <see cref="SeededGdrs"/> (ADR 0076): the window, the real stores and commands, and LibLCM in place of FieldWorks.
/// </summary>
[Trait("MotifTestLevel", "Integration")]
[Collection(LcmCacheTestCollection.Name)]
public sealed class CompareDiscussionRealProjectTests(PristineProjectFixture pristine)
{
    [Fact]
    public void AnEditFieldWorksOvertookIsComparedDoneAndThenAppliesWithBothChanges()
    {
        var path = ProjectWithGdrs();
        var root = RootFor(path);
        var record = Show(path, "GDR-1");
        var id = record.Record.Id;
        var starting = Text(record.Discussion[0]);

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var runner = new InProcessRunnerLauncher(IsolatedRunner.Options(root));
            var store = new GdrEditStore(path, root, runner);
            var compared = new List<CompareWindow>();
            var window = new GdrPreviewWindow([record.Record], shown => GdrQuery.Show(new GdrShowRequest(path, shown)).Value, store,
                () => Task.FromResult(true), compare: compare =>
                {
                    compared.Add(compare);
                    Assert.Empty(compare.Comparison.Undecided);
                    Assert.True(compare.Finish());
                    return Task.FromResult(compare.Outcome);
                });
            using var closer = window.CloseOnExit();
            window.Show();
            window.Show(id);
            await window.Loading;
            window.Field!.Focus();
            window.KeyTextInput("zz ");
            window.Session!.EndSession();
            await Staged(path, id);

            InFieldWorks(path, "GDR-1", (cache, text) =>
            {
                var first = (IStTxtPara)text.ParagraphsOS[0];
                var contents = first.Contents.GetBldr();
                contents.Replace(contents.Length, contents.Length, " fw", TsStringUtils.MakeProps(null, cache.DefaultAnalWs));
                first.Contents = contents.GetString();
            });

            Assert.False(await window.ApplyAsync());
            Assert.Equal(GdrEditStore.RefreshedMessage, window.StatusText);
            Assert.False(await window.ApplyAsync());
            Assert.True(window.IsStaleStripShown);

            Assert.True(await window.CompareAsync());
            await window.Loading;
            Assert.Single(compared);
            Assert.False(window.IsStaleStripShown);

            Assert.True(await window.ApplyAsync(), window.StatusText);
        }, TimeSpan.FromMinutes(4));

        var applied = Show(path, "GDR-1").Discussion[0];
        Assert.Equal("zz " + starting + " fw", Text(applied));
        Assert.Equal("Emphasis", applied.Runs.Single(run => run.Text == "y").Style);
        Assert.Equal(SeededGdrs.HyperlinkData, applied.Runs.Single(run => run.Text == "link").ObjData);
        Assert.Equal(SeededGdrs.ObjectData, applied.Runs.Single(run => run.Text == "￼").ObjData);
    }

    // The window stages on a thread of its own, so the test waits until the Draft holds the edit.
    private static async Task Staged(string path, string id)
    {
        var request = new TextParagraphsFieldRequest(path, id, GdrDiscussionPayload.Field);
        for (var waited = 0; waited < 200; waited++)
        {
            if (ProposalCommands.PendingTextParagraphs(request) is { Succeeded: true, Value.Paragraphs: not null }) return;
            await Task.Delay(50);
        }
        Assert.Fail("The edit never reached the pending changes.");
    }

    private static void InFieldWorks(string path, string abbreviation, Action<LcmCache, IStText> change)
    {
        using var cache = new FwDataProjectLoader().LoadScratchCache(path);
        var record = cache.ServiceLocator.GetInstance<ICmPossibilityRepository>().AllInstances()
            .Single(possibility => possibility.Abbreviation.AnalysisDefaultWritingSystem.Text == abbreviation);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => change(cache, record.DiscussionOA));
        new FwDataProjectLoader().Save(cache);
    }

    private static string Text(GdrParagraph paragraph) => string.Concat(paragraph.Runs.Select(run => run.Text));

    private static GdrShowResponse Show(string path, string id)
    {
        var shown = GdrQuery.Show(new GdrShowRequest(path, id));
        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        return shown.Value!;
    }

    private string ProjectWithGdrs()
    {
        using var cache = pristine.NewScratch();
        SeededGdrs.Seed(cache);
        var path = cache.ProjectId.Path;
        new FwDataProjectLoader().Save(cache);
        Assert.True(BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), RootFor(path)).Succeeded);
        return path;
    }

    private static string RootFor(string projectPath)
    {
        var root = Path.Combine(Path.GetDirectoryName(projectPath)!, "compare-discussion-root");
        Directory.CreateDirectory(root);
        return root;
    }
}
