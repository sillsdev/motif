using System;
using System.Collections.Generic;
using System.IO;
using SIL.Motif.Commands.Queries;
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
    public void CurrentEvidenceIncludesLaterSubsetResultsForTheDefaultSelection()
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

        var result = CurrentEvidenceQuery.ReadCurrentEvidence(database, project);

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal("base", result.Value!.MatchingAssessment?.AssessmentId);
        Assert.Equal("correctness", result.Value.MatchingCorrectnessAssessmentId);
        Assert.Equal("rerun", Assert.Single(result.Value.RerunAssessments).AssessmentId);
        Assert.Equal("analysed", result.Value.EffectiveWords.Single(word => word.Word == "cat").Outcome);
        Assert.Equal(2, result.Value.EffectiveWords.Count);
    }

    private static NewAssessmentRecord Record(string id, IReadOnlyList<string> words,
        IReadOnlyList<AssessedWord> results, string savedUtc, string token) => new(
        id, null, null, "pangloss", AssessmentKind.ParseTime.ToStoredKind(), "{}", "sha256:scope",
        "whitespace", "1", token, Selection.Create("run", words), "sha256:outcome",
        "sha256:semantic", "sha256:grammar", "fingerprint", "pipeline", 0, results,
        SavedUtc: savedUtc);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
