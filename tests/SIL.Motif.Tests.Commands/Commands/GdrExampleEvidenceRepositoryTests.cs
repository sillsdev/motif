using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Store;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>The GDR sync check reads example words from stored Assessment rows and never parses (ADR 0119).</summary>
public sealed class GdrExampleEvidenceRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-gdr-evidence", Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;
    private long _word;

    public GdrExampleEvidenceRepositoryTests()
    {
        Directory.CreateDirectory(_root);
        var project = Path.Combine(_root, "evidence.fwdata");
        File.WriteAllText(project, "placeholder");
        _database = ProjectMotifDatabase.Open(project);
    }

    public void Dispose()
    {
        _database.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private void Exec(string sql, params (string Name, object? Value)[] parameters)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    private void Assessment(string id, string saved, string kind = "ParseTime", string? ownerOf = null)
    {
        Exec("""
            INSERT INTO Assessments (AssessmentId, SelectionName, SelectionWordsJson, SelectionSha256, GrammarSourceSha256,
                SavedUtc, Assessor, Kind, ScopeJson, ScopeDigest, TokeniserName, TokeniserVersion, BaselineToken, SubjectKind)
            VALUES ($id, 'sel', '[]', 'sha', 'g', $saved, 'test', $kind, '{}', 'd', 't', '1', 'b', 'words');
            """, ("$id", id), ("$saved", saved), ("$kind", kind));
        if (ownerOf is not null)
            Exec("INSERT INTO AssessmentWordSetBindings (AssessmentId, OwnerAssessmentId) VALUES ($id, $owner);",
                ("$id", id), ("$owner", ownerOf));
    }

    private void Word(string assessment, string word, string outcome, params Guid[][] analyses)
    {
        var id = ++_word;
        Exec("INSERT INTO AssessedWords (AssessedWordId, AssessmentId, OrdinalIndex, Word, Outcome) VALUES ($w, $a, $w, $word, $o);",
            ("$w", id), ("$a", assessment), ("$word", word), ("$o", outcome));
        for (var index = 0; index < analyses.Length; index++)
            Exec("INSERT INTO ParsedAnalyses (AssessedWordId, OrdinalIndex, CategoryGuid, MorphemeGuidsJson, RootIndex, IdentityDigest) " +
                "VALUES ($w, $i, NULL, $json, 0, 'x');",
                ("$w", id), ("$i", index), ("$json", System.Text.Json.JsonSerializer.Serialize(analyses[index].Select(g => g.ToString("D")))));
    }

    [Fact]
    public void TheNewestConclusiveRowWinsAndAnInconclusiveOneFallsThroughToAnOlderAssessment()
    {
        var morpheme = Guid.NewGuid();
        Assessment("old", "2026-11-01T00:00:00Z");
        Word("old", "palit", "analysed", [morpheme]);
        Word("old", "isda", "analysed", [morpheme]);
        Assessment("new", "2026-11-02T00:00:00Z");
        Word("new", "palit", "no-analysis");
        Word("new", "isda", "timed-out");

        var found = new GdrExampleEvidenceRepository(_database).Read(["palit", "isda", "lupad"]);

        Assert.False(found["palit"].Parsed);
        Assert.Equal("new", found["palit"].AssessmentId);
        Assert.True(found["isda"].Parsed);
        Assert.Equal("old", found["isda"].AssessmentId);
        Assert.Equal([morpheme], found["isda"].Analyses.Single());
        Assert.DoesNotContain("lupad", found.Keys);
    }

    [Fact]
    public void ABoundWordSetIsReadThroughItsOwnerAndOtherKindsAreIgnored()
    {
        Assessment("owner", "2026-11-01T00:00:00Z");
        Word("owner", "palit", "analysed", [Guid.NewGuid()]);
        Assessment("bound", "2026-11-03T00:00:00Z", ownerOf: "owner");
        Assessment("diff", "2026-11-04T00:00:00Z", kind: "Difference");
        Word("diff", "palit", "no-analysis");

        var found = new GdrExampleEvidenceRepository(_database).Read(["palit"]);

        Assert.True(found["palit"].Parsed);
        Assert.Equal("bound", found["palit"].AssessmentId);
    }

    [Fact]
    public void AnEmptyStoreAnswersNothing()
    {
        Assert.Empty(new GdrExampleEvidenceRepository(_database).Read(["palit"]));
    }

    [Fact]
    public void OnlyPassedHypothesesWithAGdrLinkAreListed()
    {
        var project = Path.Combine(_root, "evidence.fwdata");
        string Add(string change)
        {
            var outcome = HypothesisCommands.Add(new AddHypothesisRequest(project, "1.0", new HypothesisDraft(
                "Final vowel drops.", "environment", change, "none-found", Environment: "_ an",
                Predictions: [.. Enumerable.Range(0, 25).Select(i => $"form{i}")],
                Questions: [new HypothesisQuestionDraft("Is kata a word?", "yes")])));
            Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
            return outcome.Value!.Hypothesis.HypothesisId;
        }

        var passed = Add("a>0");
        var proposed = Add("e>0");
        foreach (var id in new[] { passed, proposed })
            Assert.True(HypothesisCommands.Link(new LinkHypothesisRequest(project, "1.0", id, [], "GDR-8", "Analysis", null)).Succeeded);
        Assert.True(HypothesisCommands.RecordTest(new RecordHypothesisTestRequest(project, "1.0", passed,
            new HypothesisTestResult("PASS", 25, 60, new Dictionary<string, int> { ["l"] = 20 }, TrialAttestedLosses: 0))).Succeeded);
        Assert.True(HypothesisCommands.Decide(new DecideHypothesisRequest(project, "1.0", passed, "passed", "Held.", null, null)).Succeeded);

        var links = new HypothesisRepository(_database).PassedGdrLinks();

        Assert.Equal([(passed, "GDR-8", (string?)"Analysis")], links);
    }
}
