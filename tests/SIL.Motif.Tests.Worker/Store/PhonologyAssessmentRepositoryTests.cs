using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Phonology;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class PhonologyAssessmentRepositoryTests : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = PhonologyJson.CreateOptions();
    private static readonly string BareHash = new('a', 64);
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-phonology-assessment-" + Guid.NewGuid().ToString("N"));
    private readonly BaselineToken _baseline = new("project", "sha256:" + new string('b', 64), "projection",
        "2026-01-01T00:00:00Z", "sha256:" + new string('c', 64));

    public PhonologyAssessmentRepositoryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ProducedAndUnavailableReopenWithOriginalCitationsAndIndependentBindings()
    {
        PhonologyExampleSetHeader revision;
        var example = Example("e", new CitationView("Original source", "p. 1"));
        using (var database = Open())
        {
            var sets = new PhonologyExampleSetRepository(database);
            revision = sets.Save("Set", [example], null, null);
            var record = Record("produced", revision, [Row(example)]);
            new AssessmentRepository(database).Record(record);
            new AssessmentRepository(database).Record(Unavailable("unavailable", revision));
            sets.Save("Set", [example with { Citation = new CitationView("Changed source", "p. 2") }],
                revision.Revision.RevisionId, null);
        }
        using var reopened = Open();
        var results = new PhonologyAssessmentRepository(reopened);
        var detail = results.ReadExample("produced", "e");
        Assert.Equal(revision, detail.Assessment.ExampleSet);
        Assert.Equal(example.Citation, detail.Result.Example.Citation);
        var evidence = Assert.IsType<PhonologyRowEvidence.Produced>(detail.Result.Evidence);
        Assert.Equal(new[] { "ordered-1", "ordered-2" }, evidence.OrderedRuleIds);
        Assert.Equal(new[] { "visited" }, evidence.VisitedRuleIds);
        Assert.Equal(new[] { "applied" }, evidence.AppliedRuleIds);
        Assert.Equal(100UL, evidence.EffectiveLimits.StepCap);
        Assert.True(evidence.Capped);
        Assert.True(evidence.TimedOut);
        Assert.Equal(PhonologyRowCompletion.Incomplete, evidence.Completion);
        Assert.Equal(new[] { true }, evidence.ExpectedObserved);
        Assert.Equal("surface", evidence.Observed);
        Assert.Equal(7UL, evidence.Steps);
        Assert.Equal(11UL, evidence.WorkSteps);
        var binding = Assert.IsType<PhonologyInvocationBinding.Produced>(detail.Assessment.Invocation);
        Assert.Equal("sha256:" + new string('f', 64), binding.ProducerRequestSha256);
        Assert.Equal("sha256:" + new string('d', 64), binding.ExecutableSha256);
        Assert.Equal("sha256:" + new string('e', 64), binding.RequestFileSha256);
        Assert.Equal(9UL, binding.MeasuredEntryCount);
        Assert.Equal(new PhonologyDependency("WritingSystemStore/ws.ldml", "sha256:" + BareHash),
            Assert.Single(results.ReadDependencies("produced")));
        var unavailable = results.ReadExample("unavailable", "e");
        Assert.Equal(example.Citation, unavailable.Result.Example.Citation);
        Assert.IsType<PhonologyRowEvidence.Unavailable>(unavailable.Result.Evidence);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(unavailable, JsonOptions));
        var absent = json.RootElement.GetProperty("result").GetProperty("evidence");
        foreach (var field in new[] { "completion", "steps", "workSteps", "effectiveLimits", "expectedObserved", "orderedRuleIds" })
            Assert.False(absent.TryGetProperty(field, out _));
        var invocation = json.RootElement.GetProperty("assessment").GetProperty("invocation");
        Assert.False(invocation.TryGetProperty("measuredEntryCount", out _));
        Assert.False(invocation.TryGetProperty("agreeCount", out _));
    }

    [Fact]
    public void ProducerNotStartedIsProducedAndKeepsZeroAllocationsAndFlags()
    {
        using var database = Open();
        var example = Example("e");
        var first = Example("first");
        var revision = new PhonologyExampleSetRepository(database).Save("Set", [first, example], null, null);
        var row = Row(example) with { ResolvedContext = null, Observed = null,
            Completion = PhonologyCompletion.NotStarted, Reason = PhonologyReason.UnknownEntry,
            Steps = 0, WorkSteps = 0, Capped = false, TimedOut = false, ExpectedObserved = [false],
            OrderedRuleIds = [], VisitedRuleIds = [], AppliedRuleIds = [] };
        new AssessmentRepository(database).Record(Record("not-started", revision, [Row(first), row], new PhonologyLimits(1, 1, 100)));
        var evidence = Assert.IsType<PhonologyRowEvidence.Produced>(
            new PhonologyAssessmentRepository(database).ReadExample("not-started", "e").Result.Evidence);
        Assert.Equal(PhonologyRowCompletion.NotStarted, evidence.Completion);
        Assert.Equal(PhonologyRowReason.UnknownEntry, evidence.Reason);
        Assert.Equal(new PhonologyLimits(0, 0, 100), evidence.EffectiveLimits);
        Assert.False(evidence.Capped);
        Assert.False(evidence.TimedOut);
    }

    [Fact]
    public void HeaderLoadsNoRowsOrDependenciesAndAllWordProjectionsAndPromotionRefuse()
    {
        using var database = Open();
        var example = Example("e");
        var revision = new PhonologyExampleSetRepository(database).Save("Set", [example], null, null);
        var repository = new AssessmentRepository(database);
        repository.Record(Record("header", revision, [Row(example)]));
        using var scope = RepositoryReadCounters.BeginScope();
        var header = new PhonologyAssessmentRepository(database).ReadHeader("header");
        Assert.Equal("header", header.AssessmentId);
        Assert.Equal(new RepositoryReadCount(1, 1), scope.Snapshot());
        var storedHeader = repository.GetHeader("header")!;
        Assert.IsType<AssessmentRecordDetails.Unloaded>(storedHeader.Details);
        Assert.Throws<InvalidOperationException>(() => storedHeader.ToReportable());
        Assert.Throws<InvalidOperationException>(() => storedHeader.ToComparable());
        Assert.Throws<InvalidOperationException>(() => storedHeader.ToCorrectness());
        Assert.Throws<InvalidOperationException>(() => storedHeader.ToStored());
        Assert.Throws<InvalidOperationException>(() => repository.PromoteToCurrent("header"));
        Assert.Null(repository.GetCurrent());
    }

    [Fact]
    public void MismatchedKindMaterialScopeAndAuthoredOrderRollbackAtomically()
    {
        using var database = Open();
        var first = Example("first");
        var second = Example("second");
        var revision = new PhonologyExampleSetRepository(database).Save("Set", [first, second], null, null);
        var record = Record("invalid", revision, [Row(first), Row(second)]);
        var repository = new AssessmentRepository(database);
        Assert.Throws<ArgumentException>(() => repository.Record(record with { Kind = "ParseTime" }));
        Assert.Throws<ArgumentException>(() => repository.Record(record with
            { Material = new AssessmentRecordMaterial.Words([], null, null, []) }));
        Assert.Throws<ArgumentException>(() => repository.Record(record with
            { ScopeJson = ScopeCodec.Write(new StoredScope.PhonologyExamples(revision.Revision, new PhonologyLimits(1, 1, 1))) }));
        Assert.Throws<ArgumentException>(() => repository.Record(Record("invalid", revision, [Row(second), Row(first)])));
        Assert.Throws<ArgumentException>(() => repository.Record(record with { ReplacesAssessmentId = "header" }));
        Assert.Empty(repository.ListByKind("PhonologyExamples"));
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM PhonologyAssessmentBindings;";
        Assert.Equal(0L, command.ExecuteScalar());
    }

    [Fact]
    public void PhonologyCannotReplaceWordsOrBecomeAWordReplacementRoot()
    {
        using var database = Open();
        var example = Example("e");
        var revision = new PhonologyExampleSetRepository(database).Save("Set", [example], null, null);
        var phonology = Record("phonology", revision, [Row(example)]);
        var words = phonology with
        {
            AssessmentId = "words", Kind = "ParseTime", ScopeJson = "{}",
            Subject = new AssessmentSubject.Words(SIL.Motif.Host.Corpus.Selection.Create("Words", ["word"]), "none", "1"),
            Material = new AssessmentRecordMaterial.Words([], null, null, []),
        };
        var repository = new AssessmentRepository(database);
        repository.Record(words);
        repository.Record(phonology);
        repository.PromoteToCurrent("words");
        Assert.Throws<ArgumentException>(() => repository.Record(phonology with
            { AssessmentId = "example-replacement", ReplacesAssessmentId = "words" }));
        Assert.Throws<InvalidOperationException>(() => repository.Record(words with
            { AssessmentId = "word-replacement", ReplacesAssessmentId = "phonology" }));
        Assert.Throws<InvalidOperationException>(() => repository.PromoteToCurrent("phonology"));
        Assert.Equal("words", repository.GetCurrent()!.AssessmentId);
        Assert.Null(repository.GetHeader("word-replacement"));
        Assert.Null(repository.GetHeader("example-replacement"));
    }

    [Fact]
    public void ExactOrdinalForeignKeysAndSharedRetentionPreserveImmutableRevision()
    {
        using var database = Open();
        var proposal = CanonicalId.Mint("proposal/");
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT INTO Proposals (ProposalId, Status) VALUES ($id, 'proposed');";
            command.Parameters.AddWithValue("$id", proposal.Value);
            command.ExecuteNonQuery();
        }
        var example = Example("e");
        var revision = new PhonologyExampleSetRepository(database).Save("Set", [example], null, proposal.Value);
        var repository = new AssessmentRepository(database);
        repository.Record(Record("retained", revision, [Row(example)]) with { ProposalId = proposal });
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE PhonologyAssessmentRows SET Ordinal = 1 WHERE AssessmentId = 'retained';";
            Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
            command.CommandText = "UPDATE PhonologyAssessmentRows SET ExampleId = 'foreign' WHERE AssessmentId = 'retained';";
            Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
        }
        repository.DeleteByProposal(proposal, null);
        Assert.Null(repository.GetHeader("retained"));
        Assert.Single(new PhonologyExampleSetRepository(database).Read(revision.Revision.RevisionId, 1, null).Items);
        using var remaining = database.OpenConnection();
        using var count = remaining.CreateCommand();
        foreach (var table in new[] { "PhonologyAssessmentBindings", "PhonologyAssessmentRows", "PhonologyAssessmentDependencies" })
        {
            count.CommandText = "SELECT COUNT(*) FROM " + table;
            Assert.Equal(0L, count.ExecuteScalar());
        }
    }

    [Fact]
    public void LargeSummariesAdvanceWithinActualUtf8BoundAndExposeExactOmissions()
    {
        using var database = Open();
        var text = string.Concat(Enumerable.Repeat("\U0001F600", 256));
        var examples = Enumerable.Range(0, 100).Select(index => new PhonologyExample("e" + index, text,
            Enumerable.Range(0, 16).Select(form => text[..^2] + (char)('a' + form)).ToArray(),
            new PhonologyGenerationContext.HcXmlEntry(text), new CitationView(new string('"', 300), new string('\\', 120))))
            .ToArray();
        var revision = new PhonologyExampleSetRepository(database).Save("Set", examples, null, null);
        var rules = Enumerable.Range(0, 1024).Select(index => text[..^2] + char.ConvertFromUtf32(0x10000 + index)).ToArray();
        var rows = examples.Select((example, index) => Row(example) with
        {
            ExpectedObserved = Enumerable.Repeat(true, 16).ToArray(),
            OrderedRuleIds = index == 0 ? rules : [], VisitedRuleIds = index == 0 ? rules : [],
            AppliedRuleIds = index == 0 ? rules : [],
        }).ToArray();
        new AssessmentRepository(database).Record(Record("large", revision, rows));
        var results = new PhonologyAssessmentRepository(database);
        var collected = new List<string>();
        PhonologyPageCursor? cursor = null;
        var pages = 0;
        do
        {
            var page = results.ReadPage("large", 100, cursor);
            Assert.NotEmpty(page.Items);
            Assert.True(JsonSerializer.SerializeToUtf8Bytes(page, JsonOptions).Length <= 4 * 1024 * 1024);
            Assert.True(page.Continuation is null || page.Continuation.AfterOrdinal > (cursor?.AfterOrdinal ?? -1));
            foreach (var summary in page.Items)
            {
                var evidence = Assert.IsType<PhonologyRowSummary.Produced>(summary.Evidence);
                Assert.Equal(evidence.AppliedRuleCount, evidence.AppliedRuleIds.Count + evidence.AppliedRulesOmitted);
                if (summary.Ordinal == 0)
                {
                    Assert.Equal(1024, evidence.OrderedRuleCount);
                    Assert.Equal(1024, evidence.VisitedRuleCount);
                    Assert.Equal(32, evidence.AppliedRuleIds.Count);
                    Assert.Equal(992, evidence.AppliedRulesOmitted);
                }
                collected.Add(summary.Example.Id);
            }
            cursor = page.Continuation;
            pages++;
        } while (cursor is not null);
        Assert.Equal(examples.Select(example => example.Id), collected);
        Assert.True(pages > 1);
        var detail = results.ReadExample("large", "e0");
        Assert.Equal(1024, Assert.IsType<PhonologyRowEvidence.Produced>(detail.Result.Evidence).AppliedRuleIds.Count);
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(detail, JsonOptions).Length <= 32 * 1024 * 1024);
    }

    private NewAssessmentRecord Record(string id, PhonologyExampleSetHeader revision, IReadOnlyList<PhonologyMeasuredRow> rows, PhonologyLimits? requested = null)
    {
        var limits = requested ?? new PhonologyLimits(100, 200, 100);
        rows = rows.Select((row, index) =>
        {
            var steps = limits.StepCap / (ulong)rows.Count + ((ulong)index < limits.StepCap % (ulong)rows.Count ? 1UL : 0UL);
            var work = limits.WorkCap / (ulong)rows.Count + ((ulong)index < limits.WorkCap % (ulong)rows.Count ? 1UL : 0UL);
            return row with { EffectiveLimits = new PhonologyLimits(steps, work, limits.TimeoutMs),
                Steps = Math.Min(row.Steps, steps), WorkSteps = Math.Min(row.WorkSteps, work) };
        }).ToArray();
        var measurement = new PhonologyMeasurement(BareHash, [new("WritingSystemStore/ws.ldml", BareHash)],
            new string('e', 64), new string('f', 64), "producer-build", new string('d', 64), BareHash, 1024, limits, 9, rows);
        return new NewAssessmentRecord(id, null, null, "pangloss", "PhonologyExamples",
            ScopeCodec.Write(new StoredScope.PhonologyExamples(revision.Revision, limits)), "sha256:scope",
            new AssessmentSubject.PhonologyExamples(revision.Revision), JsonSerializer.Serialize(_baseline, MotifJson.CreateOptions()),
            null, null, "sha256:" + BareHash, null, null, null,
            new AssessmentRecordMaterial.PhonologyExamples(new PhonologyBaselineSource(_baseline), limits,
                new PhonologyAssessmentOutcome.Produced(measurement)));
    }

    private NewAssessmentRecord Unavailable(string id, PhonologyExampleSetHeader revision)
    {
        var record = Record(id, revision, []);
        return record with { Material = ((AssessmentRecordMaterial.PhonologyExamples)record.Material) with
            { Outcome = new PhonologyAssessmentOutcome.Unavailable(PhonologyInvocationReason.AbsentProducer, "No executable.") } };
    }

    private static PhonologyMeasuredRow Row(PhonologyExample example) => new(example.Id, example.Context, example.Context,
        example.Underlying, example.Expected, new PhonologyLimits(0, 0, 100), PhonologyDisposition.Unavailable,
        PhonologyCompletion.Incomplete, [true], "surface", PhonologyReason.Timeout, 7, 11, true, true,
        ["ordered-1", "ordered-2"], ["visited"], ["applied"]);
    private static PhonologyExample Example(string id, CitationView? citation = null) =>
        new(id, "underlying", ["surface"], new PhonologyGenerationContext.HcXmlEntry("entry"), citation);
    private MotifDatabase Open() => MotifDatabase.OpenOwned(Path.Combine(_root, "project.motif.db"),
        new ProjectLocator(Path.Combine(_root, "project.fwdata"), "project"), MotifSchema.CurrentSchema, new Version(1, 0));
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
