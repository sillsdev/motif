using Avalonia.Controls;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App.RealClient;

[Collection(LcmCacheTestCollection.Name)]
public sealed class TextsRealClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task AParserBuiltApprovedReadingIsShownAsSameWithAnApprovedMark()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        project.Behave(new { words = new[]
        {
            new { word = SeededProject.AnalysedWordForm, outcome = "no-analysis" },
        } });
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: project.ParserPath);
            var workspace = walkthrough.Workspace;
            workspace.Selection.PastedWords = SeededProject.AnalysedWordForm;
            workspace.Assess.ProjectPath = project.FwDataPath;
            await workspace.Assess.RunCommand.ExecuteAsync(null);

            var expected = Assert.Single(Assert.Single(workspace.Assess.Result!.Words)
                .Correctness!.Expectations).Morphs;
            project.Behave(new { words = new[]
            {
                new
                {
                    word = SeededProject.AnalysedWordForm,
                    outcome = "complete",
                    analyses = new[]
                    {
                        new
                        {
                            morphs = expected.Select(morph => new
                            {
                                form = morph.Form,
                                msa = morph.Msa,
                                inflType = morph.InflType,
                                guessedString = (string?)null,
                            }).ToArray(),
                        },
                    },
                },
            } });
            await workspace.Assess.RunCommand.ExecuteAsync(null);

            var compare = workspace.PageModel<TextsPageModel>().Assess.Compare;
            compare.ClearSelectionCommand.Execute(null);
            var word = Assert.Single(compare.Words);
            Assert.Equal(AnalysisMarkingClass.Same, word.Marking.PanGlossClass);
            Assert.Equal([OpinionMarkKind.Approved], word.OpinionMarks.Select(mark => mark.Kind));
        }, TimeSpan.FromMinutes(1));
    }
    [Fact]
    public async Task CompactCellKeepsEveryStoredOpinionFromTheProject()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances()
                    .Single(candidate => candidate.Form.VernacularDefaultWritingSystem?.Text ==
                        SeededProject.AnalysedWordForm);
                var template = wordform.AnalysesOC.Single().MorphBundlesOS[0];
                foreach (var opinion in new[] { Opinions.noopinion, Opinions.disapproves })
                {
                    var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create();
                    wordform.AnalysesOC.Add(analysis);
                    var bundle = cache.ServiceLocator.GetInstance<IWfiMorphBundleFactory>().Create();
                    analysis.MorphBundlesOS.Add(bundle);
                    bundle.MorphRA = template.MorphRA;
                    bundle.MsaRA = template.MsaRA;
                    bundle.SenseRA = template.SenseRA;
                    cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, opinion);
                }
            }));
        var baseline = await project.Client.CaptureBaselineAsync(
            new BaselineCaptureRequest(project.FwDataPath), CancellationToken.None);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
        project.Behave(new { words = new[]
        {
            new { word = SeededProject.AnalysedWordForm, outcome = "no-analysis" },
        } });
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: project.ParserPath);
            var workspace = walkthrough.Workspace;
            workspace.Selection.PastedWords = SeededProject.AnalysedWordForm;
            workspace.Assess.ProjectPath = project.FwDataPath;
            await workspace.Assess.RunCommand.ExecuteAsync(null);

            var compare = workspace.PageModel<TextsPageModel>().Assess.Compare;
            compare.ClearSelectionCommand.Execute(null);
            var word = Assert.Single(compare.Words);
            Assert.Equal(3, word.OpinionMarks.Count);
            Assert.Contains(word.OpinionMarks, mark => mark.Kind == OpinionMarkKind.Approved);
            Assert.Contains(word.OpinionMarks, mark => mark.Kind == OpinionMarkKind.Unknown);
            Assert.Contains(word.OpinionMarks, mark => mark.Kind == OpinionMarkKind.Disapproved);
        }, TimeSpan.FromMinutes(1));
    }

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

            lists.SelectListCommand.Execute(lists.Lists.Single(list => list.Name == "Approved, not parsed"));
            var approvedNoParse = Assert.Single(compare.Words);
            Assert.Equal(AnalysisMarkingClass.None, approvedNoParse.Marking.PanGlossClass);
            Assert.Equal([OpinionMarkKind.Approved], approvedNoParse.OpinionMarks.Select(mark => mark.Kind));

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
