using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheTestCollection.Name)]
public sealed class AnalysisOperationScopesTests(PristineProjectFixture pristine)
{
    [Theory]
    [InlineData(AnalysisOperationScope.SelectedText)]
    [InlineData(AnalysisOperationScope.ChosenTexts)]
    [InlineData(AnalysisOperationScope.CheckedWords)]
    public void RemovalScopeUsesTheRealCommandClientForOnlyItsStoredAnalyses(
        AnalysisOperationScope scope)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await using var project = await OpenProjectAsync();
            var tokens = SelectionModelFixture.VisibleLines(project.InText).SelectMany(line => line.Tokens)
                .Where(token => token.IsWord).ToArray();
            if (scope == AnalysisOperationScope.CheckedWords)
            {
                var chosen = Assert.Single(tokens, token => token.HasFieldWorksAnalyses);
                chosen.IsSelectedForActions = true;
            }

            var targets = scope switch
            {
                AnalysisOperationScope.SelectedText => SelectionModelFixture.VisibleLines(project.InText)
                    .SelectMany(line => line.Tokens),
                AnalysisOperationScope.ChosenTexts => tokens,
                _ => tokens.Where(token => token.IsSelectedForActions),
            };
            var expected = targets.SelectMany(token => token.Marking.FieldWorksAnalyses)
                .Select(analysis => analysis.StoredAnalysisId).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
            Assert.NotEmpty(expected);

            await project.InText.RemoveAnalysesCommand.ExecuteAsync(scope);

            var actual = project.InText.Changes.Snapshot.Changes
                .Where(change => change.Kind == "remove-analysis")
                .Select(change => change.StoredAnalysisId).Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal).ToArray();
            Assert.Equal(expected, actual);
        }, TimeSpan.FromSeconds(120));
    }

    private async Task<ScopeProject> OpenProjectAsync()
    {
        var project = new WalkthroughProject(pristine);
        try
        {
            new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
            {
                var text = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(project.Text.TextId);
                NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                {
                    foreach (var paragraph in text.ContentsOA!.ParagraphsOS.OfType<IStTxtPara>())
                        paragraph.ParseIsCurrent = true;
                });
            });

            var client = RealCommandClient.Create(project.ManagedRoot);
            var baseline = await client.CaptureBaselineAsync(new BaselineCaptureRequest(project.FwDataPath),
                CancellationToken.None);
            Assert.True(baseline.Succeeded, baseline.Refusal?.Message);
            var selected = await client.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
                project.FwDataPath, Path.GetFileNameWithoutExtension(project.FwDataPath),
                [project.Text.TextId], []), CancellationToken.None);
            Assert.True(selected.Succeeded, selected.Refusal?.Message);
            var reads = new WorkspaceSelection(client);
            await reads.ReloadAsync(project.FwDataPath, [project.Text.TextId], []);

            var fake = new FakeCommandClient();
            fake.ReadWordStateCompletesWith(new WordReadStateResponse([], true));
            var selection = new SelectionViewModel(fake) { AllWordforms = true };
            var texts = new TextWordsViewModel(fake, selection, reads);
            var assess = new AssessViewModel(fake, selection) { ProjectPath = project.FwDataPath };
            var changes = new ChangesViewModel(client);
            await changes.OpenProjectAsync(project.FwDataPath);
            var inText = new ResultsInTextViewModel(texts, assess, _ => { }, _ => { }, changes, fake, reads);
            await texts.SetProjectAsync(project.FwDataPath);
            await SelectionModelFixture.RealizeAsync(inText);
            return new ScopeProject(project, inText, texts, reads);
        }
        catch
        {
            project.Dispose();
            throw;
        }
    }

    private sealed class ScopeProject(WalkthroughProject project, ResultsInTextViewModel inText,
        TextWordsViewModel words, WorkspaceSelection reads) : IAsyncDisposable
    {
        public ResultsInTextViewModel InText { get; } = inText;

        public async ValueTask DisposeAsync()
        {
            await InText.StopAsync();
            await words.StopAsync();
            await reads.StopAsync();
            project.Dispose();
        }
    }
}
