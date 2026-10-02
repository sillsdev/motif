using Avalonia.Input;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class TextsListMembershipRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task EachListHoldsTheWordsItsQuestionNames()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        project.Behave(new { words = new object[]
        {
            new { word = SeededProject.AnalysedWordForm, outcome = "no-analysis" },
            ParsedFirstForm,
            new { word = SeededProject.SecondForm, outcome = "no-analysis" },
            new { word = "motifextra", outcome = "capped" },
        } });
        await using var workspace = CreateWorkspace(project.Client);
        await workspace.Context.OpenProjectAsync(project.FwDataPath);
        var page = workspace.PageModel<TextsPageModel>();
        workspace.Selection.PastedWords = string.Join(Environment.NewLine,
            SeededProject.AnalysedWordForm, SeededProject.FirstForm, SeededProject.SecondForm, "motifextra");
        workspace.Assess.ProjectPath = project.FwDataPath;
        await workspace.Assess.RunCommand.ExecuteAsync(null);

        Assert.Equal(RunState.Completed, workspace.Assess.State);
        Assert.Equal(4, workspace.Assess.Result!.Words.Count);
        var compare = page.Assess.Compare;
        compare.ClearSelectionCommand.Execute(null);
        Assert.Equal(4, compare.Words.Count);

        var expectedWords = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Lost"] = [SeededProject.AnalysedWordForm],
            ["Built something else"] = [],
            ["Built anyway"] = [],
            ["Have a look"] = [],
            ["New: PanGloss proposes"] = [SeededProject.FirstForm],
            ["Nobody can analyze"] = [SeededProject.SecondForm],
            ["Stopped"] = ["motifextra"],
            ["Not parsed"] = [],
        };
        var lists = page.TextsLists;
        Assert.Equal(8, lists.Lists.Count);
        Assert.Equal(4, lists.Lists.Count(list => list.HasWords));

        foreach (var list in lists.Lists)
        {
            var expected = expectedWords[list.Name].Order(StringComparer.Ordinal).ToArray();
            lists.SelectListCommand.Execute(list);
            var actual = compare.Words.Select(row => row.Word).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Length, list.WordCount);
        }

        lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Lost"));
        var approvedNoParse = Assert.Single(compare.Words);
        Assert.Equal(AnalysisMarkingClass.None, approvedNoParse.Marking.PanGlossClass);
        Assert.Equal([OpinionMarkKind.Approved], approvedNoParse.OpinionMarks.Select(mark => mark.Kind));
    }

    private static object ParsedFirstForm => new
    {
        word = SeededProject.FirstForm,
        outcome = "complete",
        analyses = new[] { new { morphs = new[]
        {
            new { form = "11111111-1111-1111-1111-111111111111", msa = "22222222-2222-2222-2222-222222222222",
                inflType = (string?)null, guessedString = (string?)null },
        } } },
    };

    private static WorkspaceShellViewModel CreateWorkspace(ICommandClient client)
    {
        var selection = new SelectionViewModel(client);
        return new WorkspaceShellViewModel(new ProjectViewModel(client, new ProjectPicker()),
            new BaselineViewModel(client), selection, new AssessViewModel(client, selection),
            new FolderPicker(), new DragSource(), client);
    }

    private sealed class ProjectPicker : IProjectPicker
    {
        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class FolderPicker : IHandoffFolderPicker
    {
        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);
    }

    private sealed class DragSource : IFileDragSource
    {
        public Task<DragDropEffects> StartDragAsync(
            PointerPressedEventArgs trigger, IReadOnlyList<string> filePaths, DragDropEffects allowedEffects) =>
            Task.FromResult(DragDropEffects.None);
    }
}
