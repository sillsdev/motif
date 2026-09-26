using System.Text.Json;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Services;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;
using SIL.Motif.Commands.Queries;

namespace SIL.Motif.Tests.App;

[Collection(LcmCacheTestCollection.Name)]
public sealed class CompareOverviewParityTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _managedRoot = Path.Combine(Path.GetTempPath(), "Motif.CompareOverviewParity", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task AppMatrixAndOverviewClassifyTheSameStoredAssessmentWords()
    {
        Directory.CreateDirectory(_managedRoot);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), _managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var set = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(
            projectPath, "Parity", [text.TextId], []));
        Assert.True(set.Succeeded, set.Refusal?.Message);

        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        var databasePath = ProjectDatabaseCatalog.DatabasePathFor(project);
        var tokenJson = JsonSerializer.Serialize(capture.Value!.Token, SIL.Motif.Contract.MotifJson.CreateOptions());
        AssessmentRecord stored;
        string baselineCachePath;
        using (var database = MotifDatabase.OpenOwned(databasePath, project, MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project))!;
            baselineCachePath = baseline.FwDataPath;
            using var baselineCache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
            var selectionRequest = new SelectionRequest(false, [text.TextId], [], false, null);
            var composed = SelectionComposer.Compose(baselineCache, selectionRequest,
                new AssessmentRepository(database), tokenJson);
            Assert.True(composed.Succeeded, composed.Refusal?.Message);
            var selection = composed.Value!.Selection;
            var words = selection.Words.Select((word, index) => new AssessedWord(
                word, index == 0 ? "analysed" : "no-analysis", [], 20)
            {
                ProjectStanding = index == 0 ? ProjectStanding.Approved : ProjectStanding.NotPresent,
                IsIncomplete = false,
                ReadingGrades = index == 0 ? ["approved", "disapproved"] : null,
                Morphology = index == 0
                    ? new ParseWordEvidence("v1", 0, word, 20, false, false, false,
                    [
                        new ParseAnalysis([new ParseMorph("first", "msa-1", null, null)]),
                        new ParseAnalysis([new ParseMorph("second", "msa-2", null, null)]),
                    ], [])
                    : null,
            }).ToArray();
            new AssessmentRepository(database).Record(new NewAssessmentRecord(
                "assessment/parity", null, null, "pangloss", AssessmentKind.ParseTime.ToStoredKind(),
                "{}", "sha256:scope", "whitespace", "1", tokenJson, selection,
                "sha256:outcome", "sha256:semantic", "sha256:grammar", "model", "pipeline", 0,
                words, SavedUtc: "2026-09-24T12:00:00.0000000+00:00"));
            stored = new AssessmentRepository(database).Get("assessment/parity");
        }

        var hiddenBaselinePath = baselineCachePath + ".unavailable";
        File.Move(baselineCachePath, hiddenBaselinePath);
        try
        {
            var overview = OverviewCommand.Overview(new OverviewRequest(projectPath));
            Assert.True(overview.Succeeded, overview.Refusal?.Message);
            var timing = TimingCommand.Timing(new TimingRequest(projectPath, WordSet: "all"));
            Assert.True(timing.Succeeded, timing.Refusal?.Message);
            var client = new CommandClient(_managedRoot);
            var appOverview = await client.OverviewAsync(new OverviewRequest(projectPath), CancellationToken.None);
            var appTiming = await client.TimingAsync(new TimingRequest(projectPath, WordSet: "all"), CancellationToken.None);
            Assert.True(appOverview.Succeeded, appOverview.Refusal?.Message);
            Assert.True(appTiming.Succeeded, appTiming.Refusal?.Message);
            var overviewContext = WorkspaceContextTests.NewContext(client);
            var overviewPage = new OverviewPageModel(overviewContext);
            await overviewContext.OpenProjectAsync(projectPath);
            Assert.NotNull(overviewPage.Overview);
            Assert.Equal(appOverview.Value!.MotifStoreCreatedUtc, overviewPage.Overview.MotifStoreCreatedUtc);
            Assert.Equal(appOverview.Value.SelectionWordCount, overviewPage.Overview.SelectionWordCount);
            Assert.Equal(appOverview.Value.TextCoverage, overviewPage.Overview.TextCoverage);
            Assert.Equal(appOverview.Value.Accuracy, overviewPage.Overview.Accuracy);
            Assert.Contains($"{appOverview.Value.SelectionWordCount:N0}", overviewPage.SelectionWordCountText);
            Assert.Contains($"{appOverview.Value.TextCoverage.ParsedOccurrences:N0} of " +
                $"{appOverview.Value.TextCoverage.TotalOccurrences:N0}", overviewPage.TextCoverageOccurrences);
            Assert.Contains($"{appOverview.Value.Accuracy.Violations:N0} violations", overviewPage.AccuracyBreakdown);
            Assert.Contains($"{appOverview.Value.Accuracy.RejectedAnalysesRebuilt:N0} rejected analyses rebuilt",
                overviewPage.AccuracyBreakdown);
            Assert.Equal(overview.Value!.TextCoverage, appOverview.Value!.TextCoverage);
            Assert.Equal(overview.Value.Accuracy, appOverview.Value.Accuracy);
            Assert.Equal(overview.Value.WordformCount, appOverview.Value.WordformCount);
            Assert.Equal(overview.Value.RuleCount, appOverview.Value.RuleCount);
            Assert.Equal(timing.Value!.WordCount, appTiming.Value!.WordCount);
            Assert.Equal(timing.Value.Aggregates, appTiming.Value.Aggregates);
            Assert.Equal(timing.Value.CostliestWords, appTiming.Value.CostliestWords);
            var currentEvidence = CurrentEvidenceQuery.ReadCurrentEvidence(projectPath);
            Assert.True(currentEvidence.Succeeded, currentEvidence.Refusal?.Message);
            var appCurrentEvidence = await client.ReadCurrentEvidenceAsync(projectPath, CancellationToken.None);
            Assert.True(appCurrentEvidence.Succeeded, appCurrentEvidence.Refusal?.Message);
            var readModel = currentEvidence.Value!;
            Assert.Equal(readModel.Freshness, appCurrentEvidence.Value!.Freshness);
            Assert.Equal(readModel.MatchingAssessment?.AssessmentId,
                appCurrentEvidence.Value.MatchingAssessment?.AssessmentId);
            Assert.Equal(readModel.Selection?.Selection.Sha256,
                appCurrentEvidence.Value.Selection?.Selection.Sha256);
            Assert.Equal(EvidenceFreshness.Current, readModel.Freshness);
            Assert.Equal(capture.Value.Token, readModel.Baseline?.Token);
            Assert.Equal(stored.AssessmentId, readModel.MatchingAssessment?.AssessmentId);
            Assert.Equal(overview.Value.SelectionFingerprint, readModel.Selection?.Selection.Sha256);
            Assert.Equal(overview.Value.SelectionWordCount, readModel.Selection?.Selection.Words.Count);
            Assert.Equal(overview.Value.TextOccurrenceCount, readModel.Selection?.TotalOccurrences);
            Assert.Equal(overview.Value.WordformCount, readModel.ProjectSummary?.WordformCount);
            Assert.Equal(overview.Value.RuleCount, readModel.ProjectSummary?.RuleCount);
            Assert.NotEmpty(readModel.ProjectSummary!.Wordforms);

            var appWords = stored.Words!.Select(word => new AssessmentWordResult(
                word.Word, word.Outcome, word.IsIncomplete, "Search completed", word.ElapsedMs, word.RawSignature)
            {
                ProjectStanding = word.ProjectStanding,
                ReadingGrades = word.ReadingGrades,
                Morphology = word.Morphology,
            }).ToArray();
            var table = new AssessWordsViewModel();
            table.Load(appWords);
            var compare = new CompareViewModel();
            compare.Load(table.AllRows);
            int Column(CompareColumnKind column) => compare.Columns.Single(item => item.Column == column).Count;
            int Row(WordProjectStatus row) => compare.Rows.Single(item => item.Row == row).Cells.Sum(cell => cell.Count);
            int Cell(WordProjectStatus row, CompareColumnKind column) =>
                compare.Cells.Single(item => item.Row == row && item.Column == column).Count;
            int Family(CompareFamilyKind family) => compare.Cells.Where(cell => cell.Family == family).Sum(cell => cell.Count);

            Assert.Equal(overview.Value.AssessmentId, stored.AssessmentId);
            Assert.Equal(overview.Value.TextCoverage.ParsedWords, Column(CompareColumnKind.Match) + Column(CompareColumnKind.NoMatch));
            Assert.Equal(overview.Value.TextCoverage.NoParseWords, Column(CompareColumnKind.NoParse));
            Assert.Equal(overview.Value.TextCoverage.UnknownWords, Column(CompareColumnKind.Timeout));
            Assert.Equal(overview.Value.TextCoverage.SkippedWords, Column(CompareColumnKind.Skipped));
            Assert.Equal(overview.Value.Accuracy.ApprovedWordsKept,
                Cell(WordProjectStatus.Approved, CompareColumnKind.Match));
            Assert.Equal(overview.Value.Accuracy.ApprovedWordCount, Row(WordProjectStatus.Approved));
            Assert.Equal(overview.Value.Accuracy.Violations, Family(CompareFamilyKind.Violation));
            Assert.Equal(overview.Value.Accuracy.UnknownWords, Family(CompareFamilyKind.Unknown));
            Assert.Equal(1, overview.Value.Accuracy.RejectedAnalysesRebuilt);
            Assert.Equal(0, overview.Value.Accuracy.RejectedWordsInMatchCell);
            Assert.Equal(overview.Value.Accuracy.RejectedWordCount, Row(WordProjectStatus.Rejected));
            Assert.Equal(overview.Value.Accuracy.CandidatesConfirmed,
                Cell(WordProjectStatus.Candidate, CompareColumnKind.Match));
            Assert.Equal(overview.Value.Accuracy.CandidateWordCount, Row(WordProjectStatus.Candidate));
        }
        finally
        {
            File.Move(hiddenBaselinePath, baselineCachePath);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRoot, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }
}
