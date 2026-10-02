using SIL.Motif.Contract.Responses;
using System;
using System.Collections.Generic;
using System.IO;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Host.Texts;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;
using System.Text.Json;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class CurrentEvidenceQueryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.CurrentEvidenceQueryTests",
        Guid.NewGuid().ToString("N"));

    public CurrentEvidenceQueryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ReadCurrentEvidenceReturnsSavedSummarySelectionAndStaleness()
    {
        var fwDataPath = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(fwDataPath, "synthetic project marker");
        var savedUtc = DateTimeOffset.Parse("2026-08-23T11:30:00Z");
        File.SetLastWriteTimeUtc(fwDataPath, savedUtc.UtcDateTime);
        var project = new ProjectLocator(fwDataPath, "project");
        var textId = Guid.NewGuid();
        var summary = new ProjectSummarySnapshot(2, 3, 4, 5, 6, ["motifa", "motifb"],
            [new ProjectTextSummary(textId, "Genesis", 2, 3,
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["motifa"] = 2,
                    ["motifb"] = 1,
                })]);
        var sourceLastWriteUtc = DateTimeOffset.Parse("2026-08-23T11:00:00Z");

        using var database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var workspaceKey = ProjectWorkspaceKey.Compute(project);
        var root = Path.Combine(_root, "baseline");
        var publication = new BaselinePublication(root, Path.Combine(root, "project.fwdata"),
            new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
                "2026-08-23T10:00:00Z", "sha256:" + new string('a', 64)));
        new BaselineRepository(database).Record(workspaceKey, publication,
            DateTimeOffset.Parse("2026-08-23T12:00:00Z"), sourceLastWriteUtc, TestTextWords.Empty, summary);
        new NamedSelectionRepository(database).SetDefault("Default", [textId], ["pasted"]);

        var result = CurrentEvidenceQuery.ReadCurrentEvidence(database, project);

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(EvidenceFreshness.Stale, result.Value!.Freshness);
        Assert.Equal(savedUtc, result.Value.LastFieldWorksSaveUtc);
        Assert.Equal(2, result.Value.ProjectSummary!.WordCount);
        Assert.Equal("Default", result.Value.DefaultSelection!.Name);
        Assert.Equal(3, result.Value.Selection!.Selection.Words.Count);
        Assert.Equal(2, result.Value.Selection.OccurrencesByWord["motifa"]);
        Assert.Equal(0, result.Value.Selection.OccurrencesByWord["pasted"]);
    }

    [Fact]
    public void RefusesAssessmentWhenStoredTextAnalysesCannotBeRead()
    {
        var fwDataPath = Path.Combine(_root, "damaged.fwdata");
        File.WriteAllText(fwDataPath, "synthetic project marker");
        var project = new ProjectLocator(fwDataPath, "damaged");
        var textId = Guid.NewGuid();
        var token = new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
            "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64));
        using (var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
                   MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            var projection = new TextWordsProjection([new TextWordsProjectedText(textId, "Text", [], [])], []);
            var summary = new ProjectSummarySnapshot(1, 1, 1, 0, 0, ["cat"],
                [new ProjectTextSummary(textId, "Text", 1, 1,
                    new Dictionary<string, int>(StringComparer.Ordinal) { ["cat"] = 1 })]);
            var baselineRoot = Path.Combine(_root, "damaged-baseline");
            new BaselineRepository(database).Record(ProjectWorkspaceKey.Compute(project),
                new BaselinePublication(baselineRoot, Path.Combine(baselineRoot, "damaged.fwdata"), token),
                DateTimeOffset.Parse("2026-09-24T10:30:00Z"), DateTimeOffset.Parse("2026-09-24T10:00:00Z"),
                projection, summary);
            new NamedSelectionRepository(database).SetDefault("Default", [textId], []);
            var tokenJson = JsonSerializer.Serialize(token, MotifJson.CreateOptions());
            new AssessmentRepository(database).Record(Record("assessment", ["cat"],
                [new AssessedWord("cat", "no-analysis", []) { ProjectStanding = "approved" }],
                "2026-09-24T11:00:00Z", tokenJson));
            using var connection = database.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE BaselineTextWords SET TextJson = '{broken' WHERE TextId = $textId;";
            command.Parameters.AddWithValue("$textId", textId.ToString("D"));
            Assert.Equal(1, command.ExecuteNonQuery());
        }

        var result = CurrentEvidenceQuery.ReadCurrentEvidence(fwDataPath);

        Assert.False(result.Succeeded);
        Assert.NotNull(result.Refusal);
    }
    [Fact]
    public void LaterSubsetAssessmentReplacesOnlyItsWords()
    {
        var baseline = new AssessmentRecord("base", null, null, "pangloss",
            AssessmentKind.ParseTime.ToStoredKind(), "{}", "scope", "whitespace", "1", "token",
            Selection.Create("Default", ["cat", "dog"]), null, null, "grammar", null, null, null,
            "2026-09-24T12:00:00Z", Words: [new AssessedWord("cat", "timed-out", []),
                new AssessedWord("dog", "analysed", [])]);
        var rerun = baseline with
        {
            AssessmentId = "rerun",
            Selection = Selection.Create("subset", ["cat"]),
            SavedUtc = "2026-09-24T12:05:00Z",
            Words = [new AssessedWord("cat", "analysed", [])],
        };

        var effective = AssessmentWordOverlay.Apply(baseline.Words!, [rerun]);

        Assert.Equal("analysed", effective.Single(word => word.Word == "cat").Outcome);
        Assert.Equal("analysed", effective.Single(word => word.Word == "dog").Outcome);
        Assert.Equal(2, effective.Count);
    }

    [Fact]
    public void LaterSubsetAssessmentReplacesOnlyItsWordsObjectTimes()
    {
        static AssessmentObjectTiming Row(string word, long ns) =>
            new("morph_rule", "rule-r", "authored", "analysis", "R", word, 1, null, ns);
        var baseline = new AssessmentRecord("base", null, null, "pangloss",
            AssessmentKind.ParseTime.ToStoredKind(), "{}", "scope", "whitespace", "1", "token",
            Selection.Create("Default", ["cat", "dog"]), null, null, "grammar", null, null, null,
            "2026-09-24T12:00:00Z", Words: [new AssessedWord("cat", "timed-out", []),
                new AssessedWord("dog", "analysed", [])])
        { ObjectTimings = [Row("cat", 9), Row("dog", 5)] };
        var rerun = baseline with
        {
            AssessmentId = "rerun",
            Selection = Selection.Create("subset", ["cat", "bird"]),
            Words = [new AssessedWord("cat", "analysed", []), new AssessedWord("bird", "analysed", [])],
            ObjectTimings = [Row("cat", 2), Row("bird", 7)],
        };

        var effective = AssessmentWordOverlay.ApplyObjectTimings(baseline, [rerun]);

        Assert.Equal([("dog", 5L), ("cat", 2L)], effective.Select(row => (row.Word, row.ElapsedNs!.Value)));
    }

    [Fact]
    public void ExploratorySubsetDoesNotReplaceTheDefaultSelectionsAnswers()
    {
        var fwDataPath = Path.Combine(_root, "project.fwdata");
        File.WriteAllText(fwDataPath, "synthetic project marker");
        var project = new ProjectLocator(fwDataPath, "project");
        using var database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var token = new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
            "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64));
        var root = Path.Combine(_root, "baseline");
        new BaselineRepository(database).Record(ProjectWorkspaceKey.Compute(project),
            new BaselinePublication(root, Path.Combine(root, "project.fwdata"), token),
            DateTimeOffset.Parse("2026-09-24T10:30:00Z"), DateTimeOffset.Parse("2026-09-24T10:00:00Z"), TestTextWords.Empty,
            new ProjectSummarySnapshot(0, 0, 0, 0, 0, [], []));
        new NamedSelectionRepository(database).SetDefault("Default", [], ["cat", "dog"]);
        var tokenJson = JsonSerializer.Serialize(token, MotifJson.CreateOptions());
        var repository = new AssessmentRepository(database);
        var invocation = new BatchInvocationEvidence("invocation/one", "source", "digest", "digest",
            "words", "digest", "tsv", "digest", "stderr", "digest", 1000, StepCap.Default, 1, false);
        repository.Record(Record("base", ["cat", "dog"], [
            new AssessedWord("cat", "timed-out", []), new AssessedWord("dog", "analysed", [])],
            "2026-09-24T11:00:00Z", tokenJson) with { Invocation = invocation });
        repository.Record(Record("correctness", ["cat", "dog"], [
            new AssessedWord("cat", "timed-out", []), new AssessedWord("dog", "analysed", [])],
            "2026-09-24T11:00:00Z", tokenJson) with
        {
            Kind = AssessmentKind.Correctness.ToStoredKind(),
            Invocation = invocation,
        });
        repository.Record(Record("rerun", ["cat"], [new AssessedWord("cat", "analysed", [])],
            "2026-09-24T11:05:00Z", tokenJson));

        var result = CurrentEvidenceQuery.ReadCurrentEvidence(database, project, includeResolvedReadings: false, includeWordContext: false);

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal("base", result.Value!.MatchingAssessment?.AssessmentId);
        Assert.Equal("correctness", result.Value.MatchingCorrectnessAssessmentId);
        Assert.Empty(result.Value.RerunAssessments);
        Assert.Equal("timed-out", result.Value.EffectiveWords.Single(word => word.Word == "cat").Outcome);
        Assert.Equal(2, result.Value.EffectiveWords.Count);
    }

    private static NewAssessmentRecord Record(string id, IReadOnlyList<string> words,
        IReadOnlyList<AssessedWord> results, string savedUtc, string token) => new(
        id, null, null, "pangloss", AssessmentKind.ParseTime.ToStoredKind(), "{}", "sha256:scope",
        "whitespace", "1", token, Selection.Create("run", words), "sha256:outcome",
        "sha256:semantic", "sha256:grammar", "fingerprint", "pipeline", 0, results,
        SavedUtc: savedUtc);

    [Fact]
    public void DefaultTimingUsesExplicitReplacementsAndHistoryKeepsItsOwnRows()
    {
        var fwDataPath = Path.Combine(_root, "timing.fwdata");
        File.WriteAllText(fwDataPath, "synthetic project marker");
        var savedUtc = DateTimeOffset.Parse("2026-09-24T10:00:00Z");
        File.SetLastWriteTimeUtc(fwDataPath, savedUtc.UtcDateTime);
        var project = new ProjectLocator(fwDataPath, "timing");
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var token = new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
            "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64));
        var root = Path.Combine(_root, "baseline");
        new BaselineRepository(database).Record(ProjectWorkspaceKey.Compute(project),
            new BaselinePublication(root, Path.Combine(root, "timing.fwdata"), token),
            savedUtc, savedUtc, TestTextWords.Empty, new ProjectSummarySnapshot(0, 0, 0, 0, 0, [], []));
        new NamedSelectionRepository(database).SetDefault("Default", [], ["cat", "dog"]);
        var tokenJson = JsonSerializer.Serialize(token, MotifJson.CreateOptions());
        var repository = new AssessmentRepository(database);
        static AssessmentObjectTiming Row(string word, long ns) =>
            new("morph_rule", "rule-r", "authored", "analysis", "R", word, 1, null, ns);
        repository.Record(Record("base", ["cat", "dog"], [
            new AssessedWord("cat", "timed-out", [], 90), new AssessedWord("dog", "analysed", [], 5)],
            "2026-09-24T11:00:00Z", tokenJson) with { ObjectTimings = [Row("cat", 9_000_000), Row("dog", 5_000_000)] });
        repository.Record(Record("explicit", ["cat"], [new AssessedWord("cat", "analysed", [], 2)],
            "2026-09-24T11:05:00Z", tokenJson) with
        { ReplacesAssessmentId = "base", ObjectTimings = [Row("cat", 2_000_000)] });
        repository.Record(Record("exploratory", ["dog"], [new AssessedWord("dog", "timed-out", [], 100)],
            "2026-09-24T11:10:00Z", tokenJson) with { ObjectTimings = [Row("dog", 100_000_000)] });

        var evidence = CurrentEvidenceQuery.ReadCurrentEvidence(database, project, includeResolvedReadings: false, includeWordContext: false);
        var timing = TimingCommand.Timing(new TimingRequest(fwDataPath));
        var history = TimingCommand.Timing(new TimingRequest(fwDataPath, AssessmentId: "base"));
        var overview = OverviewCommand.Overview(new OverviewRequest(fwDataPath));

        Assert.True(evidence.Succeeded, evidence.Refusal?.Message);
        Assert.Equal("explicit", Assert.Single(evidence.Value!.RerunAssessments).AssessmentId);
        Assert.Equal([2, 5], evidence.Value.EffectiveWords.Select(word => word.ElapsedMs));
        Assert.Equal(["explicit", "base"], evidence.Value.EffectiveWords.Select(word => word.Origin!.AssessmentId));
        Assert.Equal([DateTimeOffset.Parse("2026-09-24T11:05:00Z"), DateTimeOffset.Parse("2026-09-24T11:00:00Z")],
            evidence.Value.EffectiveWords.Select(word => word.Origin!.MeasuredUtc));
        Assert.Same(evidence.Value.EvidenceSet!.Words, evidence.Value.EffectiveWords);
        Assert.True(timing.Succeeded, timing.Refusal?.Message);
        Assert.Equal([2, 5], timing.Value!.Words.Select(word => word.ElapsedMs));
        Assert.Equal(7, Assert.Single(timing.Value.Aggregates).SelfMs);
        Assert.True(history.Succeeded, history.Refusal?.Message);
        Assert.Equal([90, 5], history.Value!.Words.Select(word => word.ElapsedMs));
        Assert.Equal(14, Assert.Single(history.Value.Aggregates).SelfMs);
        Assert.True(overview.Succeeded, overview.Refusal?.Message);
        Assert.Equal("explicit", overview.Value!.WordOrigins["cat"].AssessmentId);
        Assert.Equal("base", overview.Value.WordOrigins["dog"].AssessmentId);
        Assert.Equal(TimingEvidenceRelation.Current, timing.Value.EvidenceRelation);
        Assert.Equal(token, timing.Value.Baseline);
        Assert.Equal(savedUtc, timing.Value.SourceLastWriteUtc);
        File.SetLastWriteTimeUtc(fwDataPath, savedUtc.UtcDateTime.AddMinutes(1));
        var savedSince = TimingCommand.Timing(new TimingRequest(fwDataPath));
        Assert.True(savedSince.Succeeded, savedSince.Refusal?.Message);
        Assert.Equal(TimingEvidenceRelation.SavedSince, savedSince.Value!.EvidenceRelation);
        Assert.True(savedSince.Value.IsStale);
        Assert.True(savedSince.Value.CurrentProjectIsStale);
        Assert.Equal(savedUtc, savedSince.Value.SourceLastWriteUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HistoricalTimingDoesNotBorrowTheCurrentBaselinesFreshness(bool currentProjectSavedAgain)
    {
        var fwDataPath = Path.Combine(_root, "historic.fwdata");
        File.WriteAllText(fwDataPath, "synthetic project marker");
        var savedUtc = DateTimeOffset.Parse("2026-09-24T10:00:00Z");
        File.SetLastWriteTimeUtc(fwDataPath, savedUtc.UtcDateTime);
        var project = new ProjectLocator(fwDataPath, "historic");
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var oldToken = new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
            "2026-09-23T10:00:00Z", "sha256:" + new string('a', 64));
        var newToken = new BaselineToken("project-id", "sha256:" + new string('2', 64), "projection-v1",
            "2026-09-24T10:00:00Z", "sha256:" + new string('b', 64));
        var root = Path.Combine(_root, "baseline");
        new BaselineRepository(database).Record(ProjectWorkspaceKey.Compute(project),
            new BaselinePublication(root, Path.Combine(root, "historic.fwdata"), newToken),
            savedUtc, savedUtc, TestTextWords.Empty, new ProjectSummarySnapshot(0, 0, 0, 0, 0, [], []));
        new AssessmentRepository(database).Record(Record("old", ["cat"], [new AssessedWord("cat", "analysed", [], 1)],
            "2026-09-23T11:00:00Z", JsonSerializer.Serialize(oldToken, MotifJson.CreateOptions())));
        if (currentProjectSavedAgain) File.SetLastWriteTimeUtc(fwDataPath, savedUtc.UtcDateTime.AddMinutes(1));

        var timing = TimingCommand.Timing(new TimingRequest(fwDataPath, AssessmentId: "old"));

        Assert.True(timing.Succeeded, timing.Refusal?.Message);
        Assert.True(timing.Value!.IsStale);
        Assert.Equal(TimingEvidenceRelation.Historical, timing.Value.EvidenceRelation);
        Assert.Equal(oldToken, timing.Value.Baseline);
        Assert.Null(timing.Value.SourceLastWriteUtc);
        Assert.Equal(currentProjectSavedAgain, timing.Value.CurrentProjectIsStale);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TimingOverridesUseTheSharedOverlayAndKeepTheirOrigin(bool useCurrentEvidence)
    {
        var fwDataPath = Path.Combine(_root, "override.fwdata");
        File.WriteAllText(fwDataPath, "synthetic project marker");
        var savedUtc = DateTimeOffset.Parse("2026-09-24T10:00:00Z");
        File.SetLastWriteTimeUtc(fwDataPath, savedUtc.UtcDateTime);
        var project = new ProjectLocator(fwDataPath, "override");
        using var database = MotifDatabase.OpenOwned(ProjectDatabaseCatalog.DatabasePathFor(project), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        var token = new BaselineToken("project-id", "sha256:" + new string('1', 64), "projection-v1",
            "2026-09-24T10:00:00Z", "sha256:" + new string('a', 64));
        var root = Path.Combine(_root, "baseline");
        new BaselineRepository(database).Record(ProjectWorkspaceKey.Compute(project),
            new BaselinePublication(root, Path.Combine(root, "override.fwdata"), token),
            savedUtc, savedUtc, TestTextWords.Empty, new ProjectSummarySnapshot(0, 0, 0, 0, 0, [], []));
        new NamedSelectionRepository(database).SetDefault("Default", [], ["cat", "dog"]);
        var tokenJson = JsonSerializer.Serialize(token, MotifJson.CreateOptions());
        var repository = new AssessmentRepository(database);
        static AssessmentObjectTiming Row(string word, long ns) =>
            new("morph_rule", "rule-r", "authored", "analysis", "R", word, 1, null, ns);
        repository.Record(Record("base", ["cat", "dog"], [
            new AssessedWord("cat", "timed-out", [], 90), new AssessedWord("dog", "analysed", [], 5)],
            "2026-09-24T11:00:00Z", tokenJson) with { ObjectTimings = [Row("cat", 9_000_000), Row("dog", 5_000_000)] });
        repository.Record(Record("other", ["cat", "bird"], [
            new AssessedWord("cat", "analysed", [], 2), new AssessedWord("bird", "analysed", [], 40)],
            "2026-09-24T11:05:00Z", tokenJson) with { ObjectTimings = [Row("cat", 2_000_000), Row("bird", 40_000_000)] });
        repository.Record(Record("dog-reparse", ["dog"], [new AssessedWord("dog", "analysed", [], 3)],
            "2026-09-24T11:03:00Z", tokenJson) with
        { ReplacesAssessmentId = "base", ObjectTimings = [Row("dog", 3_000_000)] });

        var timing = TimingCommand.Timing(new TimingRequest(fwDataPath, AssessmentId: useCurrentEvidence ? null : "base",
            OverrideAssessmentIds: ["other"]));

        Assert.True(timing.Succeeded, timing.Refusal?.Message);
        Assert.Equal(["cat", "dog"], timing.Value!.Words.Select(word => word.Word));
        Assert.Equal([2, useCurrentEvidence ? 3 : 5], timing.Value.Words.Select(word => word.ElapsedMs));
        Assert.Equal(["other", useCurrentEvidence ? "dog-reparse" : "base"],
            timing.Value.Words.Select(word => word.Origin!.AssessmentId));
        Assert.Equal(useCurrentEvidence ? 5 : 7, Assert.Single(timing.Value.Aggregates).SelfMs);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
