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
    public async Task RemovalScopeUsesTheRealCommandClientForOnlyItsStoredAnalyses(
        AnalysisOperationScope scope)
    {
        using var project = await OpenProjectAsync();
        var tokens = project.InText.Texts.SelectMany(text => text.Lines).SelectMany(line => line.Tokens)
            .Where(token => token.IsWord).ToArray();
        if (scope == AnalysisOperationScope.CheckedWords)
        {
            var chosen = Assert.Single(tokens, token => token.HasFieldWorksAnalyses);
            chosen.IsSelectedForActions = true;
        }

        var targets = scope switch
        {
            AnalysisOperationScope.SelectedText => project.InText.SelectedText!.Lines
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
            var listed = await client.ListTextWordsAsync(new TextWordsRequest(
                project.FwDataPath, [project.Text.TextId]), CancellationToken.None);
            Assert.True(listed.Succeeded, listed.Refusal?.Message);

            var fake = new FakeCommandClient();
            fake.ReadWordStateCompletesWith(new WordReadStateResponse([], true));
            fake.ListTextWordsCompletesWith(listed.Value!);
            var selection = new SelectionViewModel(fake) { AllWordforms = true };
            var texts = new TextWordsViewModel(fake, selection);
            var assess = new AssessViewModel(fake, selection) { ProjectPath = project.FwDataPath };
            var changes = new ChangesViewModel(client);
            await changes.OpenProjectAsync(project.FwDataPath);
            var inText = new ResultsInTextViewModel(texts, assess, _ => { }, _ => { }, changes, fake);
            await texts.SetProjectAsync(project.FwDataPath);
            return new ScopeProject(project, inText);
        }
        catch
        {
            project.Dispose();
            throw;
        }
    }

    private sealed class ScopeProject(WalkthroughProject project, ResultsInTextViewModel inText) : IDisposable
    {
        public ResultsInTextViewModel InText { get; } = inText;

        public void Dispose() => project.Dispose();
    }
}
