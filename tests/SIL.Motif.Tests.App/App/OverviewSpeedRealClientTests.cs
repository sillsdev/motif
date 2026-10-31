using System.Text.Json;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that the speed the Overview and Timing lead with is PanGloss's own stored per-word time, carried
/// unchanged through the window's real command client.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
[Trait("MotifTestLevel", "Integration")]
public sealed class OverviewSpeedRealClientTests(PristineProjectFixture pristine) : IDisposable
{
    private readonly string _managedRoot =
        Path.Combine(Path.GetTempPath(), "Motif.OverviewSpeed", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task TheSpeedHeadlineIsTheStoredWordCountAndTheirSummedWorkSteps()
    {
        Directory.CreateDirectory(_managedRoot);
        using var cache = pristine.NewScratch();
        var text = SeededProject.SeedText(cache, pristine.Seed);
        new FwDataProjectLoader().Save(cache);
        var projectPath = cache.ProjectId.Path;
        var capture = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(projectPath), _managedRoot);
        Assert.True(capture.Succeeded, capture.Refusal?.Message);
        var set = SelectionCommands.SetDefault(new SetDefaultSelectionRequest(projectPath, "Speed", [text.TextId], []));
        Assert.True(set.Succeeded, set.Refusal?.Message);

        var project = new ProjectLocator(Path.GetFullPath(projectPath), Path.GetFileNameWithoutExtension(projectPath));
        var tokenJson = JsonSerializer.Serialize(capture.Value!.Token, SIL.Motif.Contract.MotifJson.CreateOptions());
        IReadOnlyList<AssessedWord> words;
        using (var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                   MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project))!;
            using var baselineCache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
            var composed = SelectionComposer.Compose(baselineCache,
                new SelectionRequest(false, [text.TextId], [], false, null), new AssessmentRepository(database),
                tokenJson);
            Assert.True(composed.Succeeded, composed.Refusal?.Message);
            var selection = composed.Value!.Selection;
            Assert.True(selection.Words.Count >= 2, "the seeded Text needs at least two words");
            // The last word carries no time and no work count, so the headline must count only measured words.
            words = selection.Words.Select((word, index) => new AssessedWord(word, "no-analysis", [],
                index == selection.Words.Count - 1 ? null : (index + 1) * 100)
            {
                WorkSteps = index == selection.Words.Count - 1 ? null : (index + 1) * 1_000L,
                ProjectStanding = ProjectStanding.NotPresent,
                IsIncomplete = false,
            }).ToArray();
            new AssessmentRepository(database).Record(new NewAssessmentRecord(
                AssessmentId: "assessment/speed",
                ProposalId: null,
                ProposalIntentDigest: null,
                Assessor: "pangloss",
                Kind: AssessmentKind.ParseTime.ToStoredKind(),
                ScopeJson: "{}",
                ScopeDigest: "sha256:scope",
                Subject: new AssessmentSubject.Words(selection, "whitespace", "1"),
                BaselineToken: tokenJson,
                OutcomeDigest: "sha256:outcome",
                SemanticDigest: "sha256:semantic",
                GrammarSourceSha256: "sha256:grammar",
                ModelFingerprint: "model",
                Pipeline: "pipeline",
                DiagnosticCount: 0,
                Material: new AssessmentRecordMaterial.Words(words, null, null, []),
                SavedUtc: "2026-09-30T08:51:00.0000000+00:00"
            ));
        }

        var measured = words.Where(word => word.ElapsedMs is not null).ToArray();
        var totalMs = measured.Sum(word => word.ElapsedMs!.Value);
        var totalWork = measured.Sum(word => word.WorkSteps!.Value);
        var client = RealCommandClient.Create(_managedRoot);
        var read = await client.OverviewAsync(new OverviewRequest(projectPath), CancellationToken.None);

        Assert.True(read.Succeeded, read.Refusal?.Message);
        Assert.Equal(measured.Length, read.Value!.Timing.MeasuredWordCount);
        Assert.Equal(totalMs / 1000d, read.Value.AssessmentElapsedSeconds);
        Assert.Equal(measured.Length, read.Value.Timing.Work.MeasuredWordCount);
        Assert.Equal(totalWork, read.Value.Timing.Work.TotalWorkSteps);

        var context = WorkspaceContextTests.NewContext(client);
        var overview = new OverviewPageModel(context);
        var timing = new TimingPageModel(context);
        await context.OpenProjectAsync(projectPath);

        var expectedTotal = TimingShare.FormatWorkSteps(totalWork);
        // The total is the words' work steps added up; how long the run took on this computer is a separate line.
        Assert.Equal($"{measured.Length:N0} {(measured.Length == 1 ? "word" : "words")} · {expectedTotal} in total",
            overview.SpeedMain);
        Assert.Equal($"Took {TimingShare.FormatDuration(totalMs)} on this computer", overview.SpeedElapsed);
        Assert.True(timing.HasHeadline, timing.TimingRefusal?.Sentence ?? "Timing read no stored words");
        Assert.Equal(expectedTotal, timing.HeadlineTotal);
        // The caption counts the measured words against every chosen word, so the untimed one is not hidden.
        Assert.Equal($"for {measured.Length:N0} of the {words.Count:N0} words", timing.HeadlineTotalCaption);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRoot, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }
}
