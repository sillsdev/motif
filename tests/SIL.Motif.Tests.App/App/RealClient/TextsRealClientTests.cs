using System.Reflection;
using Avalonia.Controls;
using SIL.LCModel;
using SIL.LCModel.DomainServices;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Services;
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
    [Fact]
    public async Task BeforeTheFirstParseAnalyzeTextsShowsTheTextAndItsFieldWorksAnalysesWithoutReadState()
    {
        using var project = new WalkthroughProject(pristine);
        var real = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);
        var captured = await real.CaptureBaselineAsync(new BaselineCaptureRequest(project.FwDataPath),
            CancellationToken.None);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var client = CountingCommandClient.Around(real);
        var selection = new SelectionViewModel(client);
        await selection.SetProjectAsync(project.FwDataPath);
        var texts = new TextWordsViewModel(client, selection);
        var assess = new AssessViewModel(client, selection) { ProjectPath = project.FwDataPath };
        var inText = new ResultsInTextViewModel(texts, assess, _ => { }, _ => { }, new ChangesViewModel(client), client);
        Assert.Single(selection.Texts, text => text.Title == SeededProject.TextTitle).IsChecked = true;
        await texts.SetProjectAsync(project.FwDataPath);
        await texts.ReloadAsync();
        await inText.ReadStateRefresh;

        Assert.False(inText.HasAssessment);
        Assert.True(inText.HasLines);
        Assert.Null(inText.Message);
        var word = inText.VisibleLines.SelectMany(line => line.Tokens)
            .Single(token => token.Form == SeededProject.AnalysedWordForm);
        Assert.Equal([SeededProject.FirstGloss, SeededProject.SecondGloss],
            word.PrimaryFieldWorksMorphs.Select(morph => morph.GlossOrPlaceholder));
        Assert.Equal("Not parsed yet", word.PanGlossSummary);
        Assert.False(word.ShowUnread);
        Assert.Equal(0, CountingCommandClient.CallsTo(client, nameof(ICommandClient.ReadWordStateAsync)));
    }

    /// <summary>Passes every call to the real client and counts them by method name.</summary>
    public class CountingCommandClient : DispatchProxy
    {
        private readonly Dictionary<string, int> _calls = new(StringComparer.Ordinal);
        private ICommandClient? _inner;

        internal static ICommandClient Around(ICommandClient inner)
        {
            var proxy = Create<ICommandClient, CountingCommandClient>();
            ((CountingCommandClient)(object)proxy)._inner = inner;
            return proxy;
        }

        internal static int CallsTo(ICommandClient proxy, string method) =>
            ((CountingCommandClient)(object)proxy)._calls.GetValueOrDefault(method);

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            _calls[targetMethod.Name] = _calls.GetValueOrDefault(targetMethod.Name) + 1;
            try
            {
                return targetMethod.Invoke(_inner, args);
            }
            catch (TargetInvocationException invocation) when (invocation.InnerException is not null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(invocation.InnerException).Throw();
                throw;
            }
        }
    }

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
        project.Behave(new { words = new object[]
        {
            new { word = SeededProject.AnalysedWordForm, outcome = "no-analysis" },
            ParsedFirstForm,
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
                ["Unknown the parser confirms"] = [],
                ["Parsed, not in FieldWorks"] = [SeededProject.FirstForm],
                ["Nobody can analyze"] = [SeededProject.SecondForm],
                ["Disapproved but built"] = [],
                ["Stopped at a limit"] = ["motifextra"],
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
    public async Task ListsOpenOnAListWithWordsWhenTheFirstListIsEmpty()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        project.Behave(new { words = new[]
        {
            new { word = SeededProject.FirstForm, outcome = "complete" },
            new { word = SeededProject.SecondForm, outcome = "no-analysis" },
        } });
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: project.ParserPath);
            var workspace = walkthrough.Workspace;
            var page = workspace.PageModel<TextsPageModel>();
            workspace.Selection.PastedWords = string.Join(Environment.NewLine,
                SeededProject.FirstForm, SeededProject.SecondForm);
            workspace.Assess.ProjectPath = project.FwDataPath;
            await workspace.Assess.RunCommand.ExecuteAsync(null);
            Assert.Equal(RunState.Completed, workspace.Assess.State);

            page.Tab = TextsTab.Lists;

            var lists = page.TextsLists;
            Assert.False(lists.Lists[0].HasWords);
            Assert.Equal("Parsed, not in FieldWorks", lists.SelectedList?.Name);
            Assert.Equal([SeededProject.FirstForm], page.Assess.Compare.Words.Select(word => word.Word));
        }, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task MatrixBulkActionsReachReviewChanges()
    {
        using var project = await GrammarClientProject.OpenAsync(pristine);
        project.Behave(new { words = new[] { new { word = SeededProject.AnalysedWordForm, outcome = "no-analysis" } } });
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var walkthrough = new WalkthroughWindow(
                project.ManagedRoot, project.FwDataPath, parserPath: project.ParserPath);
            var workspace = walkthrough.Workspace;
            await workspace.Context.OpenProjectAsync(project.FwDataPath);
            workspace.Selection.PastedWords = SeededProject.AnalysedWordForm;
            workspace.Assess.ProjectPath = project.FwDataPath;
            await workspace.Assess.RunCommand.ExecuteAsync(null);
            // The parser builds only the first of the stored analysis's two morphs: a reading FieldWorks lacks.
            var stored = Assert.Single(Assert.Single(workspace.Assess.Result!.Words).Correctness!.Expectations).Morphs;
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
                            morphs = stored.Take(1).Select(morph => new
                            {
                                form = morph.Form, msa = morph.Msa, inflType = morph.InflType,
                                guessedString = (string?)null,
                            }).ToArray(),
                        },
                    },
                },
            } });
            await workspace.Assess.RunCommand.ExecuteAsync(null);
            Assert.Equal(RunState.Completed, workspace.Assess.State);
            var compare = workspace.PageModel<TextsPageModel>().Assess.Compare;
            compare.ClearSelectionCommand.Execute(null);
            var word = Assert.Single(compare.Words);

            word.IsChecked = true;
            Assert.True(compare.ProposeCommand.CanExecute(ChangeKinds.AddCandidate));
            await compare.ProposeCommand.ExecuteAsync(ChangeKinds.AddCandidate);
            Assert.Null(compare.Changes.LastRefusal);
            word.IsChecked = true;
            await compare.ProposeCommand.ExecuteAsync(ChangeKinds.IncorrectSpelling);

            Assert.Null(compare.Changes.LastRefusal);
            var reviewed = workspace.PageModel<ReviewPageModel>().ReviewGroups.SelectMany(group => group.Items).ToArray();
            Assert.All(reviewed, change => Assert.Equal(SeededProject.AnalysedWordForm, change.Word));
            Assert.Contains(reviewed, change => change.Kind == ChangeKinds.AddCandidate);
            Assert.Contains(reviewed, change => change.Kind == ChangeKinds.IncorrectSpelling);
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

            project.Behave(new { words = new[] { ParsedFirstForm } });
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
