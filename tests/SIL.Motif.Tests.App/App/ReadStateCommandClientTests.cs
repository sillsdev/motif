using System.Text.Json;
using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using SIL.LCModel;
using SIL.LCModel.Infrastructure;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheTestCollection.Name)]
public sealed class ReadStateCommandClientTests(PristineProjectFixture pristine)
{
    [Fact]
    public async Task AReadOccurrenceRoundTripsThroughTheCommandClientAndProjectStore()
    {
        using var scenario = await NewScenarioAsync();

        var marked = await MarkReadAsync(scenario);
        var reloaded = await ReadStateAsync(scenario);

        Assert.True(marked.Succeeded, marked.Refusal?.Message);
        Assert.True(reloaded.Succeeded, reloaded.Refusal?.Message);
        Assert.Equal([scenario.Occurrence], reloaded.Value!.ReadOccurrences);
    }

    [Fact]
    public async Task WholeTextReadAndUnreadChangesEveryWordOccurrence()
    {
        using var scenario = await NewScenarioAsync();

        var read = await scenario.Client.ReadWordStateAsync(new WordReadStateRequest(
            scenario.Project.FwDataPath, scenario.Project.Text.TextId, IsRead: true), CancellationToken.None);
        var unread = await scenario.Client.ReadWordStateAsync(new WordReadStateRequest(
            scenario.Project.FwDataPath, scenario.Project.Text.TextId, IsRead: false), CancellationToken.None);

        Assert.True(read.Succeeded, read.Refusal?.Message);
        Assert.Equal(2, read.Value!.ReadOccurrences.Count);
        Assert.True(unread.Succeeded, unread.Refusal?.Message);
        Assert.Empty(unread.Value!.ReadOccurrences);
    }

    [Fact]
    public async Task IdenticalAssessmentResultsKeepReadAndChangedResultsClearIt()
    {
        using var scenario = await NewScenarioAsync();
        RecordAssessment(scenario, "assessment/read/one", "analysed", DateTimeOffset.UtcNow);
        await MarkReadAsync(scenario);

        RecordAssessment(scenario, "assessment/read/two", "analysed", DateTimeOffset.UtcNow.AddMinutes(1));
        var unchanged = await ReadStateAsync(scenario);
        RecordAssessment(scenario, "assessment/read/three", "timed-out", DateTimeOffset.UtcNow.AddMinutes(2));
        var changed = await ReadStateAsync(scenario);

        Assert.Equal([scenario.Occurrence], unchanged.Value!.ReadOccurrences);
        Assert.Empty(changed.Value!.ReadOccurrences);
    }

    [Fact]
    public async Task ARefreshedSentenceChangeClearsReadForItsOccurrence()
    {
        using var scenario = await NewScenarioAsync();
        await MarkReadAsync(scenario);

        new FieldWorksSimulator(scenario.Project.FwDataPath).SaveEdit(cache =>
        {
            var text = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(scenario.Project.Text.TextId);
            var first = text.ContentsOA!.ParagraphsOS.OfType<IStTxtPara>().First();
            var addedWord = cache.ServiceLocator.GetInstance<IWfiWordformRepository>()
                .GetObject(scenario.Project.Text.UnanalysedWordformId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                first.Contents = SIL.LCModel.Core.Text.TsStringUtils.MakeString(
                    $"{SeededProject.AnalysedWordForm} {SeededProject.UnanalysedWordForm}{SeededProject.PunctuationForm}",
                    cache.DefaultVernWs);
                first.SegmentsOS.Single().AnalysesRS.Insert(1, addedWord);
                first.ParseIsCurrent = true;
            });
        });
        await CaptureBaselineAsync(scenario);

        var afterRefresh = await ReadStateAsync(scenario);

        Assert.Empty(afterRefresh.Value!.ReadOccurrences);
    }

    [Fact]
    public async Task ARefreshedFieldWorksAnalysisChangeClearsRead()
    {
        using var scenario = await NewScenarioAsync();
        await MarkReadAsync(scenario);

        new FieldWorksSimulator(scenario.Project.FwDataPath).SaveEdit(cache =>
        {
            var wordform = cache.ServiceLocator.GetInstance<IWfiWordformRepository>()
                .GetObject(scenario.Project.Text.AnalysedWordformId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                wordform.AnalysesOC.Add(cache.ServiceLocator.GetInstance<IWfiAnalysisFactory>().Create()));
        });
        await CaptureBaselineAsync(scenario);

        var afterRefresh = await ReadStateAsync(scenario);

        Assert.Empty(afterRefresh.Value!.ReadOccurrences);
    }

    [Fact]
    public async Task ARefreshedFieldWorksOpinionChangeClearsRead()
    {
        using var scenario = await NewScenarioAsync();
        await MarkReadAsync(scenario);

        new FieldWorksSimulator(scenario.Project.FwDataPath).SaveEdit(cache =>
        {
            var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>()
                .GetObject(scenario.Project.Text.ApprovedAnalysisId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
                cache.LangProject.DefaultUserAgent.SetEvaluation(analysis, Opinions.disapproves));
        });
        await CaptureBaselineAsync(scenario);

        var afterRefresh = await ReadStateAsync(scenario);

        Assert.Empty(afterRefresh.Value!.ReadOccurrences);
    }

    private async Task<ReadScenario> NewScenarioAsync()
    {
        var project = new WalkthroughProject(pristine);
        new FieldWorksSimulator(project.FwDataPath).SaveEdit(cache =>
        {
            var text = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(project.Text.TextId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                foreach (var paragraph in text.ContentsOA!.ParagraphsOS.OfType<IStTxtPara>())
                    paragraph.ParseIsCurrent = true;
            });
        });
        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);
        var captured = await client.CaptureBaselineAsync(new BaselineCaptureRequest(project.FwDataPath),
            CancellationToken.None);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var words = await client.ListTextWordsAsync(new TextWordsRequest(project.FwDataPath,
            [project.Text.TextId]), CancellationToken.None);
        Assert.True(words.Succeeded, words.Refusal?.Message);
        var line = Assert.Single(words.Value!.Texts.SelectMany(text => text.Lines), candidate =>
            candidate.Tokens.Any(token => token.Form == SeededProject.AnalysedWordForm));
        var token = Assert.Single(line.Tokens, candidate => candidate.Form == SeededProject.AnalysedWordForm);
        return new ReadScenario(project, client, captured.Value!.Token,
            new OccurrenceAnchor(project.Text.TextId, line.ParagraphId, line.SegmentId, token.OccurrenceIndex));
    }

    private static async Task<CommandOutcome<WordReadStateResponse>> MarkReadAsync(ReadScenario scenario) =>
        await scenario.Client.ReadWordStateAsync(new WordReadStateRequest(scenario.Project.FwDataPath,
            scenario.Project.Text.TextId, [scenario.Occurrence], IsRead: true), CancellationToken.None);

    private static async Task<CommandOutcome<WordReadStateResponse>> ReadStateAsync(ReadScenario scenario) =>
        await scenario.Client.ReadWordStateAsync(new WordReadStateRequest(scenario.Project.FwDataPath,
            scenario.Project.Text.TextId), CancellationToken.None);

    private static async Task CaptureBaselineAsync(ReadScenario scenario)
    {
        var captured = await scenario.Client.CaptureBaselineAsync(
            new BaselineCaptureRequest(scenario.Project.FwDataPath), CancellationToken.None);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
    }

    private static void RecordAssessment(ReadScenario scenario, string id, string outcome, DateTimeOffset saved)
    {
        var fullPath = Path.GetFullPath(scenario.Project.FwDataPath);
        var project = new ProjectLocator(fullPath, Path.GetFileNameWithoutExtension(fullPath));
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var form = SeededProject.AnalysedWordForm;
        new AssessmentRepository(database).Record(new NewAssessmentRecord(
            id, null, null, "test", AssessmentKind.ParseTime.ToStoredKind(), "{}", "sha256:scope",
            "whitespace-and-punctuation", "1", JsonSerializer.Serialize(scenario.BaselineToken,
                MotifJson.CreateOptions()), Selection.Create("read-state", [form]), "sha256:outcome",
            "sha256:semantic", "sha256:grammar", "test-model", "test-pipeline", 0,
            [new AssessedWord(form, outcome, [new ParsedAnalysis(null, [], 0, "sha256:reading")])],
            SavedUtc: saved.ToString("O")));
    }

    private sealed record ReadScenario(WalkthroughProject Project, CommandClient Client,
        BaselineToken BaselineToken, OccurrenceAnchor Occurrence) : IDisposable
    {
        public void Dispose() => Project.Dispose();
    }
}
