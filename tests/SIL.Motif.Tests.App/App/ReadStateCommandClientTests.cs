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
using SIL.Motif.Tests.TestFixtures;
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
    public async Task MarkingReadRefusesWhenNoBaselineExists()
    {
        using var project = new WalkthroughProject(pristine);
        var client = RealCommandClient.Create(project.ManagedRoot, FakeParser.ExecutablePath);

        var outcome = await client.ReadWordStateAsync(new WordReadStateRequest(project.FwDataPath,
            project.Text.TextId, IsRead: true), CancellationToken.None);

        Assert.False(outcome.Succeeded);
    }

    [Fact]
    public async Task WholeTextReadMarksParsedOccurrencesAndReportsThoseWhoseParagraphIsUnparsed()
    {
        using var scenario = await NewScenarioAsync();
        var words = await scenario.Client.ListTextWordsAsync(new TextWordsRequest(scenario.Project.FwDataPath,
            [scenario.Project.Text.TextId]), CancellationToken.None);
        Assert.True(words.Succeeded, words.Refusal?.Message);
        var occurrences = words.Value!.Texts.Single().Lines.SelectMany(line => line.Tokens
            .Where(token => token.WordformId is not null)
            .Select(token => new OccurrenceAnchor(scenario.Project.Text.TextId,
                line.ParagraphId, line.SegmentId, token.OccurrenceIndex))).ToArray();
        Assert.Equal(2, occurrences.Length);
        var unparsed = Assert.Single(occurrences, occurrence =>
            occurrence.ParagraphId != scenario.Occurrence.ParagraphId);

        new FieldWorksSimulator(scenario.Project.FwDataPath).SaveEdit(cache =>
        {
            var text = cache.ServiceLocator.GetInstance<ITextRepository>().GetObject(scenario.Project.Text.TextId);
            var paragraph = Assert.Single(text.ContentsOA!.ParagraphsOS.OfType<IStTxtPara>(),
                candidate => candidate.Guid == unparsed.ParagraphId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => paragraph.ParseIsCurrent = false);
        });
        await CaptureBaselineAsync(scenario);

        var outcome = await scenario.Client.ReadWordStateAsync(new WordReadStateRequest(
            scenario.Project.FwDataPath, scenario.Project.Text.TextId, IsRead: true), CancellationToken.None);

        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        Assert.Single(outcome.Value!.ReadOccurrences);
        using var response = JsonDocument.Parse(JsonSerializer.Serialize(outcome.Value));
        Assert.True(response.RootElement.TryGetProperty("SkippedOccurrences", out var skipped));
        var skippedOccurrences = JsonSerializer.Deserialize<OccurrenceAnchor[]>(skipped.GetRawText());
        Assert.NotNull(skippedOccurrences);
        Assert.Equal([unparsed], skippedOccurrences);
    }

    [Fact]
    public async Task IdenticalAssessmentResultsKeepReadAndChangedResultsClearIt()
    {
        using var scenario = await NewScenarioAsync();
        var initial = await AssessWithFakeParserAsync(scenario, "complete");
        Assert.Equal("analysed", Assert.Single(initial.Words,
            word => word.Word == SeededProject.AnalysedWordForm).Outcome);
        await MarkReadAsync(scenario);

        await AssessWithFakeParserAsync(scenario, "complete");
        var unchanged = await ReadStateAsync(scenario);
        var changedAssessment = await AssessWithFakeParserAsync(scenario, "no-analysis");
        Assert.Equal("no-analysis", Assert.Single(changedAssessment.Words,
            word => word.Word == SeededProject.AnalysedWordForm).Outcome);
        var changed = await ReadStateAsync(scenario);

        Assert.Equal([scenario.Occurrence], unchanged.Value!.ReadOccurrences);
        Assert.Empty(changed.Value!.ReadOccurrences);
    }

    [Fact]
    public async Task AnAssessmentFromAnOlderBaselineDoesNotChangeReadState()
    {
        using var scenario = await NewScenarioAsync();
        await AssessWithFakeParserAsync(scenario, "complete");
        await MarkReadAsync(scenario);

        await CaptureBaselineAsync(scenario);
        var afterRefresh = await ReadStateAsync(scenario);

        Assert.Equal([scenario.Occurrence], afterRefresh.Value!.ReadOccurrences);
    }

    [Fact]
    public async Task AnOccurrenceCountChangeOutsideTheReadSentenceDoesNotClearRead()
    {
        using var scenario = await NewScenarioAsync();
        await AssessWithFakeParserAsync(scenario, "complete", scenario.Project.Text.TextId);
        await MarkReadAsync(scenario);

        new FieldWorksSimulator(scenario.Project.FwDataPath).SaveEdit(cache =>
        {
            var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>()
                .GetObject(scenario.Project.Text.SecondParagraphId);
            var analysis = cache.ServiceLocator.GetInstance<IWfiAnalysisRepository>()
                .GetObject(scenario.Project.Text.ApprovedAnalysisId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                paragraph.Contents = SIL.LCModel.Core.Text.TsStringUtils.MakeString(
                    SeededProject.AnalysedWordForm, cache.DefaultVernWs);
                paragraph.SegmentsOS.Single().AnalysesRS.Clear();
                paragraph.SegmentsOS.Single().AnalysesRS.Add(analysis);
                paragraph.ParseIsCurrent = true;
            });
        });
        var assessment = await AssessWithFakeParserAsync(scenario, "complete", scenario.Project.Text.TextId);
        Assert.Equal(2, Assert.Single(assessment.Words,
            word => word.Word == SeededProject.AnalysedWordForm).OccurrenceCount);
        var afterRefresh = await ReadStateAsync(scenario);

        Assert.Equal([scenario.Occurrence], afterRefresh.Value!.ReadOccurrences);
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
    public async Task InsertingPunctuationBeforeAReadWordRekeysItsOccurrenceAndKeepsItRead()
    {
        using var scenario = await NewScenarioAsync();
        await MarkReadAsync(scenario);

        new FieldWorksSimulator(scenario.Project.FwDataPath).SaveEdit(cache =>
        {
            var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>()
                .GetObject(scenario.Occurrence.ParagraphId);
            var segment = cache.ServiceLocator.GetInstance<ISegmentRepository>()
                .GetObject(scenario.Occurrence.SegmentId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            {
                var punctuation = cache.ServiceLocator.GetInstance<IPunctuationFormFactory>().Create();
                punctuation.Form = SIL.LCModel.Core.Text.TsStringUtils.MakeString(",", cache.DefaultVernWs);
                segment.AnalysesRS.Insert(0, punctuation);
                paragraph.Contents = SIL.LCModel.Core.Text.TsStringUtils.MakeString(
                    $", {SeededProject.AnalysedWordForm}{SeededProject.PunctuationForm}", cache.DefaultVernWs);
                paragraph.ParseIsCurrent = true;
            });
        });
        await CaptureBaselineAsync(scenario);
        var words = await scenario.Client.ListTextWordsAsync(new TextWordsRequest(scenario.Project.FwDataPath,
            [scenario.Project.Text.TextId]), CancellationToken.None);
        Assert.True(words.Succeeded, words.Refusal?.Message);
        var line = Assert.Single(words.Value!.Texts.SelectMany(text => text.Lines), candidate =>
            candidate.ParagraphId == scenario.Occurrence.ParagraphId);
        var moved = Assert.Single(line.Tokens, token => token.WordformId == scenario.Project.Text.AnalysedWordformId);
        Assert.NotEqual(scenario.Occurrence.Index, moved.OccurrenceIndex);
        var movedOccurrence = new OccurrenceAnchor(scenario.Project.Text.TextId, line.ParagraphId,
            line.SegmentId, moved.OccurrenceIndex);

        var afterRefresh = await ReadStateAsync(scenario);

        Assert.Equal([movedOccurrence], afterRefresh.Value!.ReadOccurrences);
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
        var parserPath = FakeParser.CopyRecordingInvocations(project.ManagedRoot);
        var client = RealCommandClient.Create(project.ManagedRoot, parserPath);
        var captured = await client.CaptureBaselineAsync(new BaselineCaptureRequest(project.FwDataPath),
            CancellationToken.None);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        var words = await client.ListTextWordsAsync(new TextWordsRequest(project.FwDataPath,
            [project.Text.TextId]), CancellationToken.None);
        Assert.True(words.Succeeded, words.Refusal?.Message);
        var line = Assert.Single(words.Value!.Texts.SelectMany(text => text.Lines), candidate =>
            candidate.Tokens.Any(token => token.Form == SeededProject.AnalysedWordForm));
        var token = Assert.Single(line.Tokens, candidate => candidate.Form == SeededProject.AnalysedWordForm);
        return new ReadScenario(project, client, parserPath,
            new OccurrenceAnchor(project.Text.TextId, line.ParagraphId, line.SegmentId, token.OccurrenceIndex));
    }

    private static async Task<CommandOutcome<WordReadStateResponse>> MarkReadAsync(ReadScenario scenario) =>
        await scenario.Client.ReadWordStateAsync(new WordReadStateRequest(scenario.Project.FwDataPath,
            scenario.Project.Text.TextId, [scenario.Occurrence], IsRead: true), CancellationToken.None);

    private static async Task<CommandOutcome<WordReadStateResponse>> ReadStateAsync(ReadScenario scenario) =>
        await scenario.Client.ReadWordStateAsync(new WordReadStateRequest(scenario.Project.FwDataPath,
            scenario.Project.Text.TextId), CancellationToken.None);

    private static async Task<AssessCommandResponse> AssessWithFakeParserAsync(ReadScenario scenario, string outcome,
        params Guid[] textIds)
    {
        await CaptureBaselineAsync(scenario);
        FakeParser.BehaveBesideExecutable(scenario.ParserPath, new
        {
            words = new[] { new { word = SeededProject.AnalysedWordForm, outcome } },
        });
        var selection = new SelectionRequest(false,
            textIds.Length == 0 ? [scenario.Project.Text.TextId] : textIds,
            [SeededProject.AnalysedWordForm], false, null);
        var assessed = await scenario.Client.AssessAsync(new AssessRequest(scenario.Project.FwDataPath, selection),
            new Progress<AssessmentProgress>(), CancellationToken.None);
        Assert.True(assessed.Succeeded, assessed.Refusal?.Message);
        Assert.Contains(assessed.Value!.Words, word => word.Word == SeededProject.AnalysedWordForm);
        return assessed.Value;
    }

    private static async Task<BaselineCaptureResponse> CaptureBaselineAsync(ReadScenario scenario)
    {
        var captured = await scenario.Client.CaptureBaselineAsync(
            new BaselineCaptureRequest(scenario.Project.FwDataPath), CancellationToken.None);
        Assert.True(captured.Succeeded, captured.Refusal?.Message);
        return captured.Value!;
    }

    private sealed record ReadScenario(WalkthroughProject Project, CommandClient Client, string ParserPath,
        OccurrenceAnchor Occurrence) : IDisposable
    {
        public void Dispose() => Project.Dispose();
    }
}
