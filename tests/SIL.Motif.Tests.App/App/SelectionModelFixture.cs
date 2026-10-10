using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Services;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App;

/// <summary>Publishes authored current-format records and owns their real reader for an App model test.</summary>
internal sealed class SelectionModelFixture(FakeCommandClient client) : IAsyncDisposable
{
    private StoredSelectionFixture? _store;
    private MotifDatabase? _pausedDatabase;
    private AssessCommandResponse? _assessment;
    internal Action<SelectionReaderPausePoint>? PauseAt { get; set; }
    internal TextWordsResponse? Source { get; private set; }
    public WorkspaceSelection Reads => client.ReaderOwner;

    public async Task PublishAsync(TextWordsResponse source, AssessCommandResponse? assessment = null,
        IReadOnlyList<Guid>? textIds = null, IReadOnlyList<string>? addedWords = null, BaselineToken? baseline = null,
        IReadOnlyList<OccurrenceAnchor>? readOccurrences = null)
    {
        await Reads.StopAsync();
        _pausedDatabase?.Dispose();
        _pausedDatabase = null;
        _store?.Dispose();
        Source = source;
        _assessment = assessment;
        _store = StoredSelectionFixture.FromDisplayRecords(source, assessment?.Baseline.Token ?? baseline);
        if (assessment is not null) _store.RecordAssessment(assessment);
        if (readOccurrences is { Count: > 0 })
            foreach (var group in readOccurrences.GroupBy(anchor => anchor.TextId))
            {
                var marked = WriteReadState(new WordReadStateRequest(_store.ProjectPath, group.Key, group.ToArray(), true)
                { AssessmentIds = assessment is null ? [] : new[] { assessment.AssessmentIds.First() }.Concat(
                    assessment.TimingOverrideAssessmentIds).ToArray() });
                Xunit.Assert.True(marked.Succeeded, marked.Refusal?.Message);
            }
        if (PauseAt is not null && assessment is not null)
        {
            var project = new ProjectLocator(_store.ProjectPath, "project");
            _pausedDatabase = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);
            client.SelectionReaderHandler = (request, cancellation) => SelectionReader.OpenAsync(_pausedDatabase,
                new SelectionReadRequest(ProjectWorkspaceKey.Compute(project), request.TextIds, request.AddedWords)
                { ProjectPath = _store.ProjectPath, ShownAssessment = request.ShownAssessment, PauseAt = PauseAt }, cancellation);
        }
        else client.SelectionReaderHandler = _store.OpenAsync;
        var root = assessment?.Measurements.LastOrDefault(item => item.Kind == AssessmentKinds.ParseTime)?.AssessmentId
            ?? assessment?.AssessmentIds.FirstOrDefault();
        await Reads.ReloadAsync(_store.ProjectPath, textIds ?? source.Texts.Select(text => text.TextId).ToArray(),
            addedWords ?? [], shownAssessment: root is null ? null : new SelectionAssessmentEvidence(
                assessment!.Baseline.Token, root, assessment.TimingOverrideAssessmentIds,
                assessment.Measurements.Where(item => item.Kind is AssessmentKinds.Correctness or AssessmentKinds.ObjectTiming)
                    .GroupBy(item => item.Kind, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Select(group => group.Last().AssessmentId).ToArray()));
        Xunit.Assert.Null(Reads.Refusal);
        Xunit.Assert.NotNull(Reads.Summary);
    }

    public static TextWordsResponse WithOccurrenceTexts(TextWordsResponse source)
    {
        if (source.Texts.Count > 0) return source;
        var words = source.Words.Select(word =>
        {
            var approved = word.Approved.Select(analysis => analysis with { StoredAnalysisOpinion = ReadingGrade.Approved }).ToArray();
            var rejected = word.Disapproved.Select(analysis => analysis with { StoredAnalysisOpinion = ReadingGrade.Disapproved }).ToArray();
            var all = approved.Concat(rejected).Concat(word.Analyses).DistinctBy(analysis => analysis.Key).ToList();
            for (var index = 0; index < word.CandidateCount; index++)
                all.Add(new ProjectAnalysis($"candidate-{word.Form}-{index}", []) { StoredAnalysisOpinion = ReadingGrade.NoOpinion });
            var occurrences = word.Occurrences.Select(occurrence => occurrence with { Analysis = occurrence.Analysis is { } chosen ?
                all.FirstOrDefault(analysis => analysis.Key == chosen.Key) ?? chosen with
                { StoredAnalysisOpinion = occurrence.Status == "approved" ? ReadingGrade.Approved : ReadingGrade.NoOpinion } : null }).ToArray();
            return word with { Approved = approved, Disapproved = rejected, Analyses = all, Occurrences = occurrences };
        }).ToArray();
        var texts = words.SelectMany(word => word.Occurrences.Select(occurrence => (Word: word, Occurrence: occurrence)))
            .GroupBy(item => item.Occurrence.TextId).Select(text => new TextLines(text.Key,
                text.First().Occurrence.TextTitle, text.GroupBy(item => item.Occurrence.Line).OrderBy(line => line.Key)
                    .Select(line => new TextLine(line.Key, line.Select((item, offset) => new TextToken(item.Word.Form,
                        item.Word.Form, null, item.Occurrence.Status)
                    {
                        WordformId = Guid.TryParse(item.Word.WordformGuid, out var id) ? id : null,
                        FormWritingSystem = item.Word.FormWritingSystem,
                        Analysis = item.Occurrence.Analysis,
                        StoredAnalyses = item.Word.Analyses,
                        IncorrectSpelling = item.Word.IncorrectSpelling,
                        OccurrenceIndex = offset,
                    }).ToArray())).ToArray())).ToArray();
        return source with { Words = words, Texts = texts };
    }

    public static AssessCommandResponse WithOrigins(AssessCommandResponse source)
    {
        var root = source.Measurements.LastOrDefault(item => item.Kind == AssessmentKinds.ParseTime)?.AssessmentId
            ?? source.AssessmentIds.First();
        return source with { Words = source.Words.Select(word => word with
            { Origin = word.Origin ?? new WordMeasurementOrigin(root, source.InvocationId, DateTimeOffset.UnixEpoch) }).ToArray() };
    }

    public static async Task RealizeAsync(ResultsInTextViewModel reader, bool waitForPresentation = true)
    {
        if (waitForPresentation) await reader.SelectionRefresh;
        Xunit.Assert.InRange(reader.VisibleHeaders.Count, 0, 48);
        foreach (var header in reader.VisibleHeaders)
        {
            Xunit.Assert.InRange(header.TokenCount, 0, 20);
            var line = reader.RealizeLine(header);
            var pages = (IProgressivePageSource)line.TokenSource!;
            await pages.ReadPageAsync(0, 20, CancellationToken.None);
        }
        if (reader.LinePages is { } lines) await lines.Pending;
    }

    public static IReadOnlyList<ResultsLineViewModel> VisibleLines(ResultsInTextViewModel reader) =>
        reader.VisibleHeaders.Select(header => reader.LinePages?.RealizedLines.FirstOrDefault(line =>
            line.ParagraphId == header.ParagraphId && line.SegmentId == header.SegmentId))
            .OfType<ResultsLineViewModel>().ToArray();

    internal Task ReplaceSourceAsync(TextWordsResponse source) => PublishAsync(source, _assessment);

    internal CommandOutcome<WordReadStateResponse> WriteReadState(WordReadStateRequest request) =>
        SIL.Motif.Commands.ReadStateCommands.Execute(request with { ProjectPath = _store!.ProjectPath });

    public async ValueTask DisposeAsync()
    {
        await Reads.StopAsync();
        _pausedDatabase?.Dispose();
        _pausedDatabase = null;
        _store?.Dispose();
        _store = null;
    }
}
