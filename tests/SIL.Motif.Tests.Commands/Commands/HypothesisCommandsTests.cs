using System.Diagnostics;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Commands;

/// <summary>The hypothesis ledger (ADR 0117): rules about what may be recorded, and the cost of every lookup.</summary>
public sealed class HypothesisCommandsTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-hypothesis", Guid.NewGuid().ToString("N"));
    private string? _project;

    // The ledger needs only a project file to key its store; no FieldWorks project is opened.
    private string Project
    {
        get
        {
            if (_project is not null) return _project;
            Directory.CreateDirectory(_root);
            _project = Path.Combine(_root, "ledger.fwdata");
            File.WriteAllText(_project, "placeholder");
            return _project;
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static HypothesisDraft Draft(string change = "d>r", string mechanism = "environment", string? environment = "_ an",
        int predictions = 25) =>
        new("Stem-final d becomes r before -an.", mechanism, change, "none: queried d>r, no row", Environment: environment,
            Predictions: Enumerable.Range(0, predictions).Select(i => $"form{i}").ToList(),
            Questions: [new HypothesisQuestionDraft("Is kadan a word?", "yes")]);

    private HypothesisItem Add(HypothesisDraft draft)
    {
        var outcome = HypothesisCommands.Add(new AddHypothesisRequest(Project, "1.0", draft));
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!.Hypothesis;
    }

    private CommandOutcome<HypothesisResponse> Test(string id, string verdict, int predicted = 25, int hits = 60,
        int? losses = null) => HypothesisCommands.RecordTest(new RecordHypothesisTestRequest(Project, "1.0", id,
        new HypothesisTestResult(verdict, predicted, hits, new Dictionary<string, int> { ["l"] = 20, ["n"] = 30 },
            TrialAttestedLosses: losses)));

    private CommandOutcome<HypothesisResponse> Decide(string id, string status, string? by = null, string[]? supersedes = null) =>
        HypothesisCommands.Decide(new DecideHypothesisRequest(Project, "1.0", id, status, "Decided in words.", by, supersedes));

    private void LinkGdr(string id) => Assert.True(HypothesisCommands.Link(new LinkHypothesisRequest(
        Project, "1.0", id, GdrId: "gdr-8", GdrSection: "Examples")).Succeeded);

    [Fact]
    public void AHypothesisGetsAPermanentIdAndTheNeighboursAlreadyOnItsShape()
    {
        var first = Add(Draft());
        var second = HypothesisCommands.Add(new AddHypothesisRequest(Project, "1.0", Draft(change: "D>R")));

        Assert.Equal("H-1", first.HypothesisId);
        Assert.Equal("H-2", second.Value!.Hypothesis.HypothesisId);
        Assert.Equal(["H-1"], second.Value.Neighbours.Select(n => n.HypothesisId));
        Assert.Equal("proposed", first.Status);
    }

    [Fact]
    public void ANeighbourQueryNeedsTheChangeAndNarrowsByEnvironment()
    {
        Add(Draft(environment: "_ an"));
        Add(Draft(environment: "_ un"));

        var any = HypothesisCommands.Neighbours(new HypothesisNeighboursRequest(Project, "1.0", "d>r")).Value!;
        var narrow = HypothesisCommands.Neighbours(new HypothesisNeighboursRequest(Project, "1.0", "d>r", "_ UN")).Value!;
        var none = HypothesisCommands.Neighbours(new HypothesisNeighboursRequest(Project, "1.0", "n>ng")).Value!;
        var refused = HypothesisCommands.Neighbours(new HypothesisNeighboursRequest(Project, "1.0"));

        Assert.Equal(2, any.Neighbours.Count);
        Assert.Equal(["H-2"], narrow.Neighbours.Select(n => n.HypothesisId));
        Assert.Empty(none.Neighbours);
        Assert.Equal(RefusalCodes.HypothesisInvalid, refused.Refusal!.Code);
    }

    [Fact]
    public void AHypothesisWithoutARecordedNeighbourCheckIsRefused()
    {
        var outcome = HypothesisCommands.Add(new AddHypothesisRequest(Project, "1.0", Draft() with { Neighbour = " " }));

        Assert.Equal(RefusalCodes.HypothesisInvalid, outcome.Refusal!.Code);
        Assert.Contains("neighbour", outcome.Refusal.Message);
    }

    [Fact]
    public void APassNeedsTwentyPredictedFormsAndNoAttestedLoss()
    {
        var id = Add(Draft()).HypothesisId;

        Assert.Equal(RefusalCodes.HypothesisRule, Test(id, "PASS", predicted: 19).Refusal!.Code);
        Assert.Equal(RefusalCodes.HypothesisRule, Test(id, "PASS", losses: 1).Refusal!.Code);
        Assert.True(Test(id, "UNDECIDED", predicted: 19).Succeeded);
        var passed = Test(id, "pass");
        Assert.True(passed.Succeeded, passed.Refusal?.Message);
        Assert.Equal(30, passed.Value!.Hypothesis.Tests[^1].Margin);
    }

    [Fact]
    public void AHypothesisPassesOnlyAfterAPassingTestAndAGdrLink()
    {
        var id = Add(Draft()).HypothesisId;
        Assert.Contains("Record the test first", Decide(id, "passed").Refusal!.Message);

        Test(id, "PASS");
        Assert.Contains("GDR", Decide(id, "passed").Refusal!.Message);

        LinkGdr(id);
        var decided = Decide(id, "passed");
        Assert.True(decided.Succeeded, decided.Refusal?.Message);
        Assert.Equal("GDR-8", decided.Value!.Hypothesis.GdrId);
        Assert.Contains("superseded", Decide(id, "failed").Refusal!.Message);
    }

    [Fact]
    public void ANoGdrReasonStandsInForTheLink()
    {
        var id = Add(Draft()).HypothesisId;
        Test(id, "PASS");
        Assert.True(HypothesisCommands.Link(new LinkHypothesisRequest(Project, "1.0", id, GdrNone: "lexical tag only")).Succeeded);

        Assert.True(Decide(id, "passed").Succeeded);
        Assert.Equal(RefusalCodes.HypothesisInvalid, HypothesisCommands.Link(new LinkHypothesisRequest(
            Project, "1.0", id, GdrId: "GDR-1", GdrSection: "A", GdrNone: "both")).Refusal!.Code);
    }

    // An earlier ruling (build the ordered rule, not a variant) binds a later variant until superseded.
    [Fact]
    public void APassedRuleBindsAnotherMechanismOnTheSameShapeUntilSuperseded()
    {
        var ordered = Add(Draft(change: "hC>Ch", mechanism: "ordered rule", environment: null)).HypothesisId;
        Test(ordered, "PASS");
        LinkGdr(ordered);
        Assert.True(Decide(ordered, "passed").Succeeded);

        var variant = Add(Draft(change: "hC>Ch", mechanism: "bound variant", environment: null)).HypothesisId;
        Test(variant, "PASS");
        LinkGdr(variant);

        var refused = Decide(variant, "passed");
        Assert.Equal(RefusalCodes.HypothesisRule, refused.Refusal!.Code);
        Assert.Contains(ordered, refused.Refusal.Message);

        var replaced = Decide(variant, "passed", supersedes: [ordered]);
        Assert.True(replaced.Succeeded, replaced.Refusal?.Message);
        var old = HypothesisCommands.List(new ListHypothesesRequest(Project, "1.0", HypothesisId: ordered)).Value!.Hypotheses[0];
        Assert.Equal("superseded", old.Status);
        Assert.Equal(variant, old.SupersededBy);
    }

    [Fact]
    public void AFailedAttemptIsOnRecordForTheShapeAndASupersededOneNamesItsReplacement()
    {
        var first = Add(Draft()).HypothesisId;
        Test(first, "FAIL", hits: 10);
        Assert.True(Decide(first, "failed").Succeeded);
        var second = Add(Draft(mechanism: "feature")).HypothesisId;

        var check = HypothesisCommands.Neighbours(new HypothesisNeighboursRequest(Project, "1.0", "d>r")).Value!;
        Assert.True(check.HasFailedAttempt);
        Assert.False(check.HasPassed);
        Assert.Equal(RefusalCodes.HypothesisRule, Decide(first, "superseded").Refusal!.Code);
        Assert.Equal(RefusalCodes.HypothesisRule, Decide(first, "superseded", by: first).Refusal!.Code);
        Assert.True(Decide(first, "superseded", by: second).Succeeded);
    }

    [Fact]
    public void ProposalsAndAGdrLinkToAHypothesisAndBackWithoutScanning()
    {
        var id = Add(Draft()).HypothesisId;
        var linked = HypothesisCommands.Link(new LinkHypothesisRequest(Project, "1.0", id, ["proposal/a", "proposal/b"],
            "GDR-8", "Examples")).Value!.Hypothesis;
        HypothesisCommands.Link(new LinkHypothesisRequest(Project, "1.0", id, ["proposal/a"]));

        Assert.Equal(["proposal/a", "proposal/b"], linked.Proposals);
        Assert.Equal("Examples", linked.GdrSection);
        using var database = ProjectMotifDatabase.Open(Project);
        Assert.Equal([id], new HypothesisRepository(database).HypothesesOf("proposal/b"));
    }

    [Fact]
    public void TheFourNumbersAreGradedInStepsAndObjectsPerTypeAreDerived()
    {
        var id = Add(Draft()).HypothesisId;
        HypothesisCommands.Grade(new GradeHypothesisRequest(Project, "1.0", id, "match", "Wolff", 6, ObjectsBefore: 395, TypesBefore: 400));
        var graded = HypothesisCommands.Grade(new GradeHypothesisRequest(Project, "1.0", id, ObjectsAfter: 10, TypesAfter: 400,
            AgentHours: 3, AlternativeHours: 20)).Value!.Hypothesis.Grade;

        Assert.Equal("match", graded.SealedVerdict);
        Assert.Equal(6, graded.WordQuestionsAsked);
        Assert.Equal(0.988, graded.ObjectsPerTypeBefore);
        Assert.Equal(0.025, graded.ObjectsPerTypeAfter);
        Assert.Equal(20, graded.AlternativeHours);
        Assert.Equal(RefusalCodes.HypothesisInvalid, HypothesisCommands.Grade(new GradeHypothesisRequest(Project, "1.0", id)).Refusal!.Code);
        Assert.Equal(RefusalCodes.HypothesisInvalid,
            HypothesisCommands.Grade(new GradeHypothesisRequest(Project, "1.0", id, "maybe")).Refusal!.Code);
    }

    [Fact]
    public void TheDecisionRecordAndQuestionLogAreGeneratedFromTheLedger()
    {
        var id = Add(Draft()).HypothesisId;
        Test(id, "PASS");
        LinkGdr(id);
        Decide(id, "passed");
        HypothesisCommands.AnswerQuestion(new AnswerHypothesisQuestionRequest(Project, "1.0", id, 1, "yes"));

        var decisions = HypothesisCommands.Report(new HypothesisReportRequest(Project, "1.0", "decisions")).Value!;
        var questions = HypothesisCommands.Report(new HypothesisReportRequest(Project, "1.0", "questions")).Value!;

        Assert.Equal(1, decisions.Count);
        Assert.Contains("H-1 passed: d>r", decisions.Text);
        Assert.Contains("GDR: GDR-8, Examples", decisions.Text);
        Assert.Contains("Is kadan a word? Answer: yes", questions.Text);
        Assert.DoesNotContain("Predicted", questions.Text);
        Assert.Equal(RefusalCodes.HypothesisInvalid, HypothesisCommands.AnswerQuestion(
            new AnswerHypothesisQuestionRequest(Project, "1.0", id, 2, "x")).Refusal!.Code);
    }

    [Fact]
    public void AnUnknownHypothesisIsNotFound()
    {
        var outcome = HypothesisCommands.RecordTest(new RecordHypothesisTestRequest(Project, "1.0", "H-99",
            new HypothesisTestResult("PASS", 25, 60)));

        Assert.Equal(RefusalCodes.HypothesisNotFound, outcome.Refusal!.Code);
    }

    // No check may cost over a second or scan the ledger: plans prove the seeks, timings the cost.
    [Fact]
    public void EveryLookupSeeksAnIndexAndStaysFastOnTenThousandHypotheses()
    {
        Assert.True(Add(Draft()).HypothesisId == "H-1");
        const int Count = 10_000;
        using (var database = ProjectMotifDatabase.Open(Project))
        using (var connection = database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            using var seed = connection.CreateCommand();
            seed.Transaction = transaction;
            seed.CommandText = $$"""
                WITH RECURSIVE n(i) AS (SELECT 2 UNION ALL SELECT i + 1 FROM n WHERE i < {{Count}})
                INSERT INTO Hypotheses (HypothesisId, Seq, Statement, Mechanism, ChangeText, Environment, Status, CreatedUtc, UpdatedUtc)
                SELECT 'H-' || i, i, 'statement', 'environment', 'c' || (i % 2000) || '>x', 'e' || (i % 7),
                       CASE i % 4 WHEN 0 THEN 'passed' WHEN 1 THEN 'failed' ELSE 'proposed' END, 'now', 'now' FROM n;
                INSERT INTO HypothesisProposals (HypothesisId, ProposalId, LinkedUtc)
                SELECT 'H-' || i, 'proposal/' || i, 'now' FROM (SELECT i FROM (WITH RECURSIVE m(i) AS (SELECT 2 UNION ALL SELECT i + 1 FROM m WHERE i < {{Count}}) SELECT i FROM m));
                INSERT INTO HypothesisTests (HypothesisId, TestNumber, Verdict, PredictedForms, RuleHits, ControlHitsJson, Margin, RecordedUtc)
                SELECT 'H-' || i, 1, 'PASS', 25, 60, '{}', 40, 'now' FROM (WITH RECURSIVE m(i) AS (SELECT 2 UNION ALL SELECT i + 1 FROM m WHERE i < {{Count}}) SELECT i FROM m);
                """;
            seed.ExecuteNonQuery();
            transaction.Commit();

            foreach (var (name, sql) in new[]
            {
                ("get by id", "SELECT * FROM Hypotheses WHERE HypothesisId = 'H-5000'"),
                ("next id", "SELECT COALESCE(MAX(Seq), 0) + 1 FROM Hypotheses"),
                ("neighbours", "SELECT * FROM Hypotheses WHERE ChangeText = 'c7>x' COLLATE NOCASE AND Environment = 'e1' COLLATE NOCASE ORDER BY Seq"),
                ("page", "SELECT * FROM Hypotheses ORDER BY Seq LIMIT 50 OFFSET 100"),
                ("page by status", "SELECT * FROM Hypotheses WHERE Status = 'passed' ORDER BY Seq LIMIT 50"),
                ("proposal to hypotheses", "SELECT HypothesisId FROM HypothesisProposals WHERE ProposalId = 'proposal/5000'"),
                ("proposals of hypothesis", "SELECT ProposalId FROM HypothesisProposals WHERE HypothesisId = 'H-5000'"),
                ("tests of hypothesis", "SELECT * FROM HypothesisTests WHERE HypothesisId = 'H-5000' ORDER BY TestNumber"),
            })
            {
                using var plan = connection.CreateCommand();
                plan.CommandText = "EXPLAIN QUERY PLAN " + sql;
                using var reader = plan.ExecuteReader();
                var steps = new List<string>();
                while (reader.Read()) steps.Add(reader.GetString(3));
                output.WriteLine($"{name}: {string.Join(" | ", steps)}");
                Assert.DoesNotContain(steps, step => step.StartsWith("SCAN Hypotheses", StringComparison.Ordinal) &&
                    !step.Contains("INDEX", StringComparison.Ordinal));
                Assert.DoesNotContain(steps, step => step.StartsWith("SCAN HypothesisProposals", StringComparison.Ordinal) &&
                    !step.Contains("INDEX", StringComparison.Ordinal));
                Assert.DoesNotContain(steps, step => step.StartsWith("SCAN HypothesisTests", StringComparison.Ordinal));
            }
        }

        // Warm the store once so a timing measures the operation, not the first open of the process.
        Assert.True(HypothesisCommands.Neighbours(new HypothesisNeighboursRequest(Project, "1.0", "c7>x")).Succeeded);
        double Time(string name, Func<bool> act)
        {
            var clock = Stopwatch.StartNew();
            var ok = act();
            clock.Stop();
            output.WriteLine($"{name}: {clock.Elapsed.TotalMilliseconds:F1} ms on {Count + 1} hypotheses");
            Assert.True(ok, name);
            return clock.Elapsed.TotalMilliseconds;
        }
        var timings = new[]
        {
            Time("add", () => HypothesisCommands.Add(new AddHypothesisRequest(Project, "1.0", Draft(change: "new>x"))).Succeeded),
            Time("neighbour check", () => HypothesisCommands.Neighbours(
                new HypothesisNeighboursRequest(Project, "1.0", "c7>x", "e1")).Value!.Neighbours.Count > 0),
            Time("record test", () => Test("H-5000", "PASS").Succeeded),
            Time("link proposal and GDR", () => HypothesisCommands.Link(new LinkHypothesisRequest(Project, "1.0", "H-5000",
                ["proposal/new"], "GDR-8", "Examples")).Succeeded),
            Time("show one", () => HypothesisCommands.List(new ListHypothesesRequest(Project, "1.0", HypothesisId: "H-5000")).Succeeded),
            Time("decide with guard", () => Decide("H-5000", "superseded", by: "H-1").Succeeded),
            Time("page of fifty", () => HypothesisCommands.List(new ListHypothesesRequest(Project, "1.0", Limit: 50, Offset: 5000)).Succeeded),
        };
        Assert.All(timings, ms => Assert.True(ms < 1000, $"{ms} ms"));
        // Reports and listing the whole ledger are O(n) by nature and run on demand, never inside a check.
        Time("decision report", () => HypothesisCommands.Report(new HypothesisReportRequest(Project, "1.0", "decisions")).Succeeded);
        Time("question log", () => HypothesisCommands.Report(new HypothesisReportRequest(Project, "1.0", "questions")).Succeeded);
    }
}
