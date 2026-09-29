using Avalonia.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
public sealed class TextsRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task EachListHoldsTheWordsItsQuestionNames()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        project.Behave(new { words = new[]
        {
            new { word = SeededProject.AnalysedWordForm, outcome = "no-analysis" },
            new { word = SeededProject.FirstForm, outcome = "complete" },
            new { word = SeededProject.SecondForm, outcome = "no-analysis" },
            new { word = "motifextra", outcome = "capped" },
        } });
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: project.ParserPath);
            var workspace = walkthrough.Workspace;
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
                ["Approved, not parsed"] = [SeededProject.AnalysedWordForm],
                ["Approved, parsed differently"] = [],
                ["Candidate the parser confirms"] = [],
                ["Parsed, not in the project"] = [SeededProject.FirstForm],
                ["Nobody can analyze"] = [SeededProject.SecondForm],
                ["Rejected but rebuilt"] = [],
                ["Timed out"] = ["motifextra"],
            };
            var lists = page.TextsLists;
            Assert.Equal(7, lists.Lists.Count);
            Assert.Equal(4, lists.Lists.Count(list => list.HasWords));

            foreach (var list in lists.Lists)
            {
                var expected = expectedWords[list.Name].Order(StringComparer.Ordinal).ToArray();
                lists.SelectListCommand.Execute(list);
                var actual = compare.Words.Select(row => row.Word).Order(StringComparer.Ordinal).ToArray();
                Assert.Equal(expected, actual);
                Assert.Equal(expected.Length, list.WordCount);
            }

        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task WhatChangedComparesTheRunWithTheOneBeforeIt()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: project.ParserPath);
            var workspace = walkthrough.Workspace;
            var page = workspace.PageModel<TextsPageModel>();
            workspace.Selection.PastedWords = SeededProject.FirstForm;
            workspace.Assess.ProjectPath = project.FwDataPath;

            project.Behave(new { words = new[] { new { word = SeededProject.FirstForm, outcome = "no-analysis" } } });
            await workspace.Assess.RunCommand.ExecuteAsync(null);
            Assert.Equal(RunState.Completed, workspace.Assess.State);
            Assert.Equal("no-analysis", Assert.Single(workspace.Assess.Result!.Words).Outcome);

            project.Behave(new { words = new[] { new { word = SeededProject.FirstForm, outcome = "complete" } } });
            await workspace.Assess.RunCommand.ExecuteAsync(null);
            Assert.Equal(RunState.Completed, workspace.Assess.State);
            Assert.Equal("analysed", Assert.Single(workspace.Assess.Result!.Words).Outcome);

            project.Behave(new { words = new[] { new { word = SeededProject.FirstForm, outcome = "capped" } } });
            await workspace.Assess.RunCommand.ExecuteAsync(null);
            Assert.Equal(RunState.Completed, workspace.Assess.State);
            Assert.True(Assert.Single(workspace.Assess.Result!.Words).IsIncomplete);
            Assert.Equal(1, workspace.Assess.Difference.ComparedCount);
            var moved = Assert.Single(workspace.Assess.Difference.Moves, move => move.Kind != MoveKind.Unchanged);
            Assert.Equal(CompareColumnKind.NoMatch, moved.From.Item2);
            Assert.Equal(CompareColumnKind.Timeout, moved.To.Item2);
            Assert.Equal(3, project.Invocations().Count(invocation => invocation == "batch"));

            Assert.Same(workspace.Assess.Compare, page.Assess.Compare);
        }, TimeSpan.FromMinutes(1));
    }
}
