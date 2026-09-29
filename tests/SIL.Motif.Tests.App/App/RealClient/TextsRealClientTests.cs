using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Tests.TestFixtures;
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
        var selection = new SelectionViewModel(project.Client)
        {
            PastedWords = string.Join(Environment.NewLine,
                SeededProject.AnalysedWordForm, SeededProject.FirstForm, SeededProject.SecondForm, "motifextra"),
        };
        var assess = new AssessViewModel(project.Client, selection) { ProjectPath = project.FwDataPath };
        await assess.RunCommand.ExecuteAsync(null);

        Assert.Equal(RunState.Completed, assess.State);
        Assert.Equal(4, assess.Result!.Words.Count);
        var compare = assess.Compare;
        compare.ClearSelectionCommand.Execute(null);
        var allRows = compare.Words.ToArray();
        Assert.Equal(4, allRows.Length);
        var lists = new TextsListsViewModel(compare);
        Assert.Equal(7, lists.Lists.Count);
        Assert.True(lists.Lists.Count(list => list.HasWords) >= 3);

        foreach (var list in lists.Lists)
        {
            var declaredCells = list.Cells.ToHashSet();
            var expected = allRows.Where(row => declaredCells.Contains(new TextsListCell(row.Row, row.Column)))
                .Select(row => row.Word).Order(StringComparer.Ordinal).ToArray();
            lists.SelectListCommand.Execute(list);
            var actual = compare.Words.Select(row => row.Word).Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, actual);
            Assert.Equal(expected.Length, list.WordCount);
        }
    }

    [Fact]
    public async Task WhatChangedComparesTheRunWithTheOneBeforeIt()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        var selection = new SelectionViewModel(project.Client) { PastedWords = SeededProject.FirstForm };
        var assess = new AssessViewModel(project.Client, selection) { ProjectPath = project.FwDataPath };

        project.Behave(new { words = new[] { new { word = SeededProject.FirstForm, outcome = "no-analysis" } } });
        await assess.RunCommand.ExecuteAsync(null);
        Assert.Equal(RunState.Completed, assess.State);
        Assert.Equal("no-analysis", Assert.Single(assess.Result!.Words).Outcome);

        project.Behave(new { words = new[] { new { word = SeededProject.FirstForm, outcome = "complete" } } });
        await assess.RunCommand.ExecuteAsync(null);
        Assert.Equal(RunState.Completed, assess.State);
        Assert.Equal("analysed", Assert.Single(assess.Result!.Words).Outcome);

        project.Behave(new { words = new[] { new { word = SeededProject.FirstForm, outcome = "capped" } } });
        await assess.RunCommand.ExecuteAsync(null);
        Assert.Equal(RunState.Completed, assess.State);
        Assert.True(Assert.Single(assess.Result!.Words).IsIncomplete);
        Assert.Equal(1, assess.Difference.ComparedCount);
        var moved = Assert.Single(assess.Difference.Moves, move => move.Kind != MoveKind.Unchanged);
        Assert.Equal(CompareColumnKind.NoMatch, moved.From.Item2);
        Assert.Equal(CompareColumnKind.Timeout, moved.To.Item2);
        Assert.Equal(3, project.Invocations().Count(invocation => invocation == "batch"));
    }
}
