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
[Trait("MotifTestLevel", "Integration")]
public sealed class CommandSeamCoverageTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task ConfigurationSelectionPersistsThroughTheRealClientWithoutAWindow()
    {
        using var project = new WalkthroughProject(pristine);
        var client = RealCommandClient.Create(project.ManagedRoot);
        var baseline = await client.CaptureBaselineAsync(new BaselineCaptureRequest(project.FwDataPath),
            CancellationToken.None);
        Assert.True(baseline.Succeeded, baseline.Refusal?.Message);

        var saved = await client.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
            project.FwDataPath, Path.GetFileNameWithoutExtension(project.FwDataPath), [project.Text.TextId], [],
            PerWordStepLimit: new SIL.Motif.Contract.Assess.StepCap(3_100)), CancellationToken.None);
        Assert.True(saved.Succeeded, saved.Refusal?.Message);

        var reopened = RealCommandClient.Create(project.ManagedRoot);
        var readBack = await reopened.ReadDefaultSelectionAsync(new(project.FwDataPath), CancellationToken.None);
        Assert.True(readBack.Succeeded, readBack.Refusal?.Message);
        Assert.Equal([project.Text.TextId], readBack.Value!.Selection!.TextIds);
        Assert.Equal(3_100, readBack.Value.Selection.PerWordStepLimit!.Steps);
        Assert.False(readBack.Value.SetupSkipped);

        var changed = await reopened.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
            project.FwDataPath, "Configured", [], ["motifb"],
            PerWordStepLimit: new SIL.Motif.Contract.Assess.StepCap(6_600)), CancellationToken.None);
        Assert.True(changed.Succeeded, changed.Refusal?.Message);
        var changedReadBack = await client.ReadDefaultSelectionAsync(new(project.FwDataPath), CancellationToken.None);
        Assert.True(changedReadBack.Succeeded, changedReadBack.Refusal?.Message);
        Assert.Empty(changedReadBack.Value!.Selection!.TextIds);
        Assert.Equal("motifb", Assert.Single(changedReadBack.Value.Selection.AddedWords));
        Assert.Equal(6_600, changedReadBack.Value.Selection.PerWordStepLimit!.Steps);
    }

    [Fact]
    public async Task ScopedActionsPassTheChosenScopeAcrossTheRealClient()
    {
        using var project = new WalkthroughProject(pristine);
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
        var selection = await client.SetDefaultSelectionAsync(new SetDefaultSelectionRequest(
            project.FwDataPath, Path.GetFileNameWithoutExtension(project.FwDataPath), [project.Text.TextId], []),
            CancellationToken.None);
        Assert.True(selection.Succeeded, selection.Refusal?.Message);
        var listed = await client.ListTextWordsAsync(new TextWordsRequest(
            project.FwDataPath, [project.Text.TextId]), CancellationToken.None);
        Assert.True(listed.Succeeded, listed.Refusal?.Message);

        var fake = new FakeCommandClient();
        fake.ReadWordStateCompletesWith(new WordReadStateResponse([], true));
        fake.ListTextWordsCompletesWith(listed.Value!);
        var selectionViewModel = new SelectionViewModel(fake) { AllWordforms = true };
        var texts = new TextWordsViewModel(fake, selectionViewModel);
        var assess = new AssessViewModel(fake, selectionViewModel) { ProjectPath = project.FwDataPath };
        var changes = new ChangesViewModel(client);
        await changes.OpenProjectAsync(project.FwDataPath);
        var inText = new ResultsInTextViewModel(texts, assess, _ => { }, _ => { }, changes, fake);
        await texts.SetProjectAsync(project.FwDataPath);

        Assert.NotNull(inText.SelectedText);
        var expected = inText.SelectedText!.Lines.SelectMany(line => line.Tokens)
            .SelectMany(token => token.Marking.FieldWorksAnalyses)
            .Select(analysis => analysis.StoredAnalysisId).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.NotEmpty(expected);
        Assert.True(inText.RemoveAnalysesCommand.CanExecute(AnalysisOperationScope.SelectedText));
        Assert.False(inText.RemoveAnalysesCommand.CanExecute(AnalysisOperationScope.CheckedWords));

        await inText.RemoveAnalysesCommand.ExecuteAsync(AnalysisOperationScope.SelectedText);

        var actual = changes.Snapshot.Changes
            .Where(change => change.Kind == "remove-analysis")
            .Select(change => change.StoredAnalysisId).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
    }
}
