using System.Text.Json;
using SIL.Motif.App.ViewModels;
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
    public void AppMatrixAndOverviewClassifyTheSameStoredAssessmentWords()
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
        using (var database = MotifDatabase.OpenOwned(databasePath, project, MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project))!;
            using var baselineCache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
            var selectionRequest = new SelectionRequest(false, [text.TextId], [], false, null);
            var composed = SelectionComposer.Compose(baselineCache, selectionRequest,
                new AssessmentRepository(database), tokenJson);
            Assert.True(composed.Succeeded, composed.Refusal?.Message);
            var selection = composed.Value!.Selection;
            var words = selection.Words.Select((word, index) => new AssessedWord(
                word, index == 0 ? "capped" : "no-analysis", [], 20)
            {
                ProjectStanding = ProjectStanding.NotPresent,
                IsIncomplete = false,
            }).ToArray();
            new AssessmentRepository(database).Record(new NewAssessmentRecord(
                "assessment/parity", null, null, "pangloss", AssessmentKind.ParseTime.ToStoredKind(),
                "{}", "sha256:scope", "whitespace", "1", tokenJson, selection,
                "sha256:outcome", "sha256:semantic", "sha256:grammar", "model", "pipeline", 0,
                words, SavedUtc: "2026-09-24T12:00:00.0000000+00:00"));
            stored = new AssessmentRepository(database).Get("assessment/parity");
        }

        var overview = OverviewCommand.Overview(new OverviewRequest(projectPath));
        Assert.True(overview.Succeeded, overview.Refusal?.Message);
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

        Assert.Equal(overview.Value!.AssessmentId, stored.AssessmentId);
        Assert.Equal(overview.Value.TextCoverage.ParsedWords, Column(CompareColumnKind.Match) + Column(CompareColumnKind.NoMatch));
        Assert.Equal(overview.Value.TextCoverage.NoParseWords, Column(CompareColumnKind.NoParse));
        Assert.Equal(overview.Value.TextCoverage.UnknownWords, Column(CompareColumnKind.Timeout));
        Assert.Equal(overview.Value.TextCoverage.SkippedWords, Column(CompareColumnKind.Skipped));
        Assert.Equal(overview.Value.Accuracy.ApprovedWordsKept,
            Cell(WordProjectStatus.Approved, CompareColumnKind.Match));
        Assert.Equal(overview.Value.Accuracy.ApprovedWordCount, Row(WordProjectStatus.Approved));
        Assert.Equal(overview.Value.Accuracy.Violations, Family(CompareFamilyKind.Violation));
        Assert.Equal(overview.Value.Accuracy.UnknownWords, Family(CompareFamilyKind.Unknown));
        Assert.Equal(overview.Value.Accuracy.RejectedAnalysesRebuilt,
            Cell(WordProjectStatus.Rejected, CompareColumnKind.Match));
        Assert.Equal(overview.Value.Accuracy.RejectedWordCount, Row(WordProjectStatus.Rejected));
        Assert.Equal(overview.Value.Accuracy.CandidatesConfirmed,
            Cell(WordProjectStatus.Candidate, CompareColumnKind.Match));
        Assert.Equal(overview.Value.Accuracy.CandidateWordCount, Row(WordProjectStatus.Candidate));
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRoot, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }
}
