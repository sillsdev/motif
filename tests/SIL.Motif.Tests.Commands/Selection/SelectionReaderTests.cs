using SIL.Motif.Host;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Analysis;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using SIL.LCModel;
using SIL.LCModel.Core.Text;
using SIL.LCModel.Infrastructure;
using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace SIL.Motif.Tests.Commands.SelectionReading;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group1)]
public sealed class SelectionReaderTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.SelectionReaderTests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task IndependentlyPagedLinesShareOneRangeAndRetainTheirExactSourceOffsets()
    {
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            var first = cache.ServiceLocator.GetInstance<ISegmentRepository>().GetObject(text.FirstSegmentId);
            var second = cache.ServiceLocator.GetInstance<ISegmentRepository>().GetObject(text.SecondSegmentId);
            var analysis = first.AnalysesRS[0];
            for (var index = 0; index < 99; index++) first.AnalysesRS.Add(analysis);
            var word = second.AnalysesRS[0];
            for (var index = 0; index < 99; index++) second.AnalysesRS.Add(word);
        });
        new FwDataProjectLoader().Save(cache);
        var path = cache.ProjectId.Path;
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path),
            Path.Combine(_root, "managed"));
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var opened = await SelectionReader.OpenAsync(new OpenSelectionReaderRequest(path, [text.TextId], []));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        await using var reader = opened.Value!;
        var summarized = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(summarized.Succeeded, summarized.Refusal?.Message);
        using var summary = summarized.Value!;
        var header = Assert.Single(summary.Value.Texts);
        Assert.Equal([101, 100], header.Lines.Select(line => line.TokenCount));
        Assert.Equal([text.FirstParagraphId, text.SecondParagraphId], header.Lines.Select(line => line.ParagraphId));
        Assert.Equal(0, reader.Diagnostics.TextRowsDeserialized);

        var initial = await reader.ReadLinePagesAsync(text.TextId, [new(2, 60), new(1, 0)]);
        Assert.True(initial.Succeeded, initial.Refusal?.Message);
        using var initialRead = initial.Value!;
        Assert.Equal(40, initialRead.Value.ReturnedTokens);
        Assert.Equal([1, 2], initialRead.Value.Lines.Select(line => line.LineNumber));
        Assert.Equal([0, 60], initialRead.Value.Lines.Select(line => line.SourceTokenOffset));
        Assert.Equal(SeededProject.PunctuationForm, initialRead.Value.Lines[0].Tokens[1].Token.Text);
        Assert.Equal(1, reader.Diagnostics.LeasedLineRanges);
        Assert.Equal(40, reader.Diagnostics.LeasedLineTokens);
        var diagnostics = reader.Diagnostics;
        var warm = await reader.ReadLinePagesAsync(text.TextId, [new(1, 0), new(2, 60)]);
        Assert.True(warm.Succeeded, warm.Refusal?.Message);
        using var warmRead = warm.Value!;
        Assert.Same(initialRead.Value, warmRead.Value);
        Assert.Equal(diagnostics.QueriesIssued + 4, reader.Diagnostics.QueriesIssued);
        Assert.Equal(diagnostics.TextRowsDeserialized, reader.Diagnostics.TextRowsDeserialized);
        Assert.Equal(1, reader.Diagnostics.LeasedLineRanges);
        initialRead.Dispose();
        warmRead.Dispose();

        var advanced = await reader.ReadLinePagesAsync(text.TextId, [new(1, 100), new(2, 80)]);
        Assert.True(advanced.Succeeded, advanced.Refusal?.Message);
        using var advancedRead = advanced.Value!;
        Assert.Equal([100, 80], advancedRead.Value.Lines.Select(line => line.SourceTokenOffset));
        Assert.Equal([1, 20], advancedRead.Value.Lines.Select(line => line.Tokens.Count));
        Assert.Equal(1, reader.Diagnostics.LeasedLineRanges);
        Assert.Equal(21, reader.Diagnostics.LeasedLineTokens);
        Assert.Equal(1, reader.Diagnostics.TextRowsDeserialized);
        var invalid = await reader.ReadLinePagesAsync(text.TextId, [new(1, 0), new(1, 20)]);
        Assert.False(invalid.Succeeded);
        var beyond = await reader.ReadLinePagesAsync(text.TextId, [new(2, 101)]);
        Assert.False(beyond.Succeeded);
    }

    [Fact]
    public async Task OpenBindsTheBaselineAndSelectionIdentityAndDisposeReleasesTheSession()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);

        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);
        var request = new SelectionReadRequest(ProjectWorkspaceKey.Compute(project), [text.TextId],
            ["  cafe\u0301  ", "  ", "word", SeededProject.AnalysedWordForm])
        {
            ProjectPath = projectPath,
        };

        using var repositoryObservation = ScaleCountHarness.ObserveRepositoryReads();
        var opened = await SelectionReader.OpenAsync(database, request);
        var openReads = repositoryObservation.Snapshot();

        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        var reader = opened.Value!;
        var context = reader.Context;
        Assert.Equal(capture.Value!.Token, context.Baseline);
        Assert.Equal(request.ProjectKey, context.ProjectKey);
        Assert.Equal([text.TextId], context.TextIds);
        Assert.Equal(["cafe\u0301", "word", SeededProject.AnalysedWordForm], context.AddedWords);
        Assert.StartsWith("sha256:", context.SelectionDigest, StringComparison.Ordinal);
        Assert.Equal(context.Generation, reader.Diagnostics.Generation);
        Assert.Equal(1, reader.Diagnostics.IndexRowsDeserialized);
        Assert.Equal(0, reader.Diagnostics.TextRowsDeserialized);
        Assert.Equal(0, reader.Diagnostics.WordformRowsDeserialized);
        Assert.Equal(5, reader.Diagnostics.QueriesIssued);
        Assert.Equal(openReads.Queries, reader.Diagnostics.QueriesIssued);
        Assert.Equal(openReads.RecordsDeserialized, reader.Diagnostics.RepositoryRecordsDeserialized);
        Assert.True(reader.Diagnostics.SourceTokens > 0);
        Assert.Equal(0, reader.Diagnostics.LiveLineModels);
        Assert.Equal(0, reader.Diagnostics.LiveTokenModels);

        var summaryRead = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(summaryRead.Succeeded, summaryRead.Refusal?.Message);
        Assert.Equal(context.Generation, summaryRead.Value!.PresentationStamp);
        Assert.Contains(summaryRead.Value.Value.Words,
            row => row.Key.Form == SeededProject.AnalysedWordForm.Normalize(NormalizationForm.FormD));
        var addedWord = Assert.Single(summaryRead.Value.Value.Words,
            row => row.IsAddedWord && row.Key.Form == SeededProject.AnalysedWordForm.Normalize(NormalizationForm.FormD));
        var wordContext = await reader.ReadOccurrencesAsync(addedWord.Key, new OccurrenceRange(0, 8));
        Assert.True(wordContext.Succeeded, wordContext.Refusal?.Message);
        using (var wordContextRead = wordContext.Value!)
        {
            Assert.Equal(0, wordContextRead.Value.TotalCount);
            Assert.Empty(wordContextRead.Value.Occurrences);
            Assert.Equal(text.AnalysedWordformId,
                Assert.Single(wordContextRead.Value.CandidateWordforms).WordformId);
            Assert.Contains(wordContextRead.Value.Wordforms.Keys,
                key => key.WordformId == text.AnalysedWordformId);
        Assert.NotEmpty(wordContextRead.Value.WordContext!.Analyses);
        Assert.InRange(wordContextRead.Value.WordContext.Analyses.Count, 1, 256);
        Assert.InRange(wordContextRead.Value.CandidateWordforms.Count, 1, 64);
        }
        var beforeQuery = reader.Diagnostics.QueriesIssued;
        var action = reader.ResolveActionScope([summaryRead.Value.Value.Words[0].Key]);
        Assert.NotEmpty(action.Occurrences);
        Assert.Equal(beforeQuery, reader.Diagnostics.QueriesIssued);

        reader.Dispose();
        Assert.True(reader.Diagnostics.IsDisposed);
        Assert.Equal(0, reader.Diagnostics.CachedEntries);
        Assert.Equal(0, reader.Diagnostics.IndexedWordRows);
        Assert.Throws<ObjectDisposedException>(() => reader.Context);
        await reader.DisposeAsync();

        var reopened = await SelectionReader.OpenAsync(database, request);
        Assert.True(reopened.Succeeded, reopened.Refusal?.Message);
        using var secondReader = reopened.Value!;
        Assert.True(secondReader.Context.Generation > context.Generation);
    }

    [Fact]
    public async Task OwnedFactoryHoldsTheStoreUseLeaseUntilDisposalAndAllowsConcurrentCommands()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath),
            Path.Combine(_root, "managed"));
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        var lockPath = ProjectDatabaseCatalog.DatabasePathFor(project) + ".use.lock";

        var opened = await SelectionReader.OpenAsync(new OpenSelectionReaderRequest(projectPath, [text.TextId], []));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        var reader = opened.Value!;
        try
        {
            Assert.Throws<IOException>(() => new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite,
                FileShare.None));
            var concurrent = ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText,
                (_, _) => CommandOutcome<object>.Success(new object()));
            Assert.True(concurrent.Succeeded, concurrent.Refusal?.Message);
            var summary = await reader.ReadSummaryAsync(new SelectionViewRequest());
            Assert.True(summary.Succeeded, summary.Refusal?.Message);
            using var read = summary.Value!;
            Assert.Single(read.Value.Texts);
        }
        finally
        {
            await reader.DisposeAsync();
        }
        using var exclusive = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task OwnedFactoryCancellationAndRefusalLeaveNoStoreUseLease()
    {
        Directory.CreateDirectory(_root);
        var projectPath = Path.Combine(_root, "empty.fwdata");
        File.WriteAllText(projectPath, string.Empty);
        var project = new ProjectLocator(projectPath, "empty");
        var lockPath = ProjectDatabaseCatalog.DatabasePathFor(project) + ".use.lock";
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = await SelectionReader.OpenAsync(new OpenSelectionReaderRequest(projectPath, [], []),
            cancellation.Token);
        Assert.Equal(FailureReason.Cancelled, cancelled.Refusal?.Reason);
        Assert.False(File.Exists(lockPath));

        var refused = await SelectionReader.OpenAsync(new OpenSelectionReaderRequest(projectPath, [], []));
        Assert.False(refused.Succeeded);
        Assert.Equal("selection-reader.no-baseline", refused.Refusal?.Code);
        using var exclusive = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Fact]
    public async Task DetailReadsKeepContextWideLineAndTokenModelCountsBounded()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        var secondText = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);
        var opened = await SelectionReader.OpenAsync(database,
            new SelectionReadRequest(ProjectWorkspaceKey.Compute(project), [text.TextId, secondText.TextId], []));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        Assert.Equal([text.TextId, secondText.TextId], reader.Context.TextIds);
        AssertModelCounts(reader, 0, 0, 0, 0);

        var summary = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(summary.Succeeded, summary.Refusal?.Message);
        using var summaryRead = summary.Value!;
        AssertModelCounts(reader, 0, 0, 0, 0);
        var word = summaryRead.Value.Words.First(row => row.OccurrenceCount > 0);

        var lineResult = await reader.ReadLinesAsync(text.TextId, new TextLineRange(1, 1, 0, 1));
        Assert.True(lineResult.Succeeded, lineResult.Refusal?.Message);
        var lineRead = lineResult.Value!;
        Assert.Equal(text.TextId, lineRead.Value.TextId);
        Assert.Equal(summaryRead.Context, lineRead.Context);
        Assert.Single(lineRead.Value.Lines);
        Assert.InRange(lineRead.Value.ReturnedTokens, 0, 1);
        var lineModel = new TrackedSelectionModel(lineRead.Lease, SelectionModelKind.Line);
        var lineTokenModel = new TrackedSelectionModel(lineRead.Lease, SelectionModelKind.Token);
        AssertModelCounts(reader, 1, 1, 1, 1);
        lineModel.Dispose();
        lineTokenModel.Dispose();
        AssertModelCounts(reader, 1, 0, 1, 0);
        var coldLineQueries = reader.Diagnostics.QueriesIssued;

        var warmLine = await reader.ReadLinesAsync(text.TextId, new TextLineRange(1, 1, 0, 1));
        Assert.True(warmLine.Succeeded, warmLine.Refusal?.Message);
        var warmLineRead = warmLine.Value!;
        Assert.Equal(4, reader.Diagnostics.QueriesIssued - coldLineQueries);
        Assert.Same(lineRead.Value, warmLineRead.Value);
        lineRead.Dispose();
        warmLineRead.Dispose();

        var switchedText = await reader.ReadLinesAsync(secondText.TextId, new TextLineRange(1, 1, 0, 1));
        Assert.True(switchedText.Succeeded, switchedText.Refusal?.Message);
        var switchedTextRead = switchedText.Value!;
        Assert.Equal(secondText.TextId, switchedTextRead.Value.TextId);
        Assert.Equal(1, switchedTextRead.Value.Lines[0].LineNumber);
        var switchedLineModel = new TrackedSelectionModel(switchedTextRead.Lease, SelectionModelKind.Line);
        var switchedTokenModel = new TrackedSelectionModel(switchedTextRead.Lease, SelectionModelKind.Token);
        AssertModelCounts(reader, 2, 1, 2, 1);
        switchedLineModel.Dispose();
        switchedTokenModel.Dispose();
        switchedTextRead.Dispose();
        AssertModelCounts(reader, 2, 0, 2, 0);

        var occurrence = await reader.ReadOccurrencesAsync(word.Key, new OccurrenceRange(0, 1));
        Assert.True(occurrence.Succeeded, occurrence.Refusal?.Message);
        using var occurrenceRead = occurrence.Value!;
        Assert.Single(occurrenceRead.Value.Occurrences);
        Assert.Equal(word.Key.WordformId, occurrenceRead.Value.Occurrences[0].Token.WordformId);
        var occurrenceTokenModel = new TrackedSelectionModel(occurrenceRead.Lease, SelectionModelKind.Token);
        AssertModelCounts(reader, 2, 0, 3, 1);
        occurrenceTokenModel.Dispose();
        AssertModelCounts(reader, 2, 0, 3, 0);
        var coldOccurrenceQueries = reader.Diagnostics.QueriesIssued;
        var warmOccurrence = await reader.ReadOccurrencesAsync(word.Key, new OccurrenceRange(0, 1));
        Assert.True(warmOccurrence.Succeeded, warmOccurrence.Refusal?.Message);
        using var warmOccurrenceRead = warmOccurrence.Value!;
        Assert.Equal(4, reader.Diagnostics.QueriesIssued - coldOccurrenceQueries);
        Assert.True(reader.Diagnostics.CachedEntries > 0);

        var scrolled = await reader.ReadLinesAsync(secondText.TextId, new TextLineRange(2, 1, 0, 1));
        Assert.True(scrolled.Succeeded, scrolled.Refusal?.Message);
        using var scrolledRead = scrolled.Value!;
        Assert.Equal(secondText.TextId, scrolledRead.Value.TextId);
        Assert.Equal(2, scrolledRead.Value.Lines[0].LineNumber);
        var scrolledLineModel = new TrackedSelectionModel(scrolledRead.Lease, SelectionModelKind.Line);
        var scrolledTokenModel = new TrackedSelectionModel(scrolledRead.Lease, SelectionModelKind.Token);
        AssertModelCounts(reader, 3, 1, 4, 1);
        scrolledLineModel.Dispose();
        scrolledTokenModel.Dispose();
        AssertModelCounts(reader, 3, 0, 4, 0);

        var lineBudget = Enumerable.Range(0, 48)
            .Select(_ => new TrackedSelectionModel(scrolledRead.Lease, SelectionModelKind.Line)).ToArray();
        Assert.Equal(48, reader.Diagnostics.LiveLineModels);
        Assert.ThrowsAny<InvalidOperationException>(() =>
            new TrackedSelectionModel(scrolledRead.Lease, SelectionModelKind.Line));
        foreach (var model in lineBudget) model.Dispose();
        var pinBudget = Enumerable.Range(0, 64)
            .Select(_ => new TrackedSelectionModel(scrolledRead.Lease, SelectionModelKind.Token,
                SelectionModelUse.Pinned)).ToArray();
        Assert.Equal(64, reader.Diagnostics.LivePinnedModels);
        Assert.ThrowsAny<InvalidOperationException>(() => new TrackedSelectionModel(scrolledRead.Lease,
            SelectionModelKind.Token, SelectionModelUse.Pinned));
        foreach (var model in pinBudget) model.Dispose();

        var retainedCardReferences = DisposeReaderWithRetainedModel(reader, scrolledRead.Lease);
        Assert.Equal(51, reader.Diagnostics.CreatedLineModels);
        Assert.Equal(68, reader.Diagnostics.CreatedTokenModels);
        Assert.Equal(0, reader.Diagnostics.LiveLineModels);
        Assert.Equal(0, reader.Diagnostics.LiveTokenModels);
        Assert.Throws<ObjectDisposedException>(() => scrolledRead.Lease.RegisterModel(SelectionModelKind.Line));
        Assert.Equal(0, ScaleCountHarness.AssertLiveViewModelBudget(retainedCardReferences,
            typeof(TrackedSelectionModel), 0, 0));
        Assert.Equal(0, reader.Diagnostics.LiveCardModels);
        Assert.Equal(0, reader.Diagnostics.LivePinnedModels);
    }

    [Fact]
    public async Task ResultLeasesBoundEvictedRangesPrefetchPinsAndActiveHandles()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        var opened = await SelectionReader.OpenAsync(database,
            new SelectionReadRequest(ProjectWorkspaceKey.Compute(project), [text.TextId], [])
            {
                ProjectPath = projectPath,
            });
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;

        var ranges = new List<SelectionRead<TextLineSlice>>();
        for (var line = 1; line <= 3; line++)
        {
            var result = await reader.ReadLinesAsync(text.TextId, new TextLineRange(line, 1, 0, 4));
            Assert.True(result.Succeeded, result.Refusal?.Message);
            ranges.Add(result.Value!);
        }
        var exhausted = await reader.ReadLinesAsync(text.TextId, new TextLineRange(4, 1, 0, 4));
        Assert.False(exhausted.Succeeded);
        Assert.Equal("selection-reader.ownership-limit", exhausted.Refusal!.Code);
        Assert.Equal(3, reader.Diagnostics.CachedLineRanges);
        Assert.Equal(3, reader.Diagnostics.LeasedLineRanges);
        Assert.Equal(1, reader.Diagnostics.LeasedEvictedLineRanges);
        Assert.Equal(3, reader.Diagnostics.LeasedResults);

        ranges[0].Dispose();
        var recovered = await reader.ReadLinesAsync(text.TextId, new TextLineRange(4, 1, 0, 4));
        Assert.True(recovered.Succeeded, recovered.Refusal?.Message);
        using var recoveredRead = recovered.Value!;
        Assert.Equal(3, reader.Diagnostics.LeasedLineRanges);
        Assert.Equal(0, reader.Diagnostics.LeasedEvictedLineRanges);
        ranges[1].Dispose();
        ranges[2].Dispose();
        recoveredRead.Dispose();

        var prefetch = await reader.ReadLinesAsync(text.TextId, new TextLineRange(5, 1, 0, 4),
            purpose: SelectionReadPurpose.Prefetch);
        Assert.True(prefetch.Succeeded, prefetch.Refusal?.Message);
        using var prefetchRead = prefetch.Value!;
        Assert.Equal(1, reader.Diagnostics.PrefetchLeases);
        Assert.Throws<InvalidOperationException>(() => prefetchRead.Lease.RegisterModel(SelectionModelKind.Line));
        var secondPrefetch = await reader.ReadLinesAsync(text.TextId, new TextLineRange(6, 1, 0, 4),
            purpose: SelectionReadPurpose.Prefetch);
        Assert.False(secondPrefetch.Succeeded);
        Assert.Equal("selection-reader.ownership-limit", secondPrefetch.Refusal!.Code);
        prefetchRead.Dispose();

        var firstPin = await reader.ReadLinesAsync(text.TextId, new TextLineRange(5, 1, 0, 4),
            purpose: SelectionReadPurpose.Pin);
        var secondPin = await reader.ReadLinesAsync(text.TextId, new TextLineRange(5, 1, 0, 4),
            purpose: SelectionReadPurpose.Pin);
        Assert.True(firstPin.Succeeded, firstPin.Refusal?.Message);
        Assert.True(secondPin.Succeeded, secondPin.Refusal?.Message);
        using var firstPinRead = firstPin.Value!;
        using var secondPinRead = secondPin.Value!;
        Assert.Equal(2, reader.Diagnostics.PinnedLeases);
        var thirdPin = await reader.ReadLinesAsync(text.TextId, new TextLineRange(5, 1, 0, 4),
            purpose: SelectionReadPurpose.Pin);
        Assert.False(thirdPin.Succeeded);
        Assert.Equal("selection-reader.ownership-limit", thirdPin.Refusal!.Code);
        firstPinRead.Dispose();
        secondPinRead.Dispose();

        var summaries = new List<SelectionRead<SelectionSummary>>();
        for (var index = 0; index < 16; index++)
        {
            var summary = await reader.ReadSummaryAsync(new SelectionViewRequest());
            Assert.True(summary.Succeeded, summary.Refusal?.Message);
            summaries.Add(summary.Value!);
        }
        var seventeenth = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.False(seventeenth.Succeeded);
        Assert.Equal("selection-reader.ownership-limit", seventeenth.Refusal!.Code);
        Assert.Equal(16, reader.Diagnostics.LeasedResults);
        var stampBeforeRefusedPresentation = reader.Diagnostics.PresentationStamp;
        var refusedPresentation = await reader.ReadPresentationAsync();
        Assert.False(refusedPresentation.Succeeded);
        Assert.Equal("selection-reader.ownership-limit", refusedPresentation.Refusal!.Code);
        Assert.Equal(stampBeforeRefusedPresentation, reader.Diagnostics.PresentationStamp);
        foreach (var summary in summaries) summary.Dispose();
        Assert.Equal(0, reader.Diagnostics.LeasedResults);
    }

    [Theory]
    [InlineData("lines", "dispose")]
    [InlineData("lines", "invalidate")]
    [InlineData("occurrences", "dispose")]
    [InlineData("occurrences", "invalidate")]
    public async Task InFlightDetailCannotRepublishAfterSessionCloses(string detailKind, string closeAction)
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        using var reached = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var opened = await SelectionReader.OpenAsync(database,
            new SelectionReadRequest(ProjectWorkspaceKey.Compute(project), [text.TextId], [])
            {
                PauseAt = point =>
                {
                    if (point != SelectionReaderPausePoint.BeforeCachePublication) return;
                    reached.Set();
                    if (!resume.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("The paused detail read was not released.");
                },
            });
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        var reader = opened.Value!;
        Task<CommandOutcome<SelectionRead<TextLineSlice>>>? lineReading = null;
        Task<CommandOutcome<SelectionRead<WordOccurrences>>>? occurrenceReading = null;
        if (detailKind == "lines")
        {
            lineReading = reader.ReadLinesAsync(text.TextId, new TextLineRange(1, 1, 0, 8));
        }
        else
        {
            var summary = await reader.ReadSummaryAsync(new SelectionViewRequest());
            Assert.True(summary.Succeeded, summary.Refusal?.Message);
            var word = summary.Value!.Value.Words.First(row => row.OccurrenceCount > 0);
            summary.Value.Dispose();
            occurrenceReading = reader.ReadOccurrencesAsync(word.Key, new OccurrenceRange(0, 1));
        }
        Assert.True(await Task.Run(() => reached.Wait(TimeSpan.FromSeconds(10))),
            "The detail read did not reach its cache publication pause.");

        Task? disposal = null;
        try
        {
            if (closeAction == "dispose")
            {
                disposal = reader.DisposeAsync().AsTask();
                Assert.False(disposal.IsCompleted);
            }
            else
            {
                reader.Invalidate();
            }
        }
        finally
        {
            resume.Set();
        }

        var expectedCode = closeAction == "dispose" ? "selection-reader.disposed" : "texts.evidence-changed";
        if (lineReading is not null)
        {
            var result = await lineReading;
            Assert.False(result.Succeeded);
            Assert.Equal(expectedCode, result.Refusal!.Code);
        }
        else
        {
            var result = await occurrenceReading!;
            Assert.False(result.Succeeded);
            Assert.Equal(expectedCode, result.Refusal!.Code);
        }
        if (disposal is not null) await disposal;
        var diagnostics = reader.Diagnostics;
        Assert.Equal(0, diagnostics.CachedEntries);
        Assert.Equal(0, diagnostics.CachedLineRanges);
        Assert.Equal(0, diagnostics.CachedOccurrencePages);
        Assert.Equal(0, diagnostics.CachedWordDetails);
        Assert.Equal(0, diagnostics.CachedTextGraphs);
        Assert.Equal(0, diagnostics.LeasedResults);
        Assert.Equal(0, diagnostics.PendingReads);
        Assert.Equal(closeAction == "dispose", diagnostics.IsDisposed);
        Assert.Equal(closeAction == "invalidate", diagnostics.IsObsolete);
        await reader.DisposeAsync();
    }

    private static void AssertModelCounts(
        SelectionReader reader,
        long createdLines,
        long liveLines,
        long createdTokens,
        long liveTokens)
    {
        var diagnostics = reader.Diagnostics;
        Assert.Equal(createdLines, diagnostics.CreatedLineModels);
        Assert.Equal(liveLines, diagnostics.LiveLineModels);
        Assert.Equal(createdTokens, diagnostics.CreatedTokenModels);
        Assert.Equal(liveTokens, diagnostics.LiveTokenModels);
        Assert.InRange(diagnostics.LiveLineModels, 0, 1);
        Assert.InRange(diagnostics.LiveTokenModels, 0, 1);
        Assert.InRange(diagnostics.CreatedLineModels, 0, 51);
        Assert.InRange(diagnostics.CreatedTokenModels, 0, 68);
    }

    [Theory]
    [InlineData("DELETE FROM BaselineTextReadIndex WHERE ProjectKey = $project;")]
    [InlineData("UPDATE BaselineTextReadIndex SET IndexJson = '{' WHERE ProjectKey = $project;")]
    public async Task OpenRefusesAMissingOrMalformedCompactIndex(string sql)
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = sql;
            command.Parameters.AddWithValue("$project", ProjectWorkspaceKey.Compute(project));
            Assert.True(command.ExecuteNonQuery() > 0);
        }

        var opened = await SelectionReader.OpenAsync(database,
            new SelectionReadRequest(ProjectWorkspaceKey.Compute(project), [text.TextId], []));

        Assert.False(opened.Succeeded);
        Assert.Equal("baseline.text-words-unreadable", opened.Refusal!.Code);
        Assert.Contains("delete", opened.Refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("count")]
    [InlineData("duplicate-analysis")]
    [InlineData("cross-wordform-analysis")]
    [InlineData("source-analysis-id")]
    [InlineData("null-morph")]
    [InlineData("null-forms")]
    [InlineData("null-form")]
    public async Task OpenRefusesCompactIndexesWithMalformedOrContradictoryAnalysisFacts(string corruption)
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        var projectKey = ProjectWorkspaceKey.Compute(project);
        string indexJson;
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT IndexJson FROM BaselineTextReadIndex WHERE ProjectKey = $project AND TextId = $text;";
            command.Parameters.AddWithValue("$project", projectKey);
            command.Parameters.AddWithValue("$text", text.TextId.ToString("D"));
            indexJson = (string)command.ExecuteScalar()!;
        }

        var index = JsonNode.Parse(indexJson)!.AsObject();
        var wordforms = index["Wordforms"]!.AsArray();
        var analyzed = wordforms.Select(node => node!.AsObject())
            .FirstOrDefault(node => node["Analyses"]!.AsArray().Any(analysis =>
                analysis!["Identity"]!["Morphs"]!.AsArray().Any(morph =>
                    morph!["Forms"] is JsonArray forms && forms.Count > 0)));
        Assert.NotNull(analyzed);
        var analysisNode = analyzed!["Analyses"]!.AsArray().Select(node => node!.AsObject())
            .First(analysis => analysis["Identity"]!["Morphs"]!.AsArray().Any(morph =>
                morph!["Forms"] is JsonArray forms && forms.Count > 0));
        if (corruption == "count")
        {
            analyzed!["ApprovedCount"] = (int)analyzed["ApprovedCount"]! + 1;
        }
        else if (corruption == "duplicate-analysis")
        {
            var analyses = analyzed!["Analyses"]!.AsArray();
            Assert.NotEmpty(analyses);
            analyses.Add(analyses[0]!.DeepClone());
        }
        else if (corruption == "source-analysis-id")
        {
            analysisNode["Identity"]!["SourceAnalysisId"] = CanonicalId.Mint().Value;
        }
        else if (corruption == "null-morph")
        {
            analysisNode["Identity"]!["Morphs"]!.AsArray()[0] = null;
        }
        else if (corruption == "null-forms")
        {
            analysisNode["Identity"]!["Morphs"]!.AsArray()[0]!["Forms"] = null;
        }
        else if (corruption == "null-form")
        {
            analysisNode["Identity"]!["Morphs"]!.AsArray()[0]!["Forms"]!.AsArray()[0] = null;
        }
        else
        {
            var sourceAnalysis = analyzed!["Analyses"]!.AsArray()[0]!.AsObject();
            var other = wordforms.Select(node => node!.AsObject())
                .FirstOrDefault(node => node["WordformId"]!.GetValue<string>() !=
                    analyzed["WordformId"]!.GetValue<string>() &&
                    !node["Analyses"]!.AsArray().Any(analysis =>
                        analysis!["Key"]!.GetValue<string>() == sourceAnalysis["Key"]!.GetValue<string>() &&
                        analysis["AnalysisId"]!.GetValue<string>() == sourceAnalysis["AnalysisId"]!.GetValue<string>()));
            Assert.NotNull(other);
            var wordformId = analyzed!["WordformId"]!.GetValue<string>();
            var otherId = other!["WordformId"]!.GetValue<string>();
            var token = index["Lines"]!.AsArray().SelectMany(line => line!["Tokens"]!.AsArray())
                .Select(node => node!.AsObject()).First(candidate =>
                    candidate["WordformId"]?.GetValue<string>() == wordformId &&
                    candidate["AnalysisKey"] is not null);
            token["WordformId"] = otherId;
        }

        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE BaselineTextReadIndex SET IndexJson = $json WHERE ProjectKey = $project AND TextId = $text;";
            command.Parameters.AddWithValue("$json", index.ToJsonString());
            command.Parameters.AddWithValue("$project", projectKey);
            command.Parameters.AddWithValue("$text", text.TextId.ToString("D"));
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var opened = await SelectionReader.OpenAsync(database,
            new SelectionReadRequest(projectKey, [text.TextId], []));

        Assert.False(opened.Succeeded);
        Assert.Equal("baseline.text-words-unreadable", opened.Refusal!.Code);
    }

    [Theory]
    [InlineData("compact-detail")]
    [InlineData("digest")]
    public async Task CompactIndexRefusesDetailMismatchAndDigestMismatch(string corruption)
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        var projectKey = ProjectWorkspaceKey.Compute(project);
        string indexJson;
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT IndexJson FROM BaselineTextReadIndex WHERE ProjectKey = $project AND TextId = $text;";
            command.Parameters.AddWithValue("$project", projectKey);
            command.Parameters.AddWithValue("$text", text.TextId.ToString("D"));
            indexJson = (string)command.ExecuteScalar()!;
        }

        var index = JsonNode.Parse(indexJson)!.AsObject();
        if (corruption == "digest")
        {
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE BaselineTextReadIndex SET BundleDigest = $digest " +
                "WHERE ProjectKey = $project AND TextId = $text;";
            command.Parameters.AddWithValue("$digest", "sha256:" + new string('0', 64));
            command.Parameters.AddWithValue("$project", projectKey);
            command.Parameters.AddWithValue("$text", text.TextId.ToString("D"));
            Assert.Equal(1, command.ExecuteNonQuery());

            var refused = await SelectionReader.OpenAsync(database,
                new SelectionReadRequest(projectKey, [text.TextId], []));
            Assert.False(refused.Succeeded);
            Assert.Equal("baseline.text-words-unreadable", refused.Refusal!.Code);
            return;
        }

        var wordform = index["Wordforms"]!.AsArray().Select(node => node!.AsObject())
            .First(candidate => candidate["Analyses"]!.AsArray().Any(analysis =>
                analysis!["Identity"]!["Morphs"]!.AsArray().Any(morph =>
                    morph!["Forms"] is JsonArray forms && forms.Count > 0)));
        var wordformId = Guid.Parse(wordform["WordformId"]!.GetValue<string>());
        var analysis = wordform["Analyses"]!.AsArray().Select(node => node!.AsObject())
            .First(candidate => candidate["Identity"]!["Morphs"]!.AsArray().Any(morph =>
                morph!["Forms"] is JsonArray forms && forms.Count > 0));
        var forms = analysis["Identity"]!["Morphs"]!.AsArray()[0]!["Forms"]!.AsArray();
        forms[0] = "reader-compact-detail-mismatch";
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE BaselineTextReadIndex SET IndexJson = $json " +
                "WHERE ProjectKey = $project AND TextId = $text;";
            command.Parameters.AddWithValue("$json", index.ToJsonString());
            command.Parameters.AddWithValue("$project", projectKey);
            command.Parameters.AddWithValue("$text", text.TextId.ToString("D"));
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var opened = await SelectionReader.OpenAsync(database,
            new SelectionReadRequest(projectKey, [text.TextId], []));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        var summary = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(summary.Succeeded, summary.Refusal?.Message);
        using var summaryRead = summary.Value!;
        var lineNumber = index["Lines"]!.AsArray().Select(node => node!.AsObject())
            .First(line => line["Tokens"]!.AsArray().Any(token =>
                token!["WordformId"]?.GetValue<string>() == wordformId.ToString("D")))
            ["Number"]!.GetValue<int>();
        var details = await reader.ReadLinesAsync(text.TextId, new TextLineRange(lineNumber, 1, 0, 128));
        Assert.False(details.Succeeded);
        Assert.Equal("baseline.text-words-unreadable", details.Refusal!.Code);
    }

    [Fact]
    public async Task OpenJoinsTheCurrentDefaultAssessmentAndStampsItsIdentity()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        var projectKey = ProjectWorkspaceKey.Compute(project);
        new NamedSelectionRepository(database).SetDefault("Default", [text.TextId], [],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
        var before = CurrentEvidenceQuery.ReadCurrentEvidence(database, project,
            includeResolvedReadings: false, includeWordContext: false);
        Assert.True(before.Succeeded, before.Refusal?.Message);
        var selection = before.Value!.Selection!.Selection;
        var words = selection.Words.Select(form => new AssessedWord(form, "analysed", [])
        {
            ProjectStanding = "approved",
            OccurrenceCount = 1,
            ReadingGrades = ["A"],
        }).ToArray();
        var baselineJson = System.Text.Json.JsonSerializer.Serialize(capture.Value!.Token, MotifJson.CreateOptions());
        new AssessmentRepository(database).Record(new NewAssessmentRecord(
            "selection-reader-assessment", null, null, "pangloss", AssessmentKind.ParseTime.ToStoredKind(),
            "{}", "sha256:scope", "whitespace", "1", baselineJson, selection, "sha256:outcome",
            "sha256:semantic", "sha256:grammar", "fingerprint", "pipeline", 0, words,
            SavedUtc: DateTimeOffset.UtcNow.ToString("O")));

        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(
            projectKey, [text.TextId], []) { ProjectPath = projectPath });

        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        Assert.Equal("default", reader.Context.SelectionOrigin);
        Assert.Equal("selection-reader-assessment", reader.Context.AssessmentRootId);
        Assert.Empty(reader.Context.ReplacementAssessmentIds);
        Assert.Equal(1, reader.Diagnostics.AssessmentRowsDeserialized);
        Assert.Equal(words.Length, reader.Diagnostics.AssessmentWordsDeserialized);
        var summary = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(summary.Succeeded, summary.Refusal?.Message);
        using var summaryRead = summary.Value!;
        var assessed = Assert.Single(summaryRead.Value.Words.Take(1));
        Assert.NotNull(assessed.Assessment);
        Assert.Equal("analysed", assessed.Assessment!.Outcome);
        Assert.Equal("approved", assessed.ProjectStanding);
        var lines = await reader.ReadLinesAsync(text.TextId, new TextLineRange(1, 1, 0, 8));
        Assert.True(lines.Succeeded, lines.Refusal?.Message);
        using var lineRead = lines.Value!;
        Assert.NotEmpty(lineRead.Value.Assessments);
    }

    [Fact]
    public async Task ReaderJoinsReadDraftAndWarningStateAndAdvancesPresentationStamp()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        new NamedSelectionRepository(database).SetDefault("Default", [text.TextId], [],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
        var root = RecordSelectionAssessment(database, project, capture.Value!.Token, "presentation-root", null,
            DateTimeOffset.UtcNow);
        var warning = new GrammarWarning(GrammarDiagnosticLevel.Warning, "spelling", [
            new GrammarWarningPart("motif word", GrammarWarningPartRole.Object, "subject")
            {
                Reach = new WarningReach(WarningWordsPath.Spelling)
                {
                    Spellings = [SeededProject.AnalysedWordForm],
                },
            },
        ], [], "stored finding")
        {
            Code = "selection-reader-spelling",
        };
        var baselineJson = System.Text.Json.JsonSerializer.Serialize(capture.Value.Token, MotifJson.CreateOptions());
        new GrammarCheckRepository(database).Save(baselineJson, root.Selection.Sha256, null,
            new GrammarCheckResponse([warning], true));

        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(
            ProjectWorkspaceKey.Compute(project), [text.TextId], []) { ProjectPath = projectPath });
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        var summaryResult = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(summaryResult.Succeeded, summaryResult.Refusal?.Message);
        using var summary = summaryResult.Value!;
        var word = Assert.Single(summary.Value.Words, item => item.Key.WordformId == text.AnalysedWordformId);
        Assert.NotNull(word.Assessment);
        Assert.True(word.Actions.CanMarkRead);

        var firstPresentation = await reader.ReadPresentationAsync();
        Assert.True(firstPresentation.Succeeded, firstPresentation.Refusal?.Message);
        using var first = firstPresentation.Value!;
        Assert.Empty(first.Value.ReadOccurrences[text.TextId]);
        var attributed = Assert.Single(first.Value.Warnings);
        Assert.Equal(WarningAttributionState.SpellingCandidates, attributed.YourWords!.State);
        Assert.Contains(attributed.YourWords.Words,
            item => string.Equals(item.Row.Word, SeededProject.AnalysedWordForm,
                StringComparison.OrdinalIgnoreCase));
        Assert.Contains(first.Value.WarningsByWord[word.Key], item => item.Code == "selection-reader-spelling");

        var readRepository = new ReadStateRepository(database);
        var staleAnchor = word.FirstOccurrence!.Anchor;
        readRepository.Upsert(new ReadOccurrenceRecord(staleAnchor, "{}"));
        var pending = PendingChanges.Put(new PutPendingChangeRequest(projectPath,
            SIL.Motif.Host.MotifProductVersion.CurrentText, first.Value.PendingChanges.Revision,
            new ChangeIntent("presentation-transition", AnalysisChangeKinds.Reject,
                CanonicalId.FromGuid(text.AnalysedWordformId).Value, SeededProject.AnalysedWordForm,
                StoredAnalysisId: CanonicalId.FromGuid(text.ApprovedAnalysisId).Value))
        {
            ExpectedContext = reader.Context.ExpectedWriteContext(),
        });
        Assert.True(pending.Succeeded, pending.Refusal?.Message);

        var beforePresentationDetails = reader.Diagnostics;
        var refreshed = await reader.ReadPresentationAsync();
        Assert.True(refreshed.Succeeded, refreshed.Refusal?.Message);
        using var refreshedRead = refreshed.Value!;
        Assert.Empty(refreshedRead.Value.ReadOccurrences[text.TextId]);
        Assert.Single(refreshedRead.Value.PendingChanges.Changes);
        var joinedPending = Assert.Single(refreshedRead.Value.PendingByWord[word.Key]);
        Assert.Equal("presentation-transition", joinedPending.Change.ChangeId);
        Assert.NotNull(joinedPending.Fit);
        Assert.Equal("{}", readRepository.Get(staleAnchor)!.FingerprintJson);
        var prune = ReadStateCommands.Execute(new WordReadStateRequest(projectPath, text.TextId)
        {
            ExpectedContext = reader.Context.ExpectedWriteContext(),
        });
        Assert.True(prune.Succeeded, prune.Refusal?.Message);
        Assert.Null(readRepository.Get(staleAnchor));
        Assert.True(refreshedRead.PresentationStamp > first.PresentationStamp);
        Assert.Equal(refreshedRead.PresentationStamp, reader.Diagnostics.PresentationStamp);
        Assert.True(summary.PresentationStamp < refreshedRead.PresentationStamp);
        Assert.True(reader.Diagnostics.TextRowsDeserialized > beforePresentationDetails.TextRowsDeserialized);
        Assert.True(reader.Diagnostics.WordformRowsDeserialized > beforePresentationDetails.WordformRowsDeserialized);
        Assert.True(reader.Diagnostics.SourceLines > beforePresentationDetails.SourceLines);
        Assert.True(reader.Diagnostics.SourceTokens > beforePresentationDetails.SourceTokens);
    }

    [Fact]
    public async Task PresentationReadsDoNotClearOrCreatePendingFitRows()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        new NamedSelectionRepository(database).SetDefault("Default", [text.TextId], [],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
        RecordSelectionAssessment(database, project, capture.Value!.Token, "readonly-fit-root", null,
            DateTimeOffset.UtcNow);
        var fits = new PendingChangeFitRepository(database);
        var baselineIdentity = System.Text.Json.JsonSerializer.Serialize(capture.Value.Token.SemanticIdentity,
            MotifJson.CreateOptions());
        fits.Save("orphan-fit-row", File.GetLastWriteTimeUtc(projectPath).Ticks, "unrelated-baseline", []);
        var beforeNoDraft = ReadPendingFitRows(database);

        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(
            ProjectWorkspaceKey.Compute(project), [text.TextId], []) { ProjectPath = projectPath });
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        var emptyPresentation = await reader.ReadPresentationAsync();
        Assert.True(emptyPresentation.Succeeded, emptyPresentation.Refusal?.Message);
        emptyPresentation.Value!.Dispose();
        Assert.Equal(beforeNoDraft, ReadPendingFitRows(database));

        var added = PendingChanges.Put(new PutPendingChangeRequest(projectPath, "1.0", "none",
            new ChangeIntent("readonly-fit-change", AnalysisChangeKinds.Reject,
                CanonicalId.FromGuid(text.AnalysedWordformId).Value, SeededProject.AnalysedWordForm,
                StoredAnalysisId: CanonicalId.FromGuid(text.ApprovedAnalysisId).Value))
        {
            ExpectedContext = reader.Context.ExpectedWriteContext(),
        });
        Assert.True(added.Succeeded, added.Refusal?.Message);
        fits.Clear();
        var beforeMissingFit = ReadPendingFitRows(database);
        Assert.Empty(beforeMissingFit);

        var draftPresentation = await reader.ReadPresentationAsync();
        Assert.True(draftPresentation.Succeeded, draftPresentation.Refusal?.Message);
        using var draftRead = draftPresentation.Value!;
        Assert.Single(draftRead.Value.PendingChanges.FitSummary);
        Assert.Equal(beforeMissingFit, ReadPendingFitRows(database));
        Assert.Null(fits.Get(draftRead.Value.PendingChanges.Revision,
            File.GetLastWriteTimeUtc(projectPath).Ticks, baselineIdentity));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task PresentationDiagnosticsCountNestedReadsAndMaterializedDetails(
        bool hasReadMarks, bool hasCachedFits)
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        if (hasReadMarks)
        {
            var paragraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>()
                .GetObject(text.FirstParagraphId);
            NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () => paragraph.ParseIsCurrent = true);
        }
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        new NamedSelectionRepository(database).SetDefault("Default", [text.TextId], [],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
        RecordSelectionAssessment(database, project, capture.Value!.Token, "presentation-count-root", null,
            DateTimeOffset.UtcNow);
        var context = new ExpectedContext(capture.Value.Token);
        if (hasReadMarks)
        {
            var marked = ReadStateCommands.Execute(new WordReadStateRequest(projectPath, text.TextId,
                IsRead: true) { ExpectedContext = context });
            Assert.True(marked.Succeeded, marked.Refusal?.Message);
            Assert.NotEmpty(marked.Value!.ReadOccurrences);
        }

        var put = PendingChanges.Put(new PutPendingChangeRequest(projectPath, "1.0", "none",
            new ChangeIntent("presentation-count-change", AnalysisChangeKinds.Reject,
                CanonicalId.FromGuid(text.AnalysedWordformId).Value, SeededProject.AnalysedWordForm,
                StoredAnalysisId: CanonicalId.FromGuid(text.ApprovedAnalysisId).Value))
        {
            ExpectedContext = context,
        });
        Assert.True(put.Succeeded, put.Refusal?.Message);
        var fit = PendingChanges.Load(new PendingChangesRequest(projectPath, "1.0"));
        Assert.True(fit.Succeeded, fit.Refusal?.Message);
        Assert.NotEmpty(fit.Value!.FitSummary);
        if (!hasCachedFits) new PendingChangeFitRepository(database).Clear();

        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(
            ProjectWorkspaceKey.Compute(project), [text.TextId], []) { ProjectPath = projectPath });
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        var before = reader.Diagnostics;
        using var sharedObservation = ScaleCountHarness.ObserveRepositoryReads();
        var presentation = await reader.ReadPresentationAsync();
        var sharedReads = sharedObservation.Snapshot();
        Assert.True(presentation.Succeeded, presentation.Refusal?.Message);
        using var result = presentation.Value!;
        var after = reader.Diagnostics;

        Assert.Equal(hasReadMarks ? 30 : 18, sharedReads.Queries);
        Assert.Equal(9 + (hasReadMarks ? 13 : 0) + (hasCachedFits ? 1 : 0),
            sharedReads.RecordsDeserialized);
        Assert.Equal(sharedReads.Queries, after.QueriesIssued - before.QueriesIssued);
        Assert.Equal(sharedReads.RecordsDeserialized,
            after.RepositoryRecordsDeserialized - before.RepositoryRecordsDeserialized);
        Assert.Equal(hasReadMarks ? 1 : 0, after.TextRowsDeserialized - before.TextRowsDeserialized);
        Assert.Equal(hasReadMarks ? 2 : 0, after.WordformRowsDeserialized - before.WordformRowsDeserialized);
        Assert.Equal(hasReadMarks ? 2 : 0, after.SourceLines - before.SourceLines);
        Assert.Equal(hasReadMarks ? 3 : 0, after.SourceTokens - before.SourceTokens);
        Assert.Equal(hasReadMarks ? 1 : 0, after.TextAnalyses - before.TextAnalyses);
        Assert.Equal(hasReadMarks ? 1 : 0, after.WordformAnalyses - before.WordformAnalyses);
        Assert.Equal(hasReadMarks ? 4 : 0, after.Morphs - before.Morphs);
        Assert.Equal(hasReadMarks, result.Value.ReadOccurrences[text.TextId].Count > 0);
        Assert.Single(result.Value.PendingChanges.FitSummary);
        Assert.Equal(hasCachedFits, ReadPendingFitRows(database).Count > 0);
    }

    [Theory]
    [InlineData("incomplete")]
    [InlineData("missing")]
    public async Task PresentationFitMissReturnsProjectStoreRefusalsWithoutChangingDraftOrFits(string fault)
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        new NamedSelectionRepository(database).SetDefault("Default", [text.TextId], [],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
        RecordSelectionAssessment(database, project, capture.Value!.Token, "presentation-fault-root", null,
            DateTimeOffset.UtcNow);
        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(
            ProjectWorkspaceKey.Compute(project), [text.TextId], []) { ProjectPath = projectPath });
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        var added = PendingChanges.Put(new PutPendingChangeRequest(projectPath, "1.0", "none",
            new ChangeIntent("presentation-fault-change", AnalysisChangeKinds.Reject,
                CanonicalId.FromGuid(text.AnalysedWordformId).Value, SeededProject.AnalysedWordForm,
                StoredAnalysisId: CanonicalId.FromGuid(text.ApprovedAnalysisId).Value))
        {
            ExpectedContext = reader.Context.ExpectedWriteContext(),
        });
        Assert.True(added.Succeeded, added.Refusal?.Message);
        var fitRepository = new PendingChangeFitRepository(database);
        fitRepository.Clear();
        var proposalRepository = new ProposalRepository(database);
        var draftBefore = proposalRepository.GetDraft(PendingChanges.DraftName);
        var fitsBefore = ReadPendingFitRows(database);
        Assert.Empty(fitsBefore);

        cache.Dispose();
        if (fault == "missing")
            File.Delete(projectPath);
        else
        {
            File.WriteAllText(projectPath, "<?xml version=\"1.0\"?><languageproject>");
            File.SetLastWriteTimeUtc(projectPath, DateTime.UtcNow.AddMinutes(10));
        }

        var presentation = await reader.ReadPresentationAsync();
        Assert.False(presentation.Succeeded);
        Assert.Equal(fault == "incomplete" ? "change.project-saving" : "project.operation-io",
            presentation.Refusal!.Code);
        Assert.Equal(draftBefore, proposalRepository.GetDraft(PendingChanges.DraftName));
        Assert.Equal(fitsBefore, ReadPendingFitRows(database));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PresentationKeepsSelectedRuleTimingsAndStoredParseWarnings(bool saveGrammarCheck)
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        new NamedSelectionRepository(database).SetDefault("Default", [text.TextId], [],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
        var current = CurrentEvidenceQuery.ReadCurrentEvidence(database, project,
            includeResolvedReadings: false, includeWordContext: false);
        Assert.True(current.Succeeded, current.Refusal?.Message);
        var selection = current.Value!.Selection!.Selection;
        var form = SeededProject.AnalysedWordForm;
        Assert.Contains(form, selection.Words);
        var oldTiming = new AssessmentObjectTiming("phon_rule", "timing-before-rerun", "authored",
            "forward", "rule", form, 1, 1, 20_000_000);
        var newTiming = new AssessmentObjectTiming("phon_rule", "timing-after-rerun", "authored",
            "forward", "rule", form, 2, 2, 40_000_000);
        var rootAllomorphWarning = StoredAllomorphWarning(form);
        var replacementAllomorphWarning = StoredAllomorphWarning(form);
        var root = NewAssessment("warning-root", capture.Value!.Token, selection,
            selection.Words.Select(word => new AssessedWord(word, "analysed", [])).ToArray(), null,
            DateTimeOffset.UtcNow.AddMinutes(-1)) with
        {
            Invocation = Invocation("warning-root-invocation", rootAllomorphWarning),
            ObjectTimings = [oldTiming],
        };
        new AssessmentRepository(database).Record(root);
        var replacementSelection = SIL.Motif.Host.Corpus.Selection.Create("Default", [form]);
        var replacement = NewAssessment("warning-replacement", capture.Value.Token, replacementSelection,
            [new AssessedWord(form, "analysed", [])], root.AssessmentId, DateTimeOffset.UtcNow) with
        {
            Invocation = Invocation("warning-replacement-invocation", replacementAllomorphWarning),
            ObjectTimings = [newTiming],
        };
        new AssessmentRepository(database).Record(replacement);

        var timingWarnings = new[]
        {
            TimingWarning("selection-reader-old-timing", new TraceTimingKey(oldTiming.Kind, oldTiming.Key)),
            TimingWarning("selection-reader-new-timing", new TraceTimingKey(newTiming.Kind, newTiming.Key)),
        };
        var baselineJson = System.Text.Json.JsonSerializer.Serialize(capture.Value.Token, MotifJson.CreateOptions());
        if (saveGrammarCheck)
            new GrammarCheckRepository(database).Save(baselineJson, selection.Sha256, null,
                new GrammarCheckResponse(timingWarnings, true));

        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(
            ProjectWorkspaceKey.Compute(project), [text.TextId], []) { ProjectPath = projectPath });
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        var presentation = await reader.ReadPresentationAsync();
        Assert.True(presentation.Succeeded, presentation.Refusal?.Message);
        using var result = presentation.Value!;
        var summary = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(summary.Succeeded, summary.Refusal?.Message);
        using var summaryRead = summary.Value!;
        var row = Assert.Single(summaryRead.Value.Words,
            word => word.Key.WordformId == text.AnalysedWordformId);
        var rootWarning = Assert.Single(result.Value.Warnings,
            warning => warning.Code == GrammarFindingCodes.ParseAllomorphUnsegmentable &&
                warning.Text == rootAllomorphWarning);
        Assert.NotNull(rootWarning.YourWords);
        Assert.Contains(result.Value.Warnings, warning =>
            warning.Code == GrammarFindingCodes.ParseAllomorphUnsegmentable &&
            warning.Text == replacementAllomorphWarning);
        if (saveGrammarCheck)
        {
            Assert.Contains(result.Value.WarningsByWord[row.Key], warning =>
                warning.Code == "selection-reader-new-timing");
            Assert.DoesNotContain(result.Value.WarningsByWord[row.Key], warning =>
                warning.Code == "selection-reader-old-timing");
        }
        else
        {
            Assert.DoesNotContain(result.Value.Warnings, warning =>
                warning.Code is "selection-reader-old-timing" or "selection-reader-new-timing");
        }
    }

    private static BatchInvocationEvidence Invocation(string id, string warning) =>
        new(id, "source", "digest", "digest", "words", "digest", "tsv", "digest", "stderr", "digest",
            1000, SIL.Motif.Contract.Assess.StepCap.Default, 1, false)
        {
            GrammarWarnings = warning,
        };

    private static string StoredAllomorphWarning(string form) =>
        $"warning: Allomorph '{form}' could not be segmented with this project's phonemes: " +
        $"allomorph \"{Guid.NewGuid():D}\": parse failed";

    private static GrammarWarning TimingWarning(string code, TraceTimingKey key) =>
        new(GrammarDiagnosticLevel.Warning, code,
            [new GrammarWarningPart(key.Key, GrammarWarningPartRole.Object, key.Key, "PhRegularRule")
            {
                Reach = new WarningReach(WarningWordsPath.RuleTimes) { TimingKeys = [key] },
            }], [], "rule timing warning")
        {
            Code = code,
        };

    private static IReadOnlyList<string> ReadPendingFitRows(MotifDatabase database)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT DraftRevision, ProjectLastWriteUtcTicks, BaselineIdentity, FitSummaryJson
            FROM PendingChangeFits ORDER BY DraftRevision, ProjectLastWriteUtcTicks, BaselineIdentity;
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<string>();
        while (reader.Read())
            rows.Add(string.Join("\u001f", reader.GetString(0), reader.GetInt64(1), reader.GetString(2),
                reader.GetString(3)));
        return rows;
    }

    [Fact]
    public async Task OlderPresentationReadCannotPublishWithANewerStamp()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        new NamedSelectionRepository(database).SetDefault("Default", [text.TextId], [],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
        RecordSelectionAssessment(database, project, capture.Value!.Token, "stamp-race-root", null,
            DateTimeOffset.UtcNow);

        using var reached = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var armed = 0;
        var paused = 0;
        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(
            ProjectWorkspaceKey.Compute(project), [text.TextId], [])
        {
            ProjectPath = projectPath,
            PauseAt = point =>
            {
                if (point != SelectionReaderPausePoint.AfterPresentationStateLoad ||
                    Volatile.Read(ref armed) == 0 || Interlocked.CompareExchange(ref paused, 1, 0) != 0)
                    return;
                reached.Set();
                if (!resume.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The older presentation snapshot was not released.");
            },
        });
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        Volatile.Write(ref armed, 1);
        var olderTask = reader.ReadPresentationAsync();
        Assert.True(await Task.Run(() => reached.Wait(TimeSpan.FromSeconds(10))),
            "The first presentation read did not reach its publication pause.");

        var pending = PendingChanges.Load(new PendingChangesRequest(projectPath, "1.0"));
        Assert.True(pending.Succeeded, pending.Refusal?.Message);
        var put = PendingChanges.Put(new PutPendingChangeRequest(projectPath, "1.0", pending.Value!.Revision,
            new ChangeIntent("stamp-race-change", AnalysisChangeKinds.Reject,
                CanonicalId.FromGuid(text.AnalysedWordformId).Value, SeededProject.AnalysedWordForm,
                StoredAnalysisId: CanonicalId.FromGuid(text.ApprovedAnalysisId).Value))
        {
            ExpectedContext = reader.Context.ExpectedWriteContext(),
        });
        Assert.True(put.Succeeded, put.Refusal?.Message);
        var newer = await reader.ReadPresentationAsync();
        Assert.True(newer.Succeeded, newer.Refusal?.Message);
        using var latest = newer.Value!;
        Assert.Single(latest.Value.PendingChanges.Changes);

        resume.Set();
        var older = await olderTask;
        Assert.False(older.Succeeded);
        Assert.Equal("selection-reader.presentation-changed", older.Refusal!.Code);
        Assert.Equal(latest.PresentationStamp, reader.Diagnostics.PresentationStamp);
    }

    [Fact]
    public async Task PresentationPublicationRejectsAnOlderSnapshotThatFinishesLater()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        new NamedSelectionRepository(database).SetDefault("Default", [text.TextId], [],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
        RecordSelectionAssessment(database, project, capture.Value!.Token, "snapshot-order-root", null,
            DateTimeOffset.UtcNow);

        using var firstValidationReached = new ManualResetEventSlim();
        using var resumeFirstValidation = new ManualResetEventSlim();
        using var oldSnapshotReached = new ManualResetEventSlim();
        using var resumeOldSnapshot = new ManualResetEventSlim();
        var validationPauseUsed = 0;
        var snapshotPauseUsed = 0;
        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(
            ProjectWorkspaceKey.Compute(project), [text.TextId], [])
        {
            ProjectPath = projectPath,
            PauseAt = point =>
            {
                if (point == SelectionReaderPausePoint.AfterContextValidation &&
                    Interlocked.CompareExchange(ref validationPauseUsed, 1, 0) == 0)
                {
                    firstValidationReached.Set();
                    if (!resumeFirstValidation.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("The first presentation read was not resumed.");
                }
                if (point == SelectionReaderPausePoint.AfterPresentationStateLoad &&
                    Interlocked.CompareExchange(ref snapshotPauseUsed, 1, 0) == 0)
                {
                    oldSnapshotReached.Set();
                    if (!resumeOldSnapshot.Wait(TimeSpan.FromSeconds(10)))
                        throw new TimeoutException("The old presentation snapshot was not resumed.");
                }
            },
        });
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;

        var first = reader.ReadPresentationAsync();
        Assert.True(await Task.Run(() => firstValidationReached.Wait(TimeSpan.FromSeconds(10))),
            "The first read did not pause before loading mutable state.");
        var oldSnapshot = reader.ReadPresentationAsync();
        Assert.True(await Task.Run(() => oldSnapshotReached.Wait(TimeSpan.FromSeconds(10))),
            "The second read did not load the old mutable state.");

        var pending = PendingChanges.Load(new PendingChangesRequest(projectPath, "1.0"));
        Assert.True(pending.Succeeded, pending.Refusal?.Message);
        var put = PendingChanges.Put(new PutPendingChangeRequest(projectPath, "1.0", pending.Value!.Revision,
            new ChangeIntent("snapshot-order-change", AnalysisChangeKinds.Reject,
                CanonicalId.FromGuid(text.AnalysedWordformId).Value, SeededProject.AnalysedWordForm,
                StoredAnalysisId: CanonicalId.FromGuid(text.ApprovedAnalysisId).Value))
        {
            ExpectedContext = reader.Context.ExpectedWriteContext(),
        });
        Assert.True(put.Succeeded, put.Refusal?.Message);

        resumeFirstValidation.Set();
        var latest = await first;
        Assert.True(latest.Succeeded, latest.Refusal?.Message);
        using var latestRead = latest.Value!;
        Assert.Single(latestRead.Value.PendingChanges.Changes);

        resumeOldSnapshot.Set();
        var stale = await oldSnapshot;
        Assert.False(stale.Succeeded);
        Assert.Equal("selection-reader.presentation-changed", stale.Refusal!.Code);
        Assert.Equal(latestRead.PresentationStamp, reader.Diagnostics.PresentationStamp);
    }

    [Fact]
    public void ExpectedContextMismatchLeavesReadAndPendingStoresUnchanged()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var baselineToken = capture.Value!.Token;
        var stale = new ExpectedContext(new BaselineToken(baselineToken.ProjectIdentity,
            baselineToken.SemanticSnapshotDigest, baselineToken.ProjectionVersion, baselineToken.CapturedUtc,
            "sha256:" + new string('0', 64), baselineToken.CapturedHostSessionId,
            baselineToken.CapturedEditGeneration));

        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);
        var workspaceKey = ProjectWorkspaceKey.Compute(project);
        var proposalRepository = new ProposalRepository(database);
        var draftId = CanonicalId.Mint();
        Assert.True(proposalRepository.TryCreateDraftForBaseline("context-guard", draftId, "{}",
            workspaceKey, baselineToken));
        Assert.False(proposalRepository.TrySaveDraftForBaseline("context-guard", "{}", "{\"changed\":true}",
            workspaceKey, stale.Baseline));
        Assert.Equal("{}", proposalRepository.GetDraft("context-guard").ProposalJson);

        var currentContext = ReadStateCommands.Execute(new WordReadStateRequest(projectPath, text.TextId)
            { ExpectedContext = new ExpectedContext(baselineToken) });
        Assert.True(currentContext.Succeeded, currentContext.Refusal?.Message);
        var anchor = new OccurrenceAnchor(text.TextId, Guid.NewGuid(), Guid.NewGuid(), 0);
        var readRepository = new ReadStateRepository(database);
        readRepository.Upsert(new ReadOccurrenceRecord(anchor, "{}"));
        var before = readRepository.Get(anchor);

        var read = ReadStateCommands.Execute(new WordReadStateRequest(projectPath, text.TextId, [anchor],
            IsRead: false) { ExpectedContext = stale });
        Assert.False(read.Succeeded);
        Assert.Equal("texts.evidence-changed", read.Refusal!.Code);
        Assert.Equal(before, readRepository.Get(anchor));

        var pending = PendingChanges.Load(new PendingChangesRequest(projectPath, "1.0"));
        Assert.True(pending.Succeeded, pending.Refusal?.Message);
        var put = PendingChanges.Put(new PutPendingChangeRequest(projectPath, "1.0", pending.Value!.Revision,
            new ChangeIntent("stale-context", "remove-analysis", "wordform", "word",
                StoredAnalysisId: "analysis")) { ExpectedContext = stale });
        Assert.False(put.Succeeded);
        Assert.Equal("texts.evidence-changed", put.Refusal!.Code);
        var unchanged = PendingChanges.Load(new PendingChangesRequest(projectPath, "1.0"));
        Assert.True(unchanged.Succeeded, unchanged.Refusal?.Message);
        Assert.Equal(pending.Value.Revision, unchanged.Value!.Revision);
    }

    [Theory]
    [InlineData("refresh", false)]
    [InlineData("refresh", true)]
    [InlineData("default-declaration", false)]
    [InlineData("default-declaration", true)]
    [InlineData("subset-replacement", false)]
    [InlineData("subset-replacement", true)]
    public async Task DetailReadsRefuseEvidenceChangedAfterTheirInitialValidation(
        string change, bool cached)
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        new NamedSelectionRepository(database).SetDefault("Default", [text.TextId], [],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
        AssessmentRecord? root = null;
        if (change == "subset-replacement")
            root = RecordSelectionAssessment(database, project, capture.Value!.Token, "detail-race-root", null,
                DateTimeOffset.UtcNow.AddMinutes(-1));

        using var reached = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var armed = 0;
        var paused = 0;
        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(
            ProjectWorkspaceKey.Compute(project), [text.TextId], [])
        {
            ProjectPath = projectPath,
            PauseAt = point =>
            {
                if (point != SelectionReaderPausePoint.AfterContextValidation ||
                    Volatile.Read(ref armed) == 0 || Interlocked.CompareExchange(ref paused, 1, 0) != 0)
                    return;
                reached.Set();
                if (!resume.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The detail read was not released after context validation.");
            },
        });
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        var range = new TextLineRange(1, 1, 0, 32);
        if (cached)
        {
            var warm = await reader.ReadLinesAsync(text.TextId, range);
            Assert.True(warm.Succeeded, warm.Refusal?.Message);
            warm.Value!.Dispose();
        }

        Volatile.Write(ref armed, 1);
        var pendingRead = reader.ReadLinesAsync(text.TextId, range);
        try
        {
            Assert.True(await Task.Run(() => reached.Wait(TimeSpan.FromSeconds(10))),
                "The detail read did not reach its context-validation pause.");
            switch (change)
            {
                case "refresh":
                    SeededProject.SeedText(cache, pristine.Seed);
                    new FwDataProjectLoader().Save(cache);
                    var refreshed = await new BaselineRefresh(new BaselineRepository(database), managedRoot)
                        .RefreshAsync(cache, project, CancellationToken.None);
                    Assert.NotEqual(capture.Value!.Token, refreshed);
                    break;
                case "default-declaration":
                    new NamedSelectionRepository(database).SetDefault("Changed", [text.TextId], [],
                        new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
                    break;
                case "subset-replacement":
                    var selected = root!.Selection.Words.Take(1).ToArray();
                    var subset = SIL.Motif.Host.Corpus.Selection.Create("Default", selected);
                    var replacementWords = selected.Select(word => new AssessedWord(word, "analysed", [])).ToArray();
                    new AssessmentRepository(database).Record(NewAssessment("detail-race-replacement",
                        capture.Value!.Token, subset, replacementWords, root.AssessmentId, DateTimeOffset.UtcNow));
                    break;
            }
        }
        finally
        {
            resume.Set();
        }

        var result = await pendingRead;
        Assert.False(result.Succeeded);
        Assert.Equal("texts.evidence-changed", result.Refusal!.Code);
        Assert.Equal(0, reader.Diagnostics.CachedEntries);
        Assert.Equal(0, reader.Diagnostics.LeasedResults);
    }

    [Fact]
    public async Task OrdinaryRowsLinesAndTokensHaveBudgetsIndependentOfExplicitPins()
    {
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var path = cache.ProjectId.Path;
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(path), Path.Combine(_root, "managed"));
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var opened = await SelectionReader.OpenAsync(new OpenSelectionReaderRequest(path, [text.TextId], []));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        await using var reader = opened.Value!;
        var summarized = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(summarized.Succeeded, summarized.Refusal?.Message);
        using var summary = summarized.Value!;
        var models = new List<TrackedSelectionModel>();
        try
        {
            models.Add(new(summary.Lease, SelectionModelKind.Row, SelectionModelUse.Pinned));
            models.Add(new(summary.Lease, SelectionModelKind.Line, SelectionModelUse.Pinned));
            models.AddRange(Enumerable.Range(0, 62).Select(_ => new TrackedSelectionModel(summary.Lease,
                SelectionModelKind.Token, SelectionModelUse.Pinned)));
            foreach (var (kind, count) in new[] { (SelectionModelKind.Row, 128), (SelectionModelKind.Line, 48),
                         (SelectionModelKind.Token, 512) })
            {
                models.AddRange(Enumerable.Range(0, count).Select(_ => new TrackedSelectionModel(summary.Lease, kind)));
                Assert.ThrowsAny<InvalidOperationException>(() => new TrackedSelectionModel(summary.Lease, kind));
            }
            models.Add(new(summary.Lease, SelectionModelKind.Card));
            Assert.Equal(128, reader.Diagnostics.OrdinaryRowModels);
            Assert.Equal(48, reader.Diagnostics.OrdinaryLineModels);
            Assert.Equal(512, reader.Diagnostics.OrdinaryTokenModels);
            Assert.Equal(129, reader.Diagnostics.LiveRowModels);
            Assert.Equal(49, reader.Diagnostics.LiveLineModels);
            Assert.Equal(574, reader.Diagnostics.LiveTokenModels);
            Assert.Equal(64, reader.Diagnostics.LivePinnedModels);
            Assert.Equal(1, reader.Diagnostics.LiveCardModels);
            Assert.ThrowsAny<InvalidOperationException>(() => new TrackedSelectionModel(summary.Lease,
                SelectionModelKind.Token, SelectionModelUse.Pinned));
            Assert.ThrowsAny<InvalidOperationException>(() => new TrackedSelectionModel(summary.Lease, SelectionModelKind.Card));
        }
        finally { foreach (var model in models) model.Dispose(); }
        Assert.Equal(0, reader.Diagnostics.LiveRowModels);
        Assert.Equal(0, reader.Diagnostics.LiveLineModels);
        Assert.Equal(0, reader.Diagnostics.LiveTokenModels);
        Assert.Equal(0, reader.Diagnostics.LivePinnedModels);
        Assert.Equal(0, reader.Diagnostics.LiveCardModels);
    }

    private sealed class TrackedSelectionModel : IDisposable
    {
        private IDisposable? _registration;

        public TrackedSelectionModel(SelectionReadLease lease, SelectionModelKind kind,
            SelectionModelUse use = SelectionModelUse.Ordinary) =>
            _registration = lease.RegisterModel(kind, use);

        public void Dispose() => Interlocked.Exchange(ref _registration, null)?.Dispose();
    }

    private static IReadOnlyList<WeakReference<object>> DisposeReaderWithRetainedModel(
        SelectionReader reader, SelectionReadLease lease)
    {
        var retained = new TrackedSelectionModel(lease, SelectionModelKind.Card, SelectionModelUse.Pinned);
        var references = ScaleCountHarness.ObserveViewModels([retained], typeof(TrackedSelectionModel));
        Assert.Equal(1, ScaleCountHarness.CountLiveViewModels(references, typeof(TrackedSelectionModel)));
        reader.Dispose();
        Assert.Equal(1, reader.Diagnostics.LiveCardModels);
        Assert.Equal(1, reader.Diagnostics.LivePinnedModels);
        Assert.Equal(1, ScaleCountHarness.CountLiveViewModels(references, typeof(TrackedSelectionModel)));
        retained.Dispose();
        return references;
    }

    [Theory]
    [InlineData("refresh")]
    [InlineData("default-declaration")]
    [InlineData("subset-replacement")]
    public async Task OpenUsesOneFiniteSnapshotAndLaterReadsRefuseChangedEvidence(string change)
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        var projectKey = ProjectWorkspaceKey.Compute(project);
        AssessmentRecord? root = null;
        if (change != "refresh")
        {
            new NamedSelectionRepository(database).SetDefault("Default", [text.TextId], [],
                new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
            if (change == "subset-replacement")
                root = RecordSelectionAssessment(database, project, capture.Value!.Token, "root", null,
                    DateTimeOffset.UtcNow.AddMinutes(-1));
        }

        using var reached = new ManualResetEventSlim();
        using var resume = new ManualResetEventSlim();
        var request = new SelectionReadRequest(projectKey, [text.TextId], [])
        {
            ProjectPath = projectPath,
            PauseAt = point =>
            {
                if (point != SelectionReaderPausePoint.AfterIndexSnapshot) return;
                reached.Set();
                if (!resume.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The paused Selection snapshot was not released.");
            },
        };
        var opening = SelectionReader.OpenAsync(database, request);
        try
        {
            Assert.True(await Task.Run(() => reached.Wait(TimeSpan.FromSeconds(10))),
                "Open did not reach its snapshot pause.");
            switch (change)
            {
                case "refresh":
                    SeededProject.SeedText(cache, pristine.Seed);
                    new FwDataProjectLoader().Save(cache);
                    var refreshedToken = await new BaselineRefresh(new BaselineRepository(database), managedRoot)
                        .RefreshAsync(cache, project, CancellationToken.None);
                    Assert.NotEqual(capture.Value!.Token, refreshedToken);
                    break;
                case "default-declaration":
                    new NamedSelectionRepository(database).SetDefault("Changed", [text.TextId], [],
                        new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
                    break;
                case "subset-replacement":
                    var selected = root!.Selection.Words.Take(1).ToArray();
                    var selection = SIL.Motif.Host.Corpus.Selection.Create("Default", selected);
                    var replacementWords = selected.Select(word => new AssessedWord(word, "analysed", [])).ToArray();
                    new AssessmentRepository(database).Record(NewAssessment("replacement", capture.Value!.Token,
                        selection, replacementWords, "root", DateTimeOffset.UtcNow));
                    break;
            }
        }
        finally
        {
            resume.Set();
        }

        var opened = await opening;
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        Assert.Equal(capture.Value!.Token, reader.Context.Baseline);
        var summary = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.False(summary.Succeeded);
        Assert.Equal("texts.evidence-changed", summary.Refusal!.Code);

        if (change == "subset-replacement")
        {
            var staleContext = reader.Context.ExpectedWriteContext();
            var anchor = new OccurrenceAnchor(text.TextId, Guid.NewGuid(), Guid.NewGuid(), 0);
            var readRepository = new ReadStateRepository(database);
            readRepository.Upsert(new ReadOccurrenceRecord(anchor, "{}"));
            var beforeRead = readRepository.Get(anchor);
            var staleRead = ReadStateCommands.Execute(new WordReadStateRequest(projectPath, text.TextId,
                [anchor], IsRead: false) { ExpectedContext = staleContext });
            Assert.False(staleRead.Succeeded);
            Assert.Equal(beforeRead, readRepository.Get(anchor));

            var pending = PendingChanges.Load(new PendingChangesRequest(projectPath, "1.0"));
            Assert.True(pending.Succeeded, pending.Refusal?.Message);
            var put = PendingChanges.Put(new PutPendingChangeRequest(projectPath, "1.0", pending.Value!.Revision,
                new ChangeIntent("stale-selection-context", "remove-analysis", "wordform", "word",
                    StoredAnalysisId: "analysis")) { ExpectedContext = staleContext });
            Assert.False(put.Succeeded);
            Assert.Equal("texts.evidence-changed", put.Refusal!.Code);
            var unchanged = PendingChanges.Load(new PendingChangesRequest(projectPath, "1.0"));
            Assert.True(unchanged.Succeeded, unchanged.Refusal?.Message);
            Assert.Equal(pending.Value.Revision, unchanged.Value!.Revision);
        }
    }

    [Fact]
    public async Task OpenRejectsASuppliedSnapshotAfterItsDefaultDeclarationChanges()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        var projectKey = ProjectWorkspaceKey.Compute(project);
        new NamedSelectionRepository(database).SetDefault("Default", [text.TextId], [],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
        var snapshot = CurrentEvidenceQuery.ReadCurrentEvidence(database, project,
            includeResolvedReadings: false, includeWordContext: false);
        Assert.True(snapshot.Succeeded, snapshot.Refusal?.Message);
        new NamedSelectionRepository(database).SetDefault("Changed", [text.TextId], [],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);

        var opened = await SelectionReader.OpenAsync(database,
            new SelectionReadRequest(projectKey, [text.TextId], [])
            {
                ProjectPath = projectPath,
                EvidenceSnapshot = snapshot.Value,
            });

        Assert.False(opened.Succeeded);
        Assert.Equal("texts.evidence-changed", opened.Refusal!.Code);
    }

    [Fact]
    public async Task OpenReadsOnlyTheSelectedAssessmentAndReplacementRows()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        new NamedSelectionRepository(database).SetDefault("Default", [text.TextId], [],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
        var root = RecordSelectionAssessment(database, project, capture.Value!.Token, "selected-root", null,
            DateTimeOffset.UtcNow.AddMinutes(-2));
        var assessmentRepository = new AssessmentRepository(database);
        for (var number = 0; number < 20; number++)
        {
            var unrelatedForm = $"unrelated-{number}";
            var unrelatedSelection = SIL.Motif.Host.Corpus.Selection.Create(
                $"Unrelated {number}", [unrelatedForm]);
            assessmentRepository.Record(NewAssessment($"unrelated-{number}", capture.Value.Token,
                unrelatedSelection, [new AssessedWord(unrelatedForm, "analysed", [])], null,
                DateTimeOffset.UtcNow.AddMinutes(-1)));
        }
        var replacedForm = root.Selection.Words[0];
        var replacementSelection = SIL.Motif.Host.Corpus.Selection.Create("Default", [replacedForm]);
        assessmentRepository.Record(NewAssessment("selected-replacement", capture.Value.Token,
            replacementSelection, [new AssessedWord(replacedForm, "analysed", [])], root.AssessmentId,
            DateTimeOffset.UtcNow));

        using var repositoryObservation = ScaleCountHarness.ObserveRepositoryReads();
        var opened = await SelectionReader.OpenAsync(database,
            new SelectionReadRequest(ProjectWorkspaceKey.Compute(project), [text.TextId], []));
        var reads = repositoryObservation.Snapshot();

        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        Assert.Equal(2, reader.Diagnostics.AssessmentRowsDeserialized);
        Assert.Equal(root.Selection.Words.Count + 1, reader.Diagnostics.AssessmentWordsDeserialized);
        Assert.Equal(reads.Queries, reader.Diagnostics.QueriesIssued);
        Assert.Equal(reads.RecordsDeserialized, reader.Diagnostics.RepositoryRecordsDeserialized);
        Assert.InRange(reader.Diagnostics.QueriesIssued, 1, 12);
        Assert.InRange(reader.Diagnostics.RepositoryRecordsDeserialized, 1, root.Selection.Words.Count + 16);
    }

    [Fact]
    public void CompactUnmarkedAnalysisOffersApproveWithoutMorphologyDisplay()
    {
        var index = new SelectionIndexBuilder();
        var wordId = Guid.NewGuid();
        var analysis = Analysis("unknown");
        index.AddWordform(Wordform(wordId, [analysis], 1), [new WritingSystemText("word", "en")]);
        index.AddWord("word");
        index.AddAssessmentWords([new AssessedWord("word", "analysed", [])
        {
            Morphology = new ParseWordEvidence("test", 0, "word", 1, false, false, false,
                [new ParseAnalysis([])], []),
        }]);

        var word = Assert.Single(index.Build().Words);

        Assert.Equal(SelectionAnalysisClass.Same, word.Actions.Classification!.Class);
        Assert.Equal(ReadingGrade.Candidate, Assert.Single(word.Actions.StoredOpinions).Value);
        Assert.True(word.Actions.NeedsALook);
    }

    [Fact]
    public void CompactIndexCountsMultilingualMembershipsAndResolvesExactLocations()
    {
        var firstTextId = Guid.NewGuid();
        var secondTextId = Guid.NewGuid();
        var firstWordformId = Guid.NewGuid();
        var homographId = Guid.NewGuid();
        var firstParagraph = Guid.NewGuid();
        var firstSegment = Guid.NewGuid();
        var secondParagraph = Guid.NewGuid();
        var secondSegment = Guid.NewGuid();
        var greekForm = "λόγος".Normalize(NormalizationForm.FormD);
        var approved = Analysis("approved");
        var index = new SelectionIndexBuilder();
        var firstFacts = Wordform(firstWordformId, [approved], 0);
        var homographFacts = Wordform(homographId, [], 1);
        var morphology = new ParseWordEvidence("test", 0, greekForm, 1, false, false, false,
            [new ParseAnalysis([])], []);

        var firstIndex = Index(firstTextId, "First", [new TextWordsProjectedLine(1, "", [
            Token("λόγος", firstWordformId, "approved", 0,
                new WritingSystemText("λόγος", "grc"), new WritingSystemText("logos", "en")),
            Token(",", null, null, 1),
            Token("λόγος", homographId, "unanalysed", 2,
                new WritingSystemText("λόγος", "grc")),
        ], firstParagraph, firstSegment, false)], firstFacts, homographFacts);
        index.AddText(firstIndex);
        foreach (var wordform in firstIndex.Wordforms) index.AddWordform(wordform);
        var secondIndex = Index(secondTextId, "Second", [new TextWordsProjectedLine(1, "", [
            Token("λόγος", firstWordformId, "approved", 0,
                new WritingSystemText("λόγος", "grc")),
        ], secondParagraph, secondSegment, false)], firstFacts);
        index.AddText(secondIndex);
        foreach (var wordform in secondIndex.Wordforms) index.AddWordform(wordform);
        index.AddWord("  cafe\u0301 ");
        index.AddWord(greekForm);
        index.AddAssessmentWords([new AssessedWord(greekForm, "analysed", [])
        {
            ProjectStanding = ProjectStanding.Approved,
            Morphology = morphology,
        }]);
        var conflictingIndex = new SelectionIndexBuilder();
        conflictingIndex.AddWordform(firstFacts);
        Assert.Throws<InvalidDataException>(() => conflictingIndex.AddWordform(Wordform(firstWordformId,
            [approved], 1)));

        var changedIdentity = approved with { Identity = approved.Identity! with { WritingSystem = "other" } };
        Assert.Throws<InvalidDataException>(() => conflictingIndex.AddWordform(Wordform(firstWordformId,
            [changedIdentity], 0)));
        var equivalentIdentity = approved with { Identity = approved.Identity! with { Morphs = [] } };
        conflictingIndex.AddWordform(Wordform(firstWordformId, [equivalentIdentity], 0));

        var summary = index.Build();

        Assert.Equal(3, summary.DistinctFormCount);
        Assert.Equal(5, summary.WordRowCount);
        Assert.Equal(3, summary.PhysicalOccurrenceCount);
        Assert.Equal(4, summary.MembershipCount);
        Assert.Equal(3, summary.SourcePositions.Count);
        Assert.Equal(3, summary.SourcePositions.Select(position => position.Location.Anchor).Distinct().Count());
        Assert.Equal(2, summary.SourcePositions.Count(position => position.Word ==
            new TextWordKey(firstWordformId, greekForm, "grc")));
        Assert.Equal(2, summary.Texts[0].DistinctFormCount);
        Assert.Equal(2, summary.Texts[0].OccurrenceCount);
        var greek = Assert.Single(summary.Words,
            row => row.Key == new TextWordKey(firstWordformId, greekForm, "grc"));
        Assert.Equal(ProjectStanding.Approved, greek.ProjectStanding);
        Assert.Equal(SelectionAnalysisClass.Same, greek.Actions.Classification!.Class);
        Assert.Equal(2, greek.OccurrenceCount);
        var homograph = Assert.Single(summary.Words,
            row => row.Key == new TextWordKey(homographId, greekForm, "grc"));
        Assert.Equal(ProjectStanding.Candidate, homograph.ProjectStanding);
        Assert.Equal(1, homograph.OccurrenceCount);
        Assert.Equal(ProjectStanding.Candidate, homograph.Assessment!.Comparison!.Standing);
        Assert.Equal(SelectionAnalysisClass.Different, homograph.Actions.Classification!.Class);
        Assert.True(Assert.Single(homograph.Actions.Classification.Readings).IsParserOnly);
        Assert.True(Assert.Single(greek.Actions.Classification.Readings).MatchesStored);
        Assert.Equal(AnalysisComparisonAvailability.Available, greek.Assessment!.Comparison!.Availability);
        Assert.Equal(approved.AnalysisId.ToString("D"),
            Assert.Single(greek.Assessment.Comparison.Readings[0].Matches).AnalysisId);
        Assert.True(greek.Actions.CanChangeOpinion);
        Assert.False(greek.Actions.CanAddParserReading);
        Assert.True(homograph.Actions.CanAddParserReading);

        var formOnly = Assert.Single(summary.Words,
            row => row.Key.WordformId is null && row.Key.Form == greekForm);
        Assert.NotNull(formOnly.Actions.Classification);
        Assert.Equal(2, formOnly.Actions.CandidateWordformIds.Count);
        Assert.False(formOnly.Actions.CanAddParserReading);
        Assert.DoesNotContain(index.Build(new SelectionViewRequest(
            Action: SelectionActionFilter.AddParserReading)).Words, row => row.Key == formOnly.Key);

        var read = index.Build(new SelectionViewRequest(ReadState: SelectionReadFilter.Read),
            new HashSet<OccurrenceAnchor> { greek.FirstOccurrence!.Anchor });
        var readGreek = Assert.Single(read.Words,
            row => row.Key == new TextWordKey(firstWordformId, greekForm, "grc"));
        Assert.Equal(1, readGreek.ReadOccurrenceCount);
        Assert.Equal(1, readGreek.UnreadOccurrenceCount);
        Assert.Equal(greek.FirstOccurrence.Anchor, Assert.Single(read.SourcePositions).Location.Anchor);
        var unread = index.Build(new SelectionViewRequest(ReadState: SelectionReadFilter.Unread),
            new HashSet<OccurrenceAnchor> { greek.FirstOccurrence.Anchor });
        Assert.Contains(unread.Words, row => row.Key == greek.Key);
        Assert.Contains(unread.Words, row => row.Key == homograph.Key);
        Assert.DoesNotContain(unread.Words, row => row.IsAddedWord);
        Assert.Equal(2, unread.SourcePositions.Count);
        Assert.DoesNotContain(unread.SourcePositions, position => position.Location.Anchor == greek.FirstOccurrence.Anchor);

        var filtered = index.Build(new SelectionViewRequest(TextIds: new HashSet<Guid> { secondTextId }));
        Assert.Equal(1, Assert.Single(filtered.Words,
            row => row.Key == new TextWordKey(firstWordformId, greekForm, "grc")).OccurrenceCount);
        Assert.Equal([1], filtered.MatchingLineNumbers[secondTextId]);
        var action = index.ResolveActionScope([greek.Key]);
        Assert.True(action.Words[greek.Key].CanRemoveAnalysis);
        Assert.Equal([greek.FirstOccurrence!.Anchor, greek.LastOccurrence!.Anchor], action.Occurrences);
        Assert.Equal([greek.LastOccurrence.Anchor], index.ResolveActionScope([greek.Key],
            included: new HashSet<OccurrenceAnchor> { greek.LastOccurrence.Anchor }).Occurrences);
        Assert.Empty(index.ResolveActionScope([greek.Key], included: new HashSet<OccurrenceAnchor>
        {
            greek.FirstOccurrence.Anchor,
        }, excluded: new HashSet<OccurrenceAnchor> { greek.FirstOccurrence.Anchor }).Occurrences);
        Assert.Equal(homograph.FirstOccurrence!.Anchor, index.Adjacent(greek.FirstOccurrence.Anchor, 1)!.Anchor);
        Assert.Equal(2, index.Adjacent(greek.FirstOccurrence.Anchor, 1)!.TokenOffset);
    }

    [Fact]
    public async Task FormOnlyRowsUseParserClassificationAndReaderOwnedActionAndStateFilters()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        var firstParagraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>()
            .GetObject(text.FirstParagraphId);
        var secondParagraph = cache.ServiceLocator.GetInstance<IStTxtParaRepository>()
            .GetObject(text.SecondParagraphId);
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
        {
            firstParagraph.ParseIsCurrent = true;
            secondParagraph.ParseIsCurrent = true;
        });
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0))
            .OpenOwned(project);
        var form = SeededProject.AnalysedWordForm.Normalize(NormalizationForm.FormD);
        new NamedSelectionRepository(database).SetDefault("Default", [text.TextId], [form],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
        var current = CurrentEvidenceQuery.ReadCurrentEvidence(database, project,
            includeResolvedReadings: false, includeWordContext: false);
        Assert.True(current.Succeeded, current.Refusal?.Message);
        var selection = current.Value!.Selection!.Selection;
        Assert.Contains(form, selection.Words);
        var parserEvidence = new ParseWordEvidence("selection-test", 0, form, 1,
            false, false, false, [new ParseAnalysis([])], []);
        var words = selection.Words.Select(word => word == form
            ? new AssessedWord(word, "analysed", []) { Morphology = parserEvidence }
            : new AssessedWord(word, "analysed", [])).ToArray();
        new AssessmentRepository(database).Record(NewAssessment("form-only-parser-root", capture.Value!.Token,
            selection, words, null, DateTimeOffset.UtcNow));

        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(
            ProjectWorkspaceKey.Compute(project), [text.TextId], [form]) { ProjectPath = projectPath });
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        Assert.Equal([form], reader.Context.AddedWords);
        var allSummary = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(allSummary.Succeeded, allSummary.Refusal?.Message);
        using var allRows = allSummary.Value!;
        Assert.Contains(allRows.Value.Words, row => row.Key.WordformId is null && row.Key.Form == form);
        var filtered = await reader.ReadSummaryAsync(new SelectionViewRequest(
            AnalysisClass: SelectionAnalysisClass.Conflict,
            ComparisonAvailability: AnalysisComparisonAvailability.Available,
            Action: SelectionActionFilter.AddParserReading));
        Assert.True(filtered.Succeeded, filtered.Refusal?.Message);
        using var filteredRead = filtered.Value!;
        var added = Assert.Single(filteredRead.Value.Words,
            row => row.IsAddedWord && row.Key.Form == form);
        Assert.Null(added.Key.WordformId);
        Assert.Equal(SelectionAnalysisClass.Conflict, added.Actions.Classification!.Class);
        Assert.True(added.Actions.CanAddParserReading);
        Assert.Equal(text.AnalysedWordformId, Assert.Single(added.Actions.CandidateWordformIds));
        var action = reader.ResolveActionScope([added.Key]);
        Assert.True(action.Words[added.Key].CanAddParserReading);
        Assert.Empty(action.Occurrences);

        var marked = ReadStateCommands.Execute(new WordReadStateRequest(projectPath, text.TextId,
            IsRead: true) { ExpectedContext = reader.Context.ExpectedWriteContext() });
        Assert.True(marked.Succeeded, marked.Refusal?.Message);
        Assert.NotEmpty(marked.Value!.ReadOccurrences);
        var readRows = await reader.ReadSummaryAsync(new SelectionViewRequest(ReadState: SelectionReadFilter.Read));
        Assert.True(readRows.Succeeded, readRows.Refusal?.Message);
        using var readResult = readRows.Value!;
        Assert.NotEmpty(readResult.Value.Words);
        Assert.All(readResult.Value.Words, row => Assert.True(row.ReadOccurrenceCount > 0));
        Assert.DoesNotContain(readResult.Value.Words, row => row.IsAddedWord);
        var unreadRows = await reader.ReadSummaryAsync(new SelectionViewRequest(ReadState: SelectionReadFilter.Unread));
        Assert.True(unreadRows.Succeeded, unreadRows.Refusal?.Message);
        using var unreadResult = unreadRows.Value!;
        Assert.Empty(unreadResult.Value.Words);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public async Task AddedWordsResolveBaselineTargetsOutsideTheChosenTexts()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);
        var form = SeededProject.AnalysedWordForm.Normalize(NormalizationForm.FormD);
        new NamedSelectionRepository(database).SetDefault("Default", [], [form],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
        var current = CurrentEvidenceQuery.ReadCurrentEvidence(database, project,
            includeResolvedReadings: false, includeWordContext: false);
        Assert.True(current.Succeeded, current.Refusal?.Message);
        var selection = current.Value!.Selection!.Selection;
        new AssessmentRepository(database).Record(NewAssessment("added-outside-texts", capture.Value!.Token,
            selection, [new AssessedWord(form, "analysed", [])
            {
                Morphology = new ParseWordEvidence("selection-test", 0, form, 1,
                    false, false, false, [new ParseAnalysis([])], []),
            }], null, DateTimeOffset.UtcNow));

        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(
            ProjectWorkspaceKey.Compute(project), [], [form]) { ProjectPath = projectPath });
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        var result = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(result.Succeeded, result.Refusal?.Message);
        using var summary = result.Value!;
        var word = Assert.Single(summary.Value.Words);
        Assert.Null(word.Key.WordformId);
        Assert.Equal(text.AnalysedWordformId, Assert.Single(word.Actions.CandidateWordformIds));
        Assert.True(word.Actions.CanChangeOpinion);
        Assert.True(word.Actions.CanRemoveAnalysis);
        Assert.True(word.Actions.CanAddParserReading);
        Assert.False(word.Actions.CanAcceptNewSet);
        Assert.NotEmpty(word.Actions.StoredAnalysisIds);
        Assert.Empty(reader.ResolveActionScope([word.Key]).Occurrences);
        Assert.True(reader.ResolveActionScope([word.Key]).Words[word.Key].CanAddParserReading);
        Assert.Equal(1, reader.Diagnostics.BaselineWordFactCachesOpened);
        Assert.Equal(1, reader.Diagnostics.BaselineWordformFactsRead);
        Assert.Equal(0, reader.Diagnostics.IndexRowsDeserialized);
        Assert.Equal(0, reader.Diagnostics.TextRowsDeserialized);
        Assert.Equal(0, reader.Diagnostics.WordformRowsDeserialized);
        Assert.Equal(0, reader.Diagnostics.Morphs);
        Assert.Equal(0, reader.Diagnostics.SourceTokens);
        var batch = await reader.ReadWordDetailsAsync([word.Key]);
        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        using var wordDetail = batch.Value!;
        Assert.Equal(text.AnalysedWordformId, wordDetail.Value.Wordforms[word.Key].WordformId);
        Assert.Equal(0, reader.Diagnostics.TextRowsDeserialized);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddedWordTargetsIncludeWordsAbsentFromEveryTextAndPreserveAmbiguity(bool ambiguous)
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        var form = ambiguous ? SeededProject.AnalysedWordForm : "stored-outside-texts";
        IWfiWordform? outside = null;
        NonUndoableUnitOfWorkHelper.Do(cache.ActionHandlerAccessor, () =>
            outside = cache.ServiceLocator.GetInstance<IWfiWordformFactory>()
                .Create(TsStringUtils.MakeString(form, cache.DefaultVernWs)));
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);
        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(
            ProjectWorkspaceKey.Compute(project), [text.TextId], [form]));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        var result = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(result.Succeeded, result.Refusal?.Message);
        using var summary = result.Value!;
        var word = Assert.Single(summary.Value.Words, row => row.IsAddedWord);
        Assert.Contains(outside!.Guid, word.Actions.CandidateWordformIds);
        Assert.Equal(ambiguous ? 2 : 1, word.Actions.CandidateWordformIds.Count);
        Assert.Equal(1, reader.Diagnostics.BaselineWordFactCachesOpened);
        Assert.Equal(ambiguous ? 2 : 1, reader.Diagnostics.BaselineWordformFactsRead);
        Assert.False(word.Actions.CanChangeOpinion);
        Assert.False(word.Actions.CanAddParserReading);
        Assert.Equal(0, reader.Diagnostics.TextRowsDeserialized);
        Assert.Equal(0, reader.Diagnostics.WordformRowsDeserialized);
        var batch = await reader.ReadWordDetailsAsync([word.Key]);
        Assert.True(batch.Succeeded, batch.Refusal?.Message);
        using (var wordDetail = batch.Value!)
        {
            if (ambiguous) Assert.Empty(wordDetail.Value.Wordforms);
            else Assert.Equal(outside.Guid, wordDetail.Value.Wordforms[word.Key].WordformId);
        }
        var detailResult = await reader.ReadOccurrencesAsync(word.Key);
        Assert.True(detailResult.Succeeded, detailResult.Refusal?.Message);
        using var detail = detailResult.Value!;
        Assert.Empty(detail.Value.Occurrences);
        Assert.Contains(detail.Value.Wordforms.Values, facts => facts.WordformId == outside.Guid);
        Assert.Equal(ambiguous ? 2 : 1, detail.Value.Wordforms.Values.DistinctBy(facts => facts.WordformId).Count());
    }

    [Fact]
    public async Task WordsWithoutAssessmentRemainInTheNotAssessedFilter()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);
        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(
            ProjectWorkspaceKey.Compute(project), [text.TextId], ["absent-word"]));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        var result = await reader.ReadSummaryAsync(new SelectionViewRequest(
            AnalysisClass: SelectionAnalysisClass.NotAssessed));
        Assert.True(result.Succeeded, result.Refusal?.Message);
        using var summary = result.Value!;
        Assert.Equal(summary.Value.WordRowCount, summary.Value.Words.Count);
        Assert.NotEmpty(summary.Value.Words);
        Assert.Contains(summary.Value.Words, word => word.IsAddedWord);
        Assert.All(summary.Value.Words, word =>
        {
            Assert.Null(word.Assessment);
            Assert.Equal(SelectionAnalysisClass.NotAssessed, word.Actions.Classification!.Class);
            Assert.False(word.Actions.CanAddParserReading);
        });
    }

    [Theory]
    [InlineData("unchanged", true, false)]
    [InlineData("default-declaration", true, false)]
    [InlineData("exploratory", true, false)]
    [InlineData("replacement", false, false)]
    [InlineData("unchanged", true, true)]
    [InlineData("default-declaration", true, true)]
    [InlineData("exploratory", true, true)]
    [InlineData("replacement", false, true)]
    public async Task DisplayedEvidenceKeepsItsNamedRootAcrossTextScopesAndGuardsWrites(
        string change, bool current, bool shownAssessment)
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var first = SeededProject.SeedText(cache, pristine.Seed);
        var other = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);
        var key = ProjectWorkspaceKey.Compute(project);
        new NamedSelectionRepository(database).SetDefault("Default", [first.TextId], [],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
        var root = RecordSelectionAssessment(database, project, capture.Value!.Token, "displayed-root", null,
            DateTimeOffset.UtcNow.AddMinutes(-1));
        var original = await SelectionReader.OpenAsync(database, new SelectionReadRequest(key, [first.TextId], [])
            { ProjectPath = projectPath });
        Assert.True(original.Succeeded, original.Refusal?.Message);
        using var originalReader = original.Value!;
        var named = originalReader.Context.ExpectedWriteContext();
        var shown = new SelectionAssessmentEvidence(named.Baseline, root.AssessmentId, [], []);
        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(key, [other.TextId], [])
        {
            ProjectPath = projectPath,
            DisplayedEvidence = shownAssessment ? null : named,
            ShownAssessment = shownAssessment ? shown : null,
        });
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        Assert.Equal("explicit", reader.Context.SelectionOrigin);
        Assert.Equal(root.AssessmentId, reader.Context.AssessmentRootId);
        Assert.Equal([other.TextId], reader.Context.TextIds);
        Assert.Equal(0, reader.Diagnostics.TextRowsDeserialized);
        Assert.Equal(0, reader.Diagnostics.WordformRowsDeserialized);
        var summary = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(summary.Succeeded, summary.Refusal?.Message);
        using var summaryRead = summary.Value!;
        Assert.Equal(2, summaryRead.Value.PhysicalOccurrenceCount);
        Assert.All(summaryRead.Value.Words, word => Assert.NotNull(word.Assessment));

        if (change == "default-declaration")
            new NamedSelectionRepository(database).SetDefault("Changed", [other.TextId], [],
                new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
        if (change is "exploratory" or "replacement")
            new AssessmentRepository(database).Record(NewAssessment("displayed-later", capture.Value.Token,
                root.Selection, root.Words!, change == "replacement" ? root.AssessmentId : null,
                DateTimeOffset.UtcNow));

        var later = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.Equal(current, later.Succeeded);
        later.Value?.Dispose();
        var pending = PendingChanges.Load(new PendingChangesRequest(projectPath, "1.0"));
        Assert.True(pending.Succeeded, pending.Refusal?.Message);
        var put = PendingChanges.Put(new PutPendingChangeRequest(projectPath, "1.0", pending.Value!.Revision,
            new ChangeIntent("displayed-opinion", AnalysisChangeKinds.Reject,
                CanonicalId.FromGuid(other.AnalysedWordformId).Value, SeededProject.AnalysedWordForm,
                StoredAnalysisId: CanonicalId.FromGuid(other.ApprovedAnalysisId).Value))
        {
            ExpectedContext = reader.Context.ExpectedWriteContext(),
        });
        Assert.Equal(current, put.Succeeded);
        if (!current)
        {
            Assert.Equal("texts.evidence-changed", put.Refusal!.Code);
            Assert.Equal(pending.Value.Revision,
                PendingChanges.Load(new PendingChangesRequest(projectPath, "1.0")).Value!.Revision);
        }
        var reopened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(key, [other.TextId], [])
            { ProjectPath = projectPath, DisplayedEvidence = shownAssessment ? null : named,
                ShownAssessment = shownAssessment ? shown : null });
        Assert.Equal(current, reopened.Succeeded);
        reopened.Value?.Dispose();
    }

    [Theory]
    [InlineData("missing-root")]
    [InlineData("wrong-selection")]
    [InlineData("wrong-baseline")]
    [InlineData("wrong-origin")]
    public async Task DisplayedEvidenceRefusesMismatchedNamedDescriptors(string fault)
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);
        var key = ProjectWorkspaceKey.Compute(project);
        new NamedSelectionRepository(database).SetDefault("Default", [text.TextId], [],
            new SelectionParsingLimits(StepCap.Default, SelectionTimeLimitMode.Estimated, null), null);
        RecordSelectionAssessment(database, project, capture.Value!.Token, "displayed-valid", null,
            DateTimeOffset.UtcNow);
        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(key, [text.TextId], []));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        var context = reader.Context.ExpectedWriteContext();
        var evidence = context.SelectionEvidence!;
        context = fault switch
        {
            "missing-root" => context with { SelectionEvidence = evidence with { RootAssessmentId = "absent" } },
            "wrong-selection" => context with
                { SelectionEvidence = evidence with { AssessmentSelectionSha256 = "sha256:wrong" } },
            "wrong-origin" => context with { SelectionEvidence = evidence with { Origin = "unknown" } },
            _ => new ExpectedContext(new BaselineToken(context.Baseline.ProjectIdentity,
                context.Baseline.SemanticSnapshotDigest, context.Baseline.ProjectionVersion,
                context.Baseline.CapturedUtc, "sha256:" + new string('0', 64),
                context.Baseline.CapturedHostSessionId, context.Baseline.CapturedEditGeneration))
                { SelectionEvidence = evidence },
        };
        var refused = await SelectionReader.OpenAsync(database, new SelectionReadRequest(key, [text.TextId], [])
            { DisplayedEvidence = context });
        Assert.False(refused.Succeeded);
        Assert.Equal("texts.evidence-changed", refused.Refusal!.Code);
    }

    [Fact]
    public async Task PlainSentenceContextUsesExactUncheckedTextAnchorsAndHydratesNoWordformDetail()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var chosen = SeededProject.SeedText(cache, pristine.Seed);
        var uncheckedText = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);
        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(
            ProjectWorkspaceKey.Compute(project), [chosen.TextId], []));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        var anchor = new OccurrenceAnchor(uncheckedText.TextId, uncheckedText.FirstParagraphId,
            uncheckedText.FirstSegmentId, 0);
        var before = reader.Diagnostics;
        var read = await reader.ReadSentenceAsync(anchor);
        Assert.True(read.Succeeded, read.Refusal?.Message);
        using (var context = read.Value!)
        {
            Assert.Equal(2, context.Value.SourceTokenCount);
            Assert.Equal(0, context.Value.TargetTokenOffset);
            Assert.Equal(anchor, context.Value.Tokens[0].Occurrence);
            Assert.Null(context.Value.Tokens[1].Occurrence);
            Assert.Equal(SeededProject.PunctuationForm, context.Value.Tokens[1].Text);
            Assert.Equal(SelectionReadPurpose.Pin, context.Lease.Purpose);
            Assert.Equal(1, reader.Diagnostics.LeasedLineRanges);
            Assert.Equal(2, reader.Diagnostics.LeasedLineTokens);
        }
        Assert.Equal(1, reader.Diagnostics.TextRowsDeserialized - before.TextRowsDeserialized);
        Assert.Equal(0, reader.Diagnostics.WordformRowsDeserialized - before.WordformRowsDeserialized);
        Assert.Equal(0, reader.Diagnostics.WordformAnalyses - before.WordformAnalyses);
        Assert.Equal(0, reader.Diagnostics.LiveTokenModels);
        Assert.Equal(0, reader.Diagnostics.LeasedResults);
        var missing = await reader.ReadSentenceAsync(anchor with { Index = 999 });
        Assert.False(missing.Succeeded);
        Assert.Equal("texts.occurrence-not-found", missing.Refusal!.Code);
        Assert.False(reader.Diagnostics.IsObsolete);
        var unchanged = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(unchanged.Succeeded, unchanged.Refusal?.Message);
        using var summary = unchanged.Value!;
        Assert.Equal(chosen.TextId, Assert.Single(summary.Value.Texts).TextId);
        Assert.Equal(2, summary.Value.PhysicalOccurrenceCount);
    }

    [Fact]
    public void SummaryCountsDeduplicateEqualWritingSystemFormsAndRetainChosenAnalysisCountsAcrossFilters()
    {
        var textId = Guid.NewGuid();
        var wordformId = Guid.NewGuid();
        var first = Analysis("approved") with { Key = "first" };
        var second = Analysis("approved") with { Key = "second" };
        var wordform = Wordform(wordformId, [first, second], 0);
        var tokens = new[] { first, second }.Select((analysis, index) =>
            Token("repeat", wordformId, "approved", index,
                new WritingSystemText("repeat", "en"), new WritingSystemText("repeat", "fr")) with
                { AnalysisKey = analysis.Key, AnalysisId = analysis.AnalysisId }).ToArray();
        var text = Index(textId, "Repeated", [new TextWordsProjectedLine(1, "repeat repeat", tokens,
            Guid.NewGuid(), Guid.NewGuid(), true)], wordform);
        var index = new SelectionIndexBuilder();
        index.AddText(text);
        index.AddWordform(wordform);

        var summary = index.Build();
        Assert.Equal(2, summary.PhysicalOccurrenceCount);
        Assert.Equal(4, summary.MembershipCount);
        Assert.Equal(2, summary.OccurrencesByForm["repeat"]);
        Assert.Equal(2, summary.WordRowsByProjectStanding[ProjectStanding.Approved]);
        Assert.Equal(2, summary.SeveralAnalysisRowCount);
        Assert.All(summary.Words, word =>
        {
            Assert.Equal(2, word.ApprovedAnalysisCount);
            Assert.Equal(2, word.ChosenAnalysisCount);
        });
        var filtered = index.Build(new SelectionViewRequest(Search: "absent"));
        Assert.Empty(filtered.Words);
        Assert.Equal(summary.OccurrencesByForm, filtered.OccurrencesByForm);
        Assert.Equal(summary.WordRowsByProjectStanding, filtered.WordRowsByProjectStanding);
        Assert.Equal(summary.SeveralAnalysisRowCount, filtered.SeveralAnalysisRowCount);
    }

    [Fact]
    public async Task VisibleWordDetailBatchSharesWritingSystemRowsWithoutReadingSentencesAndRejectsUnknownTargets()
    {
        Directory.CreateDirectory(_root);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var managedRoot = Path.Combine(_root, "managed");
        Directory.CreateDirectory(managedRoot);
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        using var database = new ProjectDatabaseCatalog(MotifSchema.CurrentSchema, new Version(1, 0)).OpenOwned(project);
        var opened = await SelectionReader.OpenAsync(database, new SelectionReadRequest(
            ProjectWorkspaceKey.Compute(project), [text.TextId], []));
        Assert.True(opened.Succeeded, opened.Refusal?.Message);
        using var reader = opened.Value!;
        var summary = await reader.ReadSummaryAsync(new SelectionViewRequest());
        Assert.True(summary.Succeeded, summary.Refusal?.Message);
        using var summaryRead = summary.Value!;
        var keys = summaryRead.Value.Words.Select(word => word.Key).ToArray();
        var before = reader.Diagnostics;
        var read = await reader.ReadWordDetailsAsync(keys);
        Assert.True(read.Succeeded, read.Refusal?.Message);
        using (var details = read.Value!)
        {
            Assert.Equal(2, details.Value.Wordforms.Count);
            Assert.Contains(details.Value.Wordforms.Values, word => word.WordformId == text.AnalysedWordformId);
            Assert.Equal(0, reader.Diagnostics.TextRowsDeserialized - before.TextRowsDeserialized);
            Assert.Equal(2, reader.Diagnostics.WordformRowsDeserialized - before.WordformRowsDeserialized);
            Assert.Equal(2, reader.Diagnostics.LeasedWordKeys);
            var warm = await reader.ReadWordDetailsAsync(keys);
            Assert.True(warm.Succeeded, warm.Refusal?.Message);
            using var warmRead = warm.Value!;
            Assert.Same(details.Value.Wordforms[keys[0]], warmRead.Value.Wordforms[keys[0]]);
            Assert.Equal(2, reader.Diagnostics.WordformRowsDeserialized - before.WordformRowsDeserialized);
        }
        Assert.Equal(0, reader.Diagnostics.LeasedWordKeys);
        var unknown = await reader.ReadWordDetailsAsync([keys[0] with { WordformId = Guid.NewGuid() }]);
        Assert.False(unknown.Succeeded);
        Assert.Equal("selection-reader.range-invalid", unknown.Refusal!.Code);
        var oversized = await reader.ReadWordDetailsAsync(Enumerable.Repeat(keys[0], 129).ToArray());
        Assert.False(oversized.Succeeded);
        Assert.Equal("selection-reader.range-invalid", oversized.Refusal!.Code);
    }

    private static AssessmentRecord RecordSelectionAssessment(MotifDatabase database, ProjectLocator project,
        BaselineToken baseline, string assessmentId, string? replacesAssessmentId, DateTimeOffset savedUtc)
    {
        var current = CurrentEvidenceQuery.ReadCurrentEvidence(database, project,
            includeResolvedReadings: false, includeWordContext: false);
        Assert.True(current.Succeeded, current.Refusal?.Message);
        var selection = current.Value!.Selection!.Selection;
        var words = selection.Words.Select(word => new AssessedWord(word, "analysed", [])).ToArray();
        new AssessmentRepository(database).Record(NewAssessment(assessmentId, baseline, selection, words,
            replacesAssessmentId, savedUtc));
        return new AssessmentRepository(database).Get(assessmentId);
    }

    private static NewAssessmentRecord NewAssessment(string assessmentId, BaselineToken baseline,
        SIL.Motif.Host.Corpus.Selection selection, IReadOnlyList<AssessedWord> words,
        string? replacesAssessmentId, DateTimeOffset savedUtc) =>
        new(assessmentId, null, null, "pangloss", AssessmentKind.ParseTime.ToStoredKind(), "{}",
            "sha256:scope", "whitespace", "1",
            System.Text.Json.JsonSerializer.Serialize(baseline, MotifJson.CreateOptions()), selection,
            "sha256:outcome", "sha256:semantic", "sha256:grammar", "fingerprint", "pipeline", 0, words,
            SavedUtc: savedUtc.ToString("O"))
        {
            ReplacesAssessmentId = replacesAssessmentId,
        };

    private static TextWordsProjectedToken Token(string text, Guid? wordformId, string? status,
        int index, params WritingSystemText[] forms) =>
        new(text, forms, wordformId, status, null, null, null, null, index, null);

    private static TextWordsProjectedAnalysis Analysis(string opinion) =>
        new(opinion, [])
        {
            AnalysisId = Guid.NewGuid(),
            Opinion = opinion,
            Identity = new ApprovedMorphology([]),
        };

    private static BaselineTextReadIndex Index(Guid textId, string title,
        IReadOnlyList<TextWordsProjectedLine> lines, params BaselineTextReadWordform[] wordforms) =>
        new(textId, title, null, lines.Select(line => new BaselineTextReadLine(line.Number,
            line.ParagraphId, line.SegmentId, line.ParseIsCurrent, line.Tokens.Select(token =>
                new BaselineTextReadToken(token.OccurrenceIndex, token.WordformId, token.Status,
                    token.AnalysisKey, token.AnalysisId, token.Forms)).ToArray())).ToArray(), wordforms);

    private static BaselineTextReadWordform Wordform(Guid id,
        IReadOnlyList<TextWordsProjectedAnalysis> approved, int candidateCount) =>
        new(id, approved.Count, candidateCount, 0, false, approved.Select(analysis =>
            new BaselineTextReadAnalysis(analysis.Key, analysis.AnalysisId, analysis.Opinion,
                analysis.Identity!)).ToArray());
}
