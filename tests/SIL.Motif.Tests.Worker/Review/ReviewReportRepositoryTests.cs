using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Contract.Review;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Worker.Review;

public sealed class ReviewReportRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-review-store-" + Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;
    private readonly ReviewReportRepository _repository;
    private readonly CanonicalId _proposal = CanonicalId.Mint("proposal/");

    public ReviewReportRepositoryTests()
    {
        Directory.CreateDirectory(_root);
        var project = new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project");
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Proposals (ProposalId, CurrentIntentDigest, Status) VALUES ($id, 'sha256:intent', 'proposed');";
        command.Parameters.AddWithValue("$id", _proposal.Value);
        command.ExecuteNonQuery();
        _repository = new ReviewReportRepository(_database);
    }

    [Fact]
    public async Task InterruptedPublicationExposesNoPartialReport()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.PublishAsync(Publication("one"), Interrupted(), CancellationToken.None));
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ReviewReports;";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public async Task SameManifestReusesId()
    {
        var first = await _repository.PublishAsync(Publication("same"), Cases(Case("a")), CancellationToken.None);
        var second = await _repository.PublishAsync(Publication("same"), Cases(Case("a")), CancellationToken.None);
        Assert.Equal(first, second);
        Assert.NotNull(_repository.ReadManifest(first));
    }

    [Fact]
    public async Task LaterParsimonyCreatesNewIdWithoutChangingOldSummary()
    {
        var first = await _repository.PublishAsync(Publication("before"), Cases(Case("a")), CancellationToken.None);
        var old = _repository.ReadManifest(first)!;
        var second = await _repository.PublishAsync(Publication("after"), Cases(Case("a")), CancellationToken.None);
        Assert.NotEqual(first, second);
        Assert.Equal(JsonSerializer.Serialize(old.Summary, ReviewJson.Options),
            JsonSerializer.Serialize(_repository.ReadManifest(first)!.Summary, ReviewJson.Options));
    }

    [Fact]
    public async Task OpenProposalInputsAreProtected()
    {
        var assessments = new AssessmentRepository(_database);
        assessments.Record(Assessment("assessment-one"));
        assessments.Record(Assessment("unreferenced"));
        var id = await _repository.PublishAsync(Publication("protected") with { InputReferences = ["assessment-one"] },
            Cases(Case("a")), CancellationToken.None);
        assessments.DeleteByProposal(_proposal, exceptAssessmentId: null);
        Assert.Equal("assessment-one", assessments.Get("assessment-one").AssessmentId);
        Assert.Throws<KeyNotFoundException>(() => assessments.Get("unreferenced"));
        Assert.True(_repository.ReadManifest(id)!.DetailAvailable);
    }

    [Fact]
    public async Task DeferDropsDetailButKeepsSummary()
    {
        var id = await _repository.PublishAsync(Publication("deferred"), Cases(Case("a")), CancellationToken.None);
        var before = _repository.ReadManifest(id)!;
        using (var connection = _database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            ReviewReportRetention.DropDetail(connection, transaction, _proposal.Value, "2026-01-01T00:00:00.0000000Z");
            transaction.Commit();
        }
        var after = _repository.ReadManifest(id)!;
        Assert.False(after.DetailAvailable);
        Assert.Equal(JsonSerializer.Serialize(before.Summary.Summary, ReviewJson.Options),
            JsonSerializer.Serialize(after.Summary.Summary, ReviewJson.Options));
        Assert.Empty(after.InputReferences);
    }

    [Fact]
    public async Task PromotedAssessmentSurvivesCleanup()
    {
        var assessments = new AssessmentRepository(_database);
        assessments.Record(Assessment("current-assessment"));
        var id = await _repository.PublishAsync(Publication("promoted") with { InputReferences = ["current-assessment"] },
            Cases(Case("a")), CancellationToken.None);
        using (var connection = _database.OpenConnection())
        using (var transaction = connection.BeginTransaction())
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE MotifMetadata SET CurrentAssessmentId = 'current-assessment';";
            command.ExecuteNonQuery();
            ReviewReportRetention.DropDetail(connection, transaction, _proposal.Value, "2026-01-01T00:00:00.0000000Z");
            transaction.Commit();
        }
        assessments.DeleteByProposal(_proposal, exceptAssessmentId: null);
        Assert.Equal("current-assessment", assessments.Get("current-assessment").AssessmentId);
        Assert.NotNull(_repository.ReadManifest(id));
    }

    [Fact]
    public async Task BudgetRefusalDoesNotAdvertiseCompleteDetail()
    {
        var id = await _repository.PublishAsync(Publication("budget") with { EstimatedDetailBytes = 200_000_000 },
            Cases(Case("a")), CancellationToken.None);
        var result = _repository.ReadManifest(id)!;
        Assert.False(result.DetailAvailable);
        Assert.Equal(DetailAvailability.Unavailable, result.Summary.DetailAvailability);
        Assert.Contains(result.Summary.Limitations, item => item.Code == "store-budget");
        Assert.Null(result.Summary.Page);
    }

    [Fact]
    public async Task StoreUpgradeCarriesSummaryAndReleasesDetail()
    {
        var id = await _repository.PublishAsync(Publication("upgrade"), Cases(Case("a")), CancellationToken.None);
        using (var connection = _database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "PRAGMA user_version=62; PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }
        var output = Path.Combine(_root, "upgraded.db");
        MotifStoreUpgrade.Copy(Path.Combine(_root, "project.motif.db"), output);
        using var upgraded = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = output }.ToString());
        upgraded.Open();
        using var read = upgraded.CreateCommand();
        read.CommandText = "SELECT SummaryJson,DetailAvailable,InputReferencesJson FROM ReviewReports WHERE ReportId=$id;";
        read.Parameters.AddWithValue("$id", id.Value);
        using var rows = read.ExecuteReader();
        Assert.True(rows.Read());
        var summary = JsonSerializer.Deserialize<ReportView>(rows.GetString(0), ReviewJson.Options)!;
        Assert.Equal(DetailAvailability.Unavailable, summary.DetailAvailability);
        Assert.Equal(0L, rows.GetInt64(1));
        Assert.Equal("[]", rows.GetString(2));
    }

    [Fact]
    public async Task UnderestimatedDetailNeverPublishesPartialRows()
    {
        var id = await _repository.PublishAsync(Publication("underestimate") with { EstimatedDetailBytes = 1 },
            Cases(Case("a"), Case("b")), CancellationToken.None);
        Assert.False(_repository.ReadManifest(id)!.DetailAvailable);
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ReviewReportCases;";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public async Task BoundedDifferenceRowsRemainUnchanged()
    {
        using var connection = _database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM ProposalDifferenceCases;";
        var before = command.ExecuteScalar();
        await _repository.PublishAsync(Publication("independent"), Cases(Case("a")), CancellationToken.None);
        Assert.Equal(before, command.ExecuteScalar());
    }

    private ReviewReportPublication Publication(string digest) =>
        new("project", _proposal.Value, "trial", "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(digest))),
            Report(), [], 1_000_000);

    private NewAssessmentRecord Assessment(string id) => new(
        AssessmentId: id, ProposalId: _proposal, ProposalIntentDigest: "sha256:intent", Assessor: "pangloss",
        Kind: "Correctness", ScopeJson: "{}", ScopeDigest: "sha256:scope",
        Subject: new AssessmentSubject.Words(Selection.Create("selection", ["sample"]), "whitespace", "1"),
        BaselineToken: "{}", OutcomeDigest: "sha256:outcome", SemanticDigest: "sha256:semantic",
        GrammarSourceSha256: "sha256:grammar", ModelFingerprint: "fingerprint", Pipeline: "pipeline",
        DiagnosticCount: 0, Material: new AssessmentRecordMaterial.Words([], null, null, []), SavedUtc: null);

    private static ReviewCaseDelta Case(string key) => new(key, key, key, "ws", false, false, false, [], [], [], []);
    private static async IAsyncEnumerable<ReviewCaseDelta> Cases(params ReviewCaseDelta[] cases)
    {
        foreach (var item in cases) { yield return item; await Task.Yield(); }
    }
    private static async IAsyncEnumerable<ReviewCaseDelta> Interrupted()
    {
        yield return Case("a");
        await Task.Yield();
        throw new InvalidOperationException("interrupted");
    }

    private ReportView Report()
    {
        var report = (ReportView)Create(typeof(ReportView));
        return report with
        {
            Schema = "motif.proposal-review-report", SchemaVersion = 1,
            Pins = report.Pins with { ProjectKey = "project", ProposalId = _proposal.Value, TrialJobId = "trial" },
            Summary = report.Summary with
            {
                Notice = "No red flags means only 'not bad', never 'good'.",
                Incomplete = report.Summary.Incomplete with { Label = "Incomplete" },
            },
            FlagEvidence = Enum.GetValues<ReviewFlagId>().Select(id => new FlagEvidence(id, EvidenceState.Unavailable, [], 0)).ToArray(),
        };
    }
    private static object Create(Type type)
    {
        if (type == typeof(BaselineToken)) return new BaselineToken("project", "sha256:" + new string('a', 64), "v1",
            "2026-01-01T00:00:00Z", "sha256:" + new string('b', 64));
        if (type == typeof(string)) return "test";
        if (type == typeof(long)) return 1L;
        if (type == typeof(int)) return 1;
        if (type == typeof(double)) return 1d;
        if (type == typeof(bool)) return false;
        if (type.IsEnum) return Enum.GetValues(type).GetValue(0)!;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
            return Array.CreateInstance(type.GetGenericArguments()[0], 0);
        if (type == typeof(ReadTarget)) return new ReadTarget(TargetKind.Resource, "test", "motif://test", null, null);
        if (type == typeof(EvidencePin)) return Create(typeof(SetEvidencePin));
        if (type == typeof(FlagFacts) || type == typeof(WorkChange)) return new WorkChange(null, 0, "test", WorkComparison.NotComparable, []);
        var constructor = type.GetConstructors().Single();
        return constructor.Invoke(constructor.GetParameters().Select(parameter =>
            Nullable.GetUnderlyingType(parameter.ParameterType) is not null ||
            new NullabilityInfoContext().Create(parameter).ReadState == NullabilityState.Nullable
                ? null : Create(parameter.ParameterType)).ToArray());
    }
    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
