using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

/// <summary>A project store is held to its byte budget, oldest unneeded evidence first, and ObjectTiming to five.</summary>
public sealed class StoreByteBudgetTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-byte-budget-" + Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;
    private readonly AssessmentRepository _repository;

    public StoreByteBudgetTests()
    {
        Directory.CreateDirectory(_root);
        var project = new ProjectLocator(Path.Combine(_root, "budget.fwdata"), "budget");
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "budget.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        _repository = new AssessmentRepository(_database);
    }

    [Fact]
    public void ObjectTimingAssessmentsAreKeptFiveAtATimeWhateverTheGeneralCount()
    {
        for (var run = 0; run < 8; run++) Record($"timing-{run}", "ObjectTiming", run);
        for (var run = 0; run < 8; run++) Record($"parse-{run}", "ParseTime", run);

        Assert.Equal(3, Sweep(20, null));

        Assert.Equal(["timing-3", "timing-4", "timing-5", "timing-6", "timing-7"],
            Ids("SELECT AssessmentId FROM Assessments WHERE Kind = 'ObjectTiming' ORDER BY AssessmentId;"));
        Assert.Equal(8, Ids("SELECT AssessmentId FROM Assessments WHERE Kind = 'ParseTime';").Count);
    }

    [Fact]
    public void AStoreOverItsBudgetLosesItsOldestEvidenceUntilItFits()
    {
        var empty = BytesInUse();
        for (var run = 0; run < 12; run++) Record($"run-{run:D2}", "ParseTime", run);
        var budget = empty + (BytesInUse() - empty) * 6 / 10;

        var removed = Sweep(100, budget);

        Assert.True(removed > 0);
        Assert.True(BytesInUse() <= budget, $"{BytesInUse()} bytes used for a budget of {budget}");
        var kept = Ids("SELECT AssessmentId FROM Assessments ORDER BY AssessmentId;");
        Assert.Equal(Enumerable.Range(12 - kept.Count, kept.Count).Select(run => $"run-{run:D2}"), kept);
        Assert.Contains("run-11", kept);
    }

    [Fact]
    public void AStoreInsideItsBudgetIsLeftAlone()
    {
        for (var run = 0; run < 4; run++) Record($"run-{run}", "ParseTime", run);

        Assert.Equal(0, Sweep(100, BytesInUse() * 2));
        Assert.Equal(4, Ids("SELECT AssessmentId FROM Assessments;").Count);
    }

    [Fact]
    public void ThePinnedAndTheCurrentAssessmentsSurviveABudgetNothingCouldMeet()
    {
        for (var run = 0; run < 6; run++) Record($"run-{run}", "ParseTime", run);
        Execute("INSERT INTO AssessmentPins VALUES ('run-0', 'person', '2026-01-01T00:00:00Z');");
        Execute("UPDATE MotifMetadata SET CurrentAssessmentId = 'run-5';");

        Sweep(100, 1);

        Assert.Equal(["run-0", "run-5"], Ids("SELECT AssessmentId FROM Assessments ORDER BY AssessmentId;"));
    }

    [Fact]
    public void AnOpenProposalsAssessmentsSurviveTheBudgetButAClosedOnesDoNot()
    {
        var open = CanonicalId.Mint("proposal/");
        var closed = CanonicalId.Mint("proposal/");
        Execute($"INSERT INTO Proposals (ProposalId, Status) VALUES ('{open.Value}', 'proposed'), ('{closed.Value}', 'applied');");
        Record("of-open", "Correctness", 0, open);
        Record("of-closed", "Correctness", 1, closed);
        Record("standalone", "Correctness", 2);

        Sweep(100, 1);

        Assert.Equal(["of-open"], Ids("SELECT AssessmentId FROM Assessments ORDER BY AssessmentId;"));
    }

    [Fact]
    public void AnAssessmentACitedReportNeedsSurvivesTheBudget()
    {
        Record("cited", "Correctness", 0);
        Record("loose", "Correctness", 1);
        Execute("INSERT INTO Reports (ReportId, AssessmentId, ReportJson, CreatedUtc, Kind) " +
            "VALUES ('report-1', 'cited', '{}', '2026-01-01T00:00:00Z', 'correctness');");

        Sweep(100, 1);

        Assert.Equal(["cited"], Ids("SELECT AssessmentId FROM Assessments;"));
    }

    [Fact]
    public void ThePayloadsOfARemovedAssessmentLeaveWithIt()
    {
        for (var run = 0; run < 6; run++) Record($"run-{run}", "ParseTime", run);

        Sweep(100, 1);

        Assert.Equal("0", Scalar("SELECT COUNT(*) FROM AssessmentWordPayloads;"));
        Assert.Equal("0", Scalar("SELECT COUNT(*) FROM AssessedWords;"));
    }

    [Fact]
    public void ADecidedProposalStillHoldingWordDetailLosesItButKeepsItsSummary()
    {
        var id = CanonicalId.Mint("proposal/");
        Execute($"INSERT INTO Proposals (ProposalId, Status) VALUES ('{id.Value}', 'applied');");
        foreach (var sql in new[]
        {
            $"INSERT INTO ProposalDifferences VALUES ('difference/1', '{id.Value}', 'job', 'b', 'a', 'text', 'human', 'token', '{{}}', 'd', 'now', NULL);",
            "INSERT INTO ProposalDifferenceCases VALUES ('difference/1', 'w', 'w', 1, 'x', 'y', 0, '{}', 'd');",
            "INSERT INTO ProposalDifferenceOccurrences VALUES ('difference/1', 'text', 'seg', 0, 1, 'w');",
            "INSERT INTO ProposalDifferenceBuckets VALUES ('difference/1', 'w', 'delta.regression');",
        })
            Execute(sql);

        using (var connection = _database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            ProposalDifferenceRetention.DropForClosedProposals(connection, transaction);
            transaction.Commit();
        }

        foreach (var table in new[] { "ProposalDifferenceCases", "ProposalDifferenceOccurrences", "ProposalDifferenceBuckets" })
            Assert.Equal("0", Scalar($"SELECT COUNT(*) FROM {table};"));
        Assert.Equal("1", Scalar("SELECT COUNT(*) FROM ProposalDifferences WHERE WordDetailDroppedUtc IS NOT NULL;"));
    }

    private int Sweep(int keep, long? budget)
    {
        using var lease = ProjectParseLease.TryAcquire(_database)!;
        using var connection = _database.OpenConnection();
        using var transaction = connection.BeginTransaction();
        var removed = AssessmentRetention.SweepStandalone(connection, transaction, lease, keep, [], out _,
            byteBudget: budget);
        transaction.Commit();
        return removed;
    }

    private void Record(string id, string kind, int run, CanonicalId? proposal = null)
    {
        var words = Enumerable.Range(0, 60)
            .Select(index => SIL.Motif.Tests.TestFixtures.CorrectnessFixture.Word($"w{run:D2}-{index:D3}", true)).ToArray();
        _repository.Record(new NewAssessmentRecord(
            AssessmentId: id, ProposalId: proposal, ProposalIntentDigest: proposal is null ? null : "sha256:intent",
            Assessor: "pangloss", Kind: kind, ScopeJson: "{\"words\":\"all\"}", ScopeDigest: "sha256:scope",
            Subject: new AssessmentSubject.Words(Selection.Create("selection-1", ["bo", "za"]), "whitespace", "1"),
            BaselineToken: "{\"baseline\":true}", OutcomeDigest: "sha256:outcome", SemanticDigest: "sha256:semantic",
            GrammarSourceSha256: "sha256:grammar", ModelFingerprint: "fingerprint", Pipeline: "pipeline",
            DiagnosticCount: 0, Material: new AssessmentRecordMaterial.Words(words, null, null, []),
            SavedUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMinutes(run).ToString("O")));
    }

    private long BytesInUse()
    {
        using var connection = _database.OpenConnection();
        return AssessmentRetention.BytesInUse(connection, null);
    }

    private void Execute(string sql)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private string Scalar(string sql)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    private List<string> Ids(string sql)
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read()) values.Add(reader.GetString(0));
        return values;
    }

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }
}
