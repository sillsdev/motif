using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Parsimony.RuleAttempts;
using SIL.Motif.Contract.Review;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>A rule attempt is a hypothesis on the shape, passed or failed; it clears the opt-in hold (ADR 0116, 0117).</summary>
public sealed class RuleAttemptLedgerTests : IDisposable
{
    private const string Optin = "P-variant-same-shape";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-attempt-ledger", Guid.NewGuid().ToString("N"));
    private readonly string _project;

    public RuleAttemptLedgerTests()
    {
        Directory.CreateDirectory(_root);
        _project = Path.Combine(_root, "ledger.fwdata");
        File.WriteAllText(_project, "placeholder");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private static List<AddedObjectShape> Variants(int n) =>
        [.. Enumerable.Range(0, n).Select(i => new AddedObjectShape($"v{i}", "free", ["e1"], [],
            StringCorrespondence.Of("kata" + i + "a", "kata" + i + "-")))];

    private string Add(string change, string? environment = null)
    {
        var outcome = HypothesisCommands.Add(new AddHypothesisRequest(_project, "1.0", new HypothesisDraft(
            "Final vowel drops before a clitic.", "environment", change, "none-found", Environment: environment,
            Predictions: [.. Enumerable.Range(0, 25).Select(i => $"form{i}")],
            Questions: [new HypothesisQuestionDraft("Is kata a word?", "yes")])));
        Assert.True(outcome.Succeeded, outcome.Refusal?.Message);
        return outcome.Value!.Hypothesis.HypothesisId;
    }

    private void Test(string id, string verdict) => Assert.True(HypothesisCommands.RecordTest(
        new RecordHypothesisTestRequest(_project, "1.0", id, new HypothesisTestResult(verdict, 25, verdict == "PASS" ? 60 : 10,
            new Dictionary<string, int> { ["l"] = 20 }, TrialAttestedLosses: 0))).Succeeded);

    private RuleAttemptSummary Summary(IReadOnlyList<AddedObjectShape> shapes)
    {
        using var database = ProjectMotifDatabase.Open(_project);
        return RuleAttemptSummary.Create(shapes, new HypothesisRuleAttemptLedger(new HypothesisRepository(database)), [Optin]);
    }

    [Fact]
    public void AGroupHoldsUntilAFailedOrPassedHypothesisIsOnItsShape()
    {
        var shapes = Variants(5);
        var key = shapes[0].Key;
        Assert.True(Summary(shapes).Hold);

        var id = Add(key);
        Assert.True(Summary(shapes).Hold);

        Test(id, "FAIL");
        Assert.True(HypothesisCommands.Decide(new DecideHypothesisRequest(_project, "1.0", id, "failed", "Attested losses.", null, null)).Succeeded);
        var cleared = Summary(shapes);
        Assert.False(cleared.Hold);
        Assert.True(Assert.Single(cleared.Groups).AttemptRecorded);
    }

    [Fact]
    public void APassedHypothesisOnTheCorrespondenceClearsTheHoldAndAnotherShapeStillHolds()
    {
        var shapes = Variants(5);
        var id = Add(shapes[0].Correspondence.Key);
        Test(id, "PASS");
        Assert.True(HypothesisCommands.Link(new LinkHypothesisRequest(_project, "1.0", id, GdrId: "gdr-8", GdrSection: "Examples")).Succeeded);
        Assert.True(HypothesisCommands.Decide(new DecideHypothesisRequest(_project, "1.0", id, "passed", "Rule stands.", null, null)).Succeeded);

        Assert.False(Summary(shapes).Hold);
        var other = Enumerable.Range(0, 5).Select(i => new AddedObjectShape($"w{i}", "free", ["e1"], [],
            StringCorrespondence.Of("kata" + i, "ka" + i))).ToList();
        Assert.True(Summary(other).Hold);
    }

    [Fact]
    public void AHypothesisConditionedOnAnotherEnvironmentDoesNotAnswerTheGroup()
    {
        var shapes = Variants(5);
        var id = Add(shapes[0].Correspondence.Key, environment: "some-other-env");
        Test(id, "FAIL");
        HypothesisCommands.Decide(new DecideHypothesisRequest(_project, "1.0", id, "failed", "No.", null, null));

        Assert.True(Summary(shapes).Hold);
    }
}
