using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class AssessmentRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-assessments-" + Guid.NewGuid().ToString("N"));
    private readonly List<MotifDatabase> _databases = [];

    public AssessmentRepositoryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void WordEvidenceIsCompressedAndRoundTrips()
    {
        var repository = NewRepository("compressed-word.fwdata", out var database);
        using var ownedDatabase = database;
        var word = SIL.Motif.Tests.TestFixtures.CorrectnessFixture.Word("same", true);
        repository.Record(NewAssessment("compressed", null, null, "Correctness", [word]));

        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT typeof(MorphologyJson), typeof(CorrectnessJson) FROM AssessedWords WHERE AssessmentId = 'compressed';";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());
        Assert.Equal("blob", reader.GetString(0));
        Assert.Equal("blob", reader.GetString(1));
        var restored = Assert.Single(repository.Get("compressed").Words!);
        Assert.Equal(JsonSerializer.Serialize(word.Morphology), JsonSerializer.Serialize(restored.Morphology));
        Assert.Equal(JsonSerializer.Serialize(word.Correctness), JsonSerializer.Serialize(restored.Correctness));
    }

    [Fact]
    public void IdenticalEvidenceIsSharedAcrossKindsAndDifferentEvidenceSurvives()
    {
        var repository = NewRepository("shared-words.fwdata", out var database);
        using var ownedDatabase = database;
        var word = SIL.Motif.Tests.TestFixtures.CorrectnessFixture.Word("same", true);
        var changed = word with { Morphology = word.Morphology! with { Capped = true } };
        repository.RecordBatch([
            NewAssessment("parse", null, null, "ParseTime", [word]),
            NewAssessment("correct", null, null, "Correctness", [word]),
            NewAssessment("different", null, null, "Correctness", [changed]),
        ]);
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(DISTINCT MorphologyJson) FROM AssessedWords;";
        Assert.Equal(2L, command.ExecuteScalar());
        Assert.False(Assert.Single(repository.Get("parse").Words!).Morphology!.Capped);
        Assert.True(Assert.Single(repository.Get("different").Words!).Morphology!.Capped);
    }

    [Fact]
    public void AttemptsAndWorkStepsSurviveReadBackAndAMissingWorkStaysNull()
    {
        var repository = NewRepository("work-steps.fwdata", out var database);
        using var ownedDatabase = database;
        repository.Record(NewAssessment("work-steps", null, null, "ParseTime",
        [
            new AssessedWord("bo", "capped", []) { Attempts = 200_000, WorkSteps = 20_000_000_000L, IsIncomplete = true },
            new AssessedWord("za", "analysed", []) { Attempts = 12 },
        ]));

        var rows = repository.Get("work-steps").Words!;

        Assert.Equal((200_000, 20_000_000_000L), (rows[0].Attempts, rows[0].WorkSteps));
        Assert.Equal((12, (long?)null), (rows[1].Attempts, rows[1].WorkSteps));
    }

    [Fact]
    public void SharedMorphologyAndApprovedExpectationsSurviveReadBackWithBothLimitFlags()
    {
        var repository = NewRepository("morphology.fwdata", out var database);
        using var ownedDatabase = database;
        var complete = SIL.Motif.Tests.TestFixtures.CorrectnessFixture.Word("same", true);
        var partial = complete with { Outcome = "timed-out", Morphology = complete.Morphology! with { Capped = true, TimedOut = true } };
        partial = partial with { Correctness = MorphologyCorrectness.Compare(partial.Morphology!, complete.Correctness!.Expectations) };
        repository.Record(NewAssessment("morphology", null, null, "Correctness", [partial, complete]));
        var rows = repository.Get("morphology").Words!;
        Assert.Equal(2, rows.Count);
        Assert.True(rows[0].Morphology!.Capped);
        Assert.True(rows[0].Morphology!.TimedOut);
        Assert.Equal("incomplete", rows[0].Correctness!.Status);
        Assert.Equal(1, rows[0].Correctness!.Matched);
        Assert.Equal(complete.Morphology!.Analyses[0].Morphs[0], rows[0].Morphology!.Analyses[0].Morphs[0]);
        Assert.Equal("covered", rows[1].Correctness!.Status);
        Assert.Single(rows[1].Correctness!.Expectations);
    }

    [Fact]
    public void ReadBackRejectsNoncontiguousStoredCaseOrdinals()
    {
        var repository = NewRepository("ordinals.fwdata", out var database);
        using var owned = database;
        repository.Record(NewAssessment("ordinal-test", null, null, "Correctness"));
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE AssessedWords SET OrdinalIndex = OrdinalIndex + 5 WHERE AssessmentId = 'ordinal-test'";
        command.ExecuteNonQuery();

        Assert.Throws<InvalidDataException>(() => repository.Get("ordinal-test"));
    }

    [Fact]
    public void FindLatestBaselineAssessmentDoesNotReadUnmatchedHistory()
    {
        var repository = NewRepository("matching-baseline.fwdata", out var database);
        using var ownedDatabase = database;
        var selected = Selection.Create("selection-1", ["bo", "za"]);
        var matching = NewAssessment("matching", null, null, "ParseTime",
            [new AssessedWord("bo", "analysed", []), new AssessedWord("za", "no-analysis", [])],
            "2026-09-24T10:00:00.0000000+00:00") with { Subject = ((AssessmentSubject.Words)NewAssessment("matching", null, null, "ParseTime",
            [new AssessedWord("bo", "analysed", []), new AssessedWord("za", "no-analysis", [])],
            "2026-09-24T10:00:00.0000000+00:00").Subject) with { Selection = selected } };
        var unrelated = NewAssessment("unrelated", null, null, "ParseTime",
            [new AssessedWord("bo", "analysed", []), new AssessedWord("za", "no-analysis", [])],
            "2026-09-24T11:00:00.0000000+00:00") with { BaselineToken = "{\"baseline\":false}" };
        repository.RecordBatch([matching, unrelated]);
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE AssessedWords SET OrdinalIndex = OrdinalIndex + 5 " +
                "WHERE AssessmentId = 'unrelated';";
            command.ExecuteNonQuery();
        }

        var result = repository.FindLatestBaselineAssessment(
            matching.Kind, matching.BaselineToken, selected.Sha256, selected.Words);

        Assert.NotNull(result);
        Assert.Equal("matching", result.AssessmentId);
        Assert.Equal(["bo", "za"], result.Words!.Select(word => word.Word));
    }

    [Fact]
    public void ReadBaselineAssessmentWordsLoadsOnlyNamedAssessmentFormsWithoutTimings()
    {
        var repository = NewRepository("selected-assessment-words.fwdata", out var database);
        using var ownedDatabase = database;
        var selected = NewAssessment("selected", null, null, "ParseTime",
            [new AssessedWord("bo", "analysed", []), new AssessedWord("za", "no-analysis", [])]);
        var unrelated = NewAssessment("unrelated", null, null, "ParseTime",
            [new AssessedWord("bo", "analysed", []), new AssessedWord("za", "no-analysis", [])]);
        repository.RecordBatch([selected, unrelated]);
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE AssessedWords SET OrdinalIndex = OrdinalIndex + 5 " +
                "WHERE AssessmentId = 'unrelated';";
            command.ExecuteNonQuery();
        }

        var result = repository.ReadBaselineAssessmentWords("ParseTime", selected.BaselineToken,
            ["selected"], ["za"]);

        var assessment = Assert.Single(result);
        Assert.Equal("selected", assessment.AssessmentId);
        Assert.Equal(["za"], assessment.Words!.Select(word => word.Word));
        Assert.Empty(assessment.ObjectTimings);
    }

    [Fact]
    public void RecordsAnAssessmentWithWordsAndAnalysesAndReadsItBackById()
    {
        var repository = NewRepository("record.fwdata", out _);
        var assessment = NewAssessment("a1", proposalId: null, proposalIntentDigest: null, kind: "parse");

        repository.Record(assessment);
        var result = repository.Get("a1");

        Assert.Equal("a1", result.AssessmentId);
        Assert.Null(result.ProposalId);
        Assert.Null(result.ProposalIntentDigest);
        Assert.Equal("pangloss", result.Assessor);
        Assert.Equal("parse", result.Kind);
        Assert.Equal("{\"words\":\"all\"}", result.ScopeJson);
        Assert.Equal("sha256:scope", result.ScopeDigest);
        Assert.Equal("whitespace", result.TokeniserName);
        Assert.Equal("1", result.TokeniserVersion);
        Assert.Equal("{\"baseline\":true}", result.BaselineToken);
        Assert.NotNull(result.Words);
        Assert.Equal(2, result.Words!.Count);
        Assert.Equal("bo", result.Words[0].Word);
        Assert.Single(result.Words[0].Analyses);
        Assert.Equal("guid-root", result.Words[0].Analyses[0].MorphemeGuids[0]);
        Assert.Empty(result.Words[1].Analyses);
    }

    [Fact]
    public void GetReturnsEveryFieldWithWordAndAnalysisOrderIntact_WhichIsWhatAnalysesReadsBack()
    {
        var repository = NewRepository("round-trip.fwdata", out var database);
        using var _ = database;
        var words = new AssessedWord[]
        {
            new("mbali", "Analysed",
            [
                new ParsedAnalysis("cat-d1", ["m1", "m2"], 0, "d1"),
                new ParsedAnalysis("cat-d2", ["m3"], 0, "d2"),
            ]),
            new("nyumba", "NoAnalysis", []),
        };
        repository.Record(NewAssessment("assessment/round-trip", null, null, "ParseTime", words));

        var stored = repository.Get("assessment/round-trip").ToStored();

        Assert.Equal("sha256:outcome", stored.Report.OutcomeDigest);
        Assert.Equal("sha256:grammar", stored.Report.GrammarSourceSha256);
        Assert.Equal("selection-1", stored.Selection.Name);
        Assert.Equal(["bo", "za"], stored.Selection.Words);
        Assert.Equal(2, stored.Report.Words.Count);
        Assert.Equal("mbali", stored.Report.Words[0].Word);
        // Order within a word's analyses, and morpheme order within an analysis, must both survive the store.
        Assert.Equal(["m1", "m2"], stored.Report.Words[0].Analyses[0].MorphemeGuids);
        Assert.Equal("d2", stored.Report.Words[0].Analyses[1].IdentityDigest);
        Assert.Equal("nyumba", stored.Report.Words[1].Word);
        Assert.Empty(stored.Report.Words[1].Analyses);
    }

    [Fact]
    public void ProjectStandingAndTextOccurrenceCountSurviveReadBack()
    {
        var repository = NewRepository("standing.fwdata", out var database);
        using var owned = database;
        var word = new AssessedWord("motifa", "analysed", [])
        {
            ProjectStanding = SIL.Motif.Contract.Responses.ProjectStanding.Approved,
            OccurrenceCount = 7,
        };

        repository.Record(NewAssessment("standing", null, null, "ParseTime", [word]));

        var stored = Assert.Single(repository.Get("standing").Words!);
        Assert.Equal(SIL.Motif.Contract.Responses.ProjectStanding.Approved, stored.ProjectStanding);
        Assert.Equal(7, stored.OccurrenceCount);
    }

    [Fact]
    public void BatchSignaturesSurviveStorageWithoutChangingCompletionOrInventingAnalyses()
    {
        var repository = NewRepository("batch-signatures.fwdata", out var database);
        using var ownedDatabase = database;
        var batch = new BatchAnalysis(BatchTsvParser.Parse(
            "0\tpartial\t15\tCAP\troot+noun\n1\tcapped-empty\t16\tCAP\t-\n" +
            "2\ttimed\t1000\tTIMEOUT\troot+verb\n3\tfinished\t20\tok\troot+noun\n" +
            "4\tabsent\t21\tok\t-\n"), 1000, "grammar.fwdata", []);
        var (words, cachePath, cacheDigest) = AssessmentMaterial.From(new AssessmentRaw.Batch(batch));
        Assert.Null(cachePath);
        Assert.Null(cacheDigest);
        repository.Record(NewAssessment("batch-signatures", null, null, "ParseTime", words));

        var stored = repository.Get("batch-signatures").Words!;
        Assert.Equal(new[] { "partial", "capped-empty", "timed", "finished", "absent" }, stored.Select(word => word.Word));
        Assert.Equal(new[] { "capped", "capped", "timed-out", "analysed", "no-analysis" }, stored.Select(word => word.Outcome));
        Assert.Equal(new[] { "root+noun", "-", "root+verb", "root+noun", "-" }, stored.Select(word => word.RawSignature));
        Assert.Equal(new int?[] { 15, 16, 1000, 20, 21 }, stored.Select(word => word.ElapsedMs));
        Assert.All(stored, word => Assert.Empty(word.Analyses));
    }

    [Fact]
    public void ObjectTimingsKeepIdentityDirectionAndNullableMeasurementsAcrossReadBack()
    {
        var repository = NewRepository("object-timing-identity.fwdata", out var database);
        using var ownedDatabase = database;
        var timings = new AssessmentObjectTiming[]
        {
            new("lex_entry", "entry-a", "authored", "analysis", "Shared label", "word", 4, null, 180_000),
            new("lex_entry", "entry-b", "authored", "analysis", "Shared label", "word", 5, null, 220_000),
            new("lex_entry", "entry-a", "authored", "synthesis", "Shared label", "word", 6, null, null),
        };
        repository.Record(NewAssessment("object-timing-identity", null, null, "ParseTime",
            [new AssessedWord("word", "analysed", []) { ElapsedNs = 725_001 }]) with
        { Material = NewAssessment("object-timing-identity", null, null, "ParseTime",
            [new AssessedWord("word", "analysed", []) { ElapsedNs = 725_001 }]).RequireWordMaterial() with { ObjectTimings = timings } });

        var stored = repository.Get("object-timing-identity");

        Assert.Equal(725_001L, Assert.Single(stored.Words!).ElapsedNs);
        Assert.Equal(3, stored.ObjectTimings.Count);
        Assert.Equal(2, stored.ObjectTimings.Select(row => row.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.All(stored.ObjectTimings, row => Assert.Equal("Shared label", row.Object));
        var unsupported = Assert.Single(stored.ObjectTimings, row => row.Direction == "synthesis");
        Assert.Null(unsupported.Passes);
        Assert.Null(unsupported.ElapsedNs);
    }

    [Fact]
    public void TimingTotalsAndDetailAvailabilitySurviveReadBack()
    {
        var repository = NewRepository("timing-evidence.fwdata", out var database);
        using var ownedDatabase = database;
        var record = NewAssessment("timing-evidence", null, null, "ParseTime");
        var evidence = new AssessmentTimingEvidence(
            [new PanGlossObjectTimingAggregate("morph_rule", "rule", "authored", "analysis", "Rule", 3, 12, 7, 900, 500)],
            1, false, 4);
        repository.Record(record with { Material = record.RequireWordMaterial() with { TimingEvidence = evidence } });

        var stored = repository.Get("timing-evidence").TimingEvidence!;
        Assert.False(stored.DetailComplete);
        Assert.Equal(1, stored.DetailWordCount);
        Assert.Equal(4, stored.TotalObjectTimingRows);
        Assert.Equal(evidence.Aggregates, stored.Aggregates);
    }

    [Fact]
    public void HeldOutAssessmentAddsLessThanTenMegabytesToTheCheckpointedStore()
    {
        var repository = NewRepository("held-out-size.fwdata", out var database);
        using var ownedDatabase = database;
        static long Checkpoint(MotifDatabase store)
        {
            using var connection = store.OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal(0L, reader.GetInt64(0));
            return new FileInfo(store.FullPath).Length;
        }

        var baselineBytes = Checkpoint(database);
        var words = Enumerable.Range(0, 7206).Select(index =>
        {
            var word = SIL.Motif.Tests.TestFixtures.CorrectnessFixture.Word($"word-{index:D5}", true);
            var analyses = Enumerable.Range(0, 6).Select(analysis => new ParseAnalysis(
                Enumerable.Range(0, 5).Select(morph => new ParseMorph(
                    $"{index:D8}-1111-1111-1111-{morph:D12}",
                    $"22222222-2222-2222-2222-{analysis:D12}", null, null)).ToArray())).ToArray();
            return word with { Morphology = word.Morphology! with { Index = index, Analyses = analyses } };
        }).ToArray();
        Assert.True(JsonSerializer.SerializeToUtf8Bytes(words[0].Morphology).Length > 3000);
        var cachePath = Path.Combine(_root, "synthetic-statistics.sqlite");
        WriteSyntheticStatistics(cachePath, words);
        var statistics = PanGlossBatchStatisticsReader.ReadBounded(cachePath, words.Select(word => word.Word).ToArray(), 2000);
        Assert.Equal(720600, statistics.TotalObjectTimingRows);
        Assert.Equal(2000, statistics.DetailWordCount);
        Assert.Equal(200000, statistics.ObjectTimings.Count);
        var timings = statistics.ObjectTimings.Select(row => new AssessmentObjectTiming(row.Kind, row.Key,
            row.IdentityQuality, row.Direction, row.Object, row.Word, row.Attempts, row.Passes, row.ElapsedNs)).ToArray();
        var selection = Selection.Create("held-out", words.Select(word => word.Word).ToArray());
        var first = NewAssessment("held-out-parse", null, null, "ParseTime", words) with
        {
            Subject = new AssessmentSubject.Words(selection, "whitespace", "1"),
        };
        first = first with { Material = first.RequireWordMaterial() with
        {
            ObjectTimings = timings,
            TimingEvidence = new AssessmentTimingEvidence(statistics.Aggregates, statistics.DetailWordCount,
                statistics.DetailComplete, statistics.TotalObjectTimingRows)
            { AttributedNs = statistics.AttributedNs, UnattributedNs = statistics.UnattributedNs, OverrunNs = statistics.OverrunNs },
        } };
        var second = first with
        {
            AssessmentId = "held-out-correctness", Kind = "Correctness",
            Material = first.RequireWordMaterial() with { ObjectTimings = [], TimingEvidence = null },
        };
        repository.RecordBatch([first, second]);

        var afterBytes = Checkpoint(database);
        var addedBytes = afterBytes - baselineBytes;
        Console.WriteLine($"Held-out Assessment file growth: {addedBytes} bytes (before {baselineBytes}, after {afterBytes})");
        Assert.True(addedBytes <= 10_000_000, $"Held-out Assessment added {addedBytes} bytes.");
    }

    private static void WriteSyntheticStatistics(string path, IReadOnlyList<AssessedWord> words)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var setup = connection.CreateCommand();
        setup.Transaction = transaction;
        setup.CommandText = """
            CREATE TABLE cache_identity (cache_id INTEGER PRIMARY KEY, schema_version INTEGER NOT NULL);
            CREATE TABLE word (word_id INTEGER PRIMARY KEY, form TEXT NOT NULL, elapsed_ns INTEGER NOT NULL,
                attempts INTEGER NOT NULL, passes INTEGER NOT NULL);
            CREATE TABLE object (object_id INTEGER PRIMARY KEY, key TEXT NOT NULL, kind TEXT NOT NULL,
                label TEXT NOT NULL, identity_quality TEXT NOT NULL);
            CREATE TABLE fact (word_id INTEGER NOT NULL, object_id INTEGER NOT NULL, direction TEXT NOT NULL,
                attempts INTEGER NOT NULL, self_time_ns INTEGER);
            INSERT INTO cache_identity VALUES (1, $version);
            """;
        setup.Parameters.AddWithValue("$version", PanGlossInterfaceVersions.StatsCacheVersion);
        setup.ExecuteNonQuery();
        using var insertObject = connection.CreateCommand();
        insertObject.Transaction = transaction;
        insertObject.CommandText = "INSERT INTO object VALUES ($id, $key, 'morph_rule', $label, 'authored')";
        var objectId = insertObject.Parameters.Add("$id", SqliteType.Integer);
        var objectKey = insertObject.Parameters.Add("$key", SqliteType.Text);
        var objectLabel = insertObject.Parameters.Add("$label", SqliteType.Text);
        for (var rule = 0; rule < 100; rule++)
        {
            objectId.Value = rule + 1;
            objectKey.Value = $"rule-{rule:D4}";
            objectLabel.Value = $"Rule {rule}";
            insertObject.ExecuteNonQuery();
        }
        using var insertWord = connection.CreateCommand();
        insertWord.Transaction = transaction;
        insertWord.CommandText = "INSERT INTO word VALUES ($id, $form, $elapsed, 10, 1)";
        var wordId = insertWord.Parameters.Add("$id", SqliteType.Integer);
        var wordForm = insertWord.Parameters.Add("$form", SqliteType.Text);
        var wordElapsed = insertWord.Parameters.Add("$elapsed", SqliteType.Integer);
        using var insertFact = connection.CreateCommand();
        insertFact.Transaction = transaction;
        insertFact.CommandText = "INSERT INTO fact VALUES ($word, $object, 'analysis', $attempts, $elapsed)";
        var factWord = insertFact.Parameters.Add("$word", SqliteType.Integer);
        var factObject = insertFact.Parameters.Add("$object", SqliteType.Integer);
        var factAttempts = insertFact.Parameters.Add("$attempts", SqliteType.Integer);
        var factElapsed = insertFact.Parameters.Add("$elapsed", SqliteType.Integer);
        for (var index = 0; index < words.Count; index++)
        {
            wordId.Value = index + 1;
            wordForm.Value = words[index].Word;
            wordElapsed.Value = index * 1000L + 1;
            insertWord.ExecuteNonQuery();
            factWord.Value = index + 1;
            for (var rule = 0; rule < 100; rule++)
            {
                factObject.Value = rule + 1;
                factAttempts.Value = (index + rule) % 37;
                factElapsed.Value = (index * 7919L + rule * 97L) % 1_000_000;
                insertFact.ExecuteNonQuery();
            }
        }
        transaction.Commit();
    }

    [Fact]
    public void MissingRawSignaturesRemainAbsentAlongsideAuthoritativeAnalyses()
    {
        var repository = NewRepository("absent-signature.fwdata", out var database);
        using var ownedDatabase = database;
        repository.Record(NewAssessment("absent-signature", null, null, "Correctness"));

        var words = repository.Get("absent-signature").Words!;
        Assert.All(words, word => Assert.Null(word.RawSignature));
        Assert.Single(words[0].Analyses);
    }

    [Fact]
    public void SharedInvocationEvidenceRoundTripsOnceWithAbsentParserMetadata()
    {
        var repository = NewRepository("invocation.fwdata", out var database);
        using var ownedDatabase = database;
        var evidence = Invocation("shared-run");
        var first = NewAssessment("timing", null, null, "ParseTime") with
        { OutcomeDigest = null,
                SemanticDigest = null,
                ModelFingerprint = null,
                Pipeline = null,
                DiagnosticCount = null,
                Material = NewAssessment("timing", null, null, "ParseTime").RequireWordMaterial() with { Invocation = evidence } };
        repository.RecordBatch([first, first with { AssessmentId = "stats", Kind = "ObjectTiming" }]);

        var stored = repository.Get("timing");
        Assert.Equal(evidence, stored.Invocation);
        Assert.Equal(evidence, repository.Get("stats").Invocation);
        Assert.Null(stored.OutcomeDigest);
        Assert.Null(stored.SemanticDigest);
        Assert.Null(stored.ModelFingerprint);
        Assert.Null(stored.Pipeline);
        Assert.Null(stored.DiagnosticCount);
        Assert.Throws<InvalidOperationException>(() => stored.ToStored());
        using var connection = database.OpenConnection();
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM AssessmentInvocations;";
        Assert.Equal(1L, count.ExecuteScalar());
    }

    [Fact]
    public void ACollisionOnTheSecondBatchRowRollsBackTheFirstRowAndInvocation()
    {
        var repository = NewRepository("atomic-batch.fwdata", out var database);
        using var ownedDatabase = database;
        var first = NewAssessment("duplicate", null, null, "ParseTime") with { Material = NewAssessment("duplicate", null, null, "ParseTime").RequireWordMaterial() with { Invocation = Invocation("rollback-run") } };
        Assert.Throws<SqliteException>(() => repository.RecordBatch([first, first]));
        Assert.Throws<KeyNotFoundException>(() => repository.Get("duplicate"));
        using var connection = database.OpenConnection();
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM AssessmentInvocations;";
        Assert.Equal(0L, count.ExecuteScalar());
    }

    [Fact]
    public void AnInvocationIdCannotBeReusedForDifferentEvidence()
    {
        var repository = NewRepository("conflicting-evidence.fwdata", out var database);
        using var ownedDatabase = database;
        var first = NewAssessment("original", null, null, "ParseTime") with { Material = NewAssessment("original", null, null, "ParseTime").RequireWordMaterial() with { Invocation = Invocation("same-run") } };
        repository.Record(first);
        var conflicting = first with
        { AssessmentId = "conflicting",
                Material = first.RequireWordMaterial() with { Invocation = first.Invocation! with { PerWordStepLimit = 7 } } };
        Assert.Throws<InvalidOperationException>(() => repository.RecordBatch([conflicting]));
        Assert.Throws<KeyNotFoundException>(() => repository.Get("conflicting"));
        Assert.Equal(first.Invocation, repository.Get("original").Invocation);
    }

    [Fact]
    public void GrammarFindingsSurviveTheInvocationSharedByEveryKindOfOneRun()
    {
        var repository = NewRepository("findings.fwdata", out var database);
        using var ownedDatabase = database;
        var evidence = Invocation("findings-run") with
        {
            GrammarWarnings = string.Join('\n',
                "warning: no boundary marker representation '+' found",
                "warning: allomorph \"a3547f67\": cannot segment \"d+\"; skipped"),
        };
        var first = NewAssessment("timing", null, null, "ParseTime") with
        { OutcomeDigest = null,
                SemanticDigest = null,
                ModelFingerprint = null,
                Pipeline = null,
                DiagnosticCount = null,
                Material = NewAssessment("timing", null, null, "ParseTime").RequireWordMaterial() with { Invocation = evidence } };

        repository.RecordBatch([first, first with { AssessmentId = "stats", Kind = "ObjectTiming" }]);

        var stored = repository.Get("timing").Invocation!;
        Assert.Equal(evidence, stored);
        Assert.Equal(evidence, repository.Get("stats").Invocation);
        Assert.Equal(2, stored.GrammarWarningLines.Count);
        Assert.Equal("warning: no boundary marker representation '+' found", stored.GrammarWarningLines[0]);
        Assert.Contains("cannot segment", stored.GrammarWarningLines[1]);
    }

    [Fact]
    public void AnInvocationThatReportedNothingReadsBackAsNoFindingsRatherThanOneBlankOne()
    {
        var repository = NewRepository("quiet.fwdata", out var database);
        using var ownedDatabase = database;
        repository.Record(NewAssessment("quiet", null, null, "ParseTime") with
        { OutcomeDigest = null,
                SemanticDigest = null,
                ModelFingerprint = null,
                Pipeline = null,
                DiagnosticCount = null,
                Material = NewAssessment("quiet", null, null, "ParseTime").RequireWordMaterial() with { Invocation = Invocation("quiet-run") } });

        var stored = repository.Get("quiet").Invocation!;

        Assert.Null(stored.GrammarWarnings);
        Assert.Empty(stored.GrammarWarningLines);
    }

    private static BatchInvocationEvidence Invocation(string id) => new(
        id, "source.fwdata", "sha256:source", "sha256:executable", "words.txt", "sha256:words",
        "rows.tsv", "sha256:rows", "stderr.txt", "sha256:stderr", 1000, 200000, 1, true);

    [Fact]
    public void MaterialCopiesInvocationEvidenceWithoutFillingMissingParserMetadata()
    {
        var evidence = Invocation("material-run");
        var produced = new ProducedAssessment(AssessmentKind.ParseTime, "sha256:source",
            null, null, null, null, null, new AssessmentRaw.WordMeasurements([])) { Invocation = evidence };
        var record = AssessmentMaterial.ToWordRecord(produced, "material", null, null, "pangloss", "{}",
            "sha256:scope", "baseline", new AssessmentSubject.Words(Selection.Create("selection", []), "whitespace", "1"));
        Assert.Equal(evidence, record.Invocation);
        Assert.Null(record.OutcomeDigest);
        Assert.Null(record.SemanticDigest);
        Assert.Null(record.ModelFingerprint);
        Assert.Null(record.Pipeline);
        Assert.Null(record.DiagnosticCount);
    }

    [Fact]
    public void GetThrowsForAnUnrecordedAssessment()
    {
        var repository = NewRepository("missing.fwdata", out _);
        Assert.Throws<KeyNotFoundException>(() => repository.Get("nope"));
    }

    [Fact]
    public void ListsByProposalAndByKind()
    {
        var repository = NewRepository("lists.fwdata", out var database);
        var proposal1 = CanonicalId.Mint("proposal/");
        var proposal2 = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal1.Value);
        SeedProposal(database, proposal2.Value);

        repository.Record(NewAssessment("parse-1", proposal1, "sha256:intent1", "parse"));
        repository.Record(NewAssessment("parse-2", proposal2, "sha256:intent2", "parse"));
        repository.Record(NewAssessment("size-1", proposal1, "sha256:intent1", "engine-size"));

        var byProposal = repository.ListByProposal(proposal1);
        Assert.Equal(2, byProposal.Count);
        Assert.All(byProposal, record => Assert.Equal(proposal1, record.ProposalId));
        Assert.All(byProposal, record => Assert.Null(record.Words));

        var byKind = repository.ListByKind("parse");
        Assert.Equal(2, byKind.Count);
        Assert.All(byKind, record => Assert.Equal("parse", record.Kind));
        Assert.Empty(repository.ListByKind("difference"));
    }

    [Fact]
    public void ListBaselineAssessmentsReturnsOnlyProposalFreeRowsOfOneKindWithWordsPopulated_NewestLast()
    {
        var repository = NewRepository("baseline-runs.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);

        repository.Record(NewAssessment("baseline-older", null, null, "ParseTime", savedUtc: "2020-01-01T00:00:00Z"));
        repository.Record(NewAssessment("baseline-newest", null, null, "ParseTime", savedUtc: "2020-01-02T00:00:00Z"));
        repository.Record(NewAssessment("trial-run", proposal, "sha256:intent", "ParseTime"));
        repository.Record(NewAssessment("baseline-other-kind", null, null, "engine-size"));

        var runs = repository.ListBaselineAssessments("ParseTime");

        Assert.Equal(["baseline-older", "baseline-newest"], runs.Select(r => r.AssessmentId).ToArray());
        Assert.All(runs, run => Assert.Null(run.ProposalId));
        Assert.All(runs, run => Assert.NotNull(run.Words));
        Assert.Equal("bo", runs[^1].Words![0].Word);
    }

    [Fact]
    public void PromotionMovesThePointerAndReadingCurrentReturnsWhatWasPromoted()
    {
        var repository = NewRepository("promote.fwdata", out _);
        Assert.Null(repository.GetCurrent());
        repository.Record(NewAssessment("candidate-1", null, null, "parse"));
        repository.Record(NewAssessment("candidate-2", null, null, "parse"));

        repository.PromoteToCurrent("candidate-1");
        Assert.Equal("candidate-1", repository.GetCurrent()!.AssessmentId);

        repository.PromoteToCurrent("candidate-2");
        Assert.Equal("candidate-2", repository.GetCurrent()!.AssessmentId);
    }

    [Fact]
    public void PromoteToCurrentThrowsForAnUnrecordedAssessment()
    {
        var repository = NewRepository("promote-missing.fwdata", out _);
        Assert.Throws<KeyNotFoundException>(() => repository.PromoteToCurrent("nope"));
    }

    [Fact]
    public void RecordRollsBackEverythingOnAGenuinePrimaryKeyCollision()
    {
        var repository = NewRepository("atomic.fwdata", out var database);
        var original = NewAssessment("collide", null, null, "parse");
        repository.Record(original);

        var colliding = NewAssessment("collide", null, null, "parse", words:
        [
            new AssessedWord("different-word", "complete", [])
        ]);

        Assert.ThrowsAny<SqliteException>(() => repository.Record(colliding));

        // The original row survives untouched, and none of the second attempt's words landed anywhere.
        using var connection = database.OpenConnection();
        using (var assessmentCount = connection.CreateCommand())
        {
            assessmentCount.CommandText = "SELECT COUNT(*) FROM Assessments WHERE AssessmentId = 'collide';";
            Assert.Equal(1L, assessmentCount.ExecuteScalar());
        }
        using (var wordCount = connection.CreateCommand())
        {
            wordCount.CommandText = "SELECT COUNT(*) FROM AssessedWords WHERE AssessmentId = 'collide';";
            Assert.Equal(2L, wordCount.ExecuteScalar());
        }
        using (var differentWord = connection.CreateCommand())
        {
            differentWord.CommandText = "SELECT COUNT(*) FROM AssessedWords WHERE Word = 'different-word';";
            Assert.Equal(0L, differentWord.ExecuteScalar());
        }
        var reread = repository.Get("collide");
        Assert.Equal(2, reread.Words!.Count);
        Assert.Equal("bo", reread.Words[0].Word);
    }

    /// <summary>
    /// The trap ADR 0042's Trial amendment names: promotion must happen before the sweep, and the promoted
    /// Assessment must survive by identity, not by hoping the sweep never learns to run first.
    /// </summary>
    [Fact]
    public void SweepDeletesTheProposalsOtherAssessments_ButThePromotedOneSurvivesByIdentity()
    {
        var repository = NewRepository("sweep-trap.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);

        repository.Record(NewAssessment("promoted", proposal, "sha256:intent", "Correctness"));
        repository.Record(NewAssessment("scratch-1", proposal, "sha256:intent", "ParseTime"));
        repository.Record(NewAssessment("scratch-2", proposal, "sha256:older-intent", "Correctness"));

        // Promotion happens first, exactly as apply must sequence it.
        repository.PromoteToCurrent("promoted");
        repository.DeleteByProposal(proposal, exceptAssessmentId: "promoted");

        // Asserted after the sweep has already run — the identity exclusion, not the ordering, is what is pinned.
        Assert.Equal("promoted", repository.Get("promoted").AssessmentId);
        Assert.Equal("promoted", repository.GetCurrent()!.AssessmentId);
        Assert.Throws<KeyNotFoundException>(() => repository.Get("scratch-1"));
        Assert.Throws<KeyNotFoundException>(() => repository.Get("scratch-2"));
        Assert.DoesNotContain(repository.ListByProposal(proposal), record => record.AssessmentId != "promoted");
    }

    [Fact]
    public void SweepWithNoExceptionDeletesEveryAssessmentOnTheProposal()
    {
        var repository = NewRepository("sweep-all.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);
        repository.Record(NewAssessment("only-one", proposal, "sha256:intent", "Correctness"));

        repository.DeleteByProposal(proposal, exceptAssessmentId: null);

        Assert.Empty(repository.ListByProposal(proposal));
    }

    [Fact]
    public void SweepReclaimsOrphanedPayloadsButKeepsSharedPayloads()
    {
        var repository = NewRepository("sweep-payloads.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);
        var shared = SIL.Motif.Tests.TestFixtures.CorrectnessFixture.Word("shared", true);
        var removed = SIL.Motif.Tests.TestFixtures.CorrectnessFixture.Word("removed", true);
        repository.Record(NewAssessment("removed", proposal, "sha256:intent", "Correctness", [shared, removed]));
        repository.Record(NewAssessment("kept", null, null, "Correctness", [shared]));
        var before = int.Parse(Scalar(database, "SELECT CAST(COUNT(*) AS TEXT) FROM AssessmentWordPayloads;"));

        repository.DeleteByProposal(proposal, exceptAssessmentId: null);

        var after = int.Parse(Scalar(database, "SELECT CAST(COUNT(*) AS TEXT) FROM AssessmentWordPayloads;"));
        Assert.True(after > 0 && after < before);
        Assert.Equal("shared", Assert.Single(repository.Get("kept").Words!).Morphology!.Word);
    }

    [Fact]
    public void MissingReferencedWordPayloadIsNotReadAsEmptyEvidence()
    {
        var repository = NewRepository("missing-payload.fwdata", out var database);
        var word = SIL.Motif.Tests.TestFixtures.CorrectnessFixture.Word("word", true);
        repository.Record(NewAssessment("assessment", null, null, "Correctness", [word]));
        using (var connection = database.OpenConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA foreign_keys = OFF; DELETE FROM AssessmentWordPayloads;";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => repository.Get("assessment"));
    }

    [Fact]
    public void SweepLeavesAnotherProposalsAssessmentsUntouched()
    {
        var repository = NewRepository("sweep-other.fwdata", out var database);
        var swept = CanonicalId.Mint("proposal/");
        var untouched = CanonicalId.Mint("proposal/");
        SeedProposal(database, swept.Value);
        SeedProposal(database, untouched.Value);
        repository.Record(NewAssessment("swept-1", swept, "sha256:intent", "Correctness"));
        repository.Record(NewAssessment("kept-1", untouched, "sha256:intent", "Correctness"));

        repository.DeleteByProposal(swept, exceptAssessmentId: null);

        Assert.Empty(repository.ListByProposal(swept));
        Assert.Equal("kept-1", repository.Get("kept-1").AssessmentId);
    }

    [Fact]
    public void SweepKeepsAnAssessmentAReportCitesAndTheReportsBinding()
    {
        var repository = NewRepository("sweep-report.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);
        repository.Record(NewAssessment("reported", proposal, "sha256:intent", "Correctness"));
        repository.Record(NewAssessment("loose", proposal, "sha256:intent", "ParseTime"));
        Execute(database, "INSERT INTO Reports (ReportId, ProposalId, AssessmentId, ReportJson, CreatedUtc) " +
            "VALUES ('report/1', $proposal, 'reported', '{}', '2026-10-13T00:00:00Z');", proposal.Value);

        repository.DeleteByProposal(proposal, exceptAssessmentId: null);

        Assert.Equal("reported", repository.Get("reported").AssessmentId);
        Assert.Equal("reported", Scalar(database, "SELECT AssessmentId FROM Reports WHERE ReportId = 'report/1';"));
        Assert.Throws<KeyNotFoundException>(() => repository.Get("loose"));
    }

    [Fact]
    public void SweepKeepsBothAssessmentsCitedByASavedDifferenceAndItsReplacementLineage()
    {
        var repository = NewRepository("sweep-difference.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);
        repository.Record(NewAssessment("before", proposal, "sha256:intent", "Correctness"));
        repository.Record(NewAssessment("root", proposal, "sha256:intent", "Correctness"));
        repository.Record(NewAssessment("middle", proposal, "sha256:intent", "Correctness"));
        repository.Record(NewAssessment("after", proposal, "sha256:intent", "Correctness"));
        repository.Record(NewAssessment("uncited", proposal, "sha256:intent", "ParseTime"));
        Execute(database, "UPDATE Assessments SET ReplacesAssessmentId = 'root' WHERE AssessmentId = 'middle';", proposal.Value);
        Execute(database, "UPDATE Assessments SET ReplacesAssessmentId = 'middle' WHERE AssessmentId = 'after';", proposal.Value);

        var differences = new ProposalDifferenceRepository(database);
        differences.Publish(new NewProposalDifference(
            DifferenceSummary("difference/1", proposal.Value, "before", "after"), []));

        repository.DeleteByProposal(proposal, exceptAssessmentId: null);

        Assert.Equal("before", repository.Get("before").AssessmentId);
        Assert.Equal("root", repository.Get("root").AssessmentId);
        Assert.Equal("middle", repository.Get("middle").AssessmentId);
        Assert.Equal("after", repository.Get("after").AssessmentId);
        Assert.Throws<KeyNotFoundException>(() => repository.Get("uncited"));
        Assert.Equal(("before", "after"),
            (differences.GetSummary("difference/1")!.BeforeAssessmentId,
                differences.GetSummary("difference/1")!.AfterAssessmentId));
    }

    [Fact]
    public void SweepKeepsAssessmentIdsInEveryFrozenParsimonyCitationSlot()
    {
        var repository = NewRepository("sweep-parsimony.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);
        repository.Record(NewAssessment("report/top-level", proposal, "sha256:intent", "Correctness"));
        repository.Record(NewAssessment("report/inputs", proposal, "sha256:intent", "Correctness"));
        repository.Record(NewAssessment("report/evidence", proposal, "sha256:intent", "Correctness"));
        repository.Record(NewAssessment("report/uncited", proposal, "sha256:intent", "ParseTime"));
        new ReportRepository(database).Save(ParsimonyReport(
            "report/parsimony", ["report/top-level"], ["report/inputs"], ["report/evidence"]));

        repository.DeleteByProposal(proposal, exceptAssessmentId: null);

        Assert.Equal("report/top-level", repository.Get("report/top-level").AssessmentId);
        Assert.Equal("report/inputs", repository.Get("report/inputs").AssessmentId);
        Assert.Equal("report/evidence", repository.Get("report/evidence").AssessmentId);
        Assert.Throws<KeyNotFoundException>(() => repository.Get("report/uncited"));
    }

    [Fact]
    public void SweepAcceptsAnOmittedOptionalParsimonyAssessmentIdsList()
    {
        var repository = NewRepository("sweep-parsimony-optional-assessment-ids.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);
        repository.Record(NewAssessment("report/uncited", proposal, "sha256:intent", "Correctness"));
        var report = ParsimonyReport("report/optional-assessment-ids", [], [], []);
        var reportJson = JsonNode.Parse(report.ReportJson)!.AsObject();
        Assert.True(reportJson.Remove("assessmentIds"));
        var omittedListReport = report with
        {
            ReportJson = reportJson.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        };
        var read = JsonSerializer.Deserialize<ParsimonyReportResponse>(omittedListReport.ReportJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(read);
        Assert.Empty(read.AssessmentIds);
        Assert.Empty(read.Inputs.AssessmentIds);
        Assert.NotNull(read.JoinQuality);
        new ReportRepository(database).Save(omittedListReport);

        repository.DeleteByProposal(proposal, exceptAssessmentId: null);

        Assert.Throws<KeyNotFoundException>(() => repository.Get("report/uncited"));
    }

    [Fact]
    public void SweepRefusesAnExplicitNullOptionalParsimonyAssessmentIdsList()
    {
        var repository = NewRepository("sweep-parsimony-null-assessment-ids.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);
        repository.Record(NewAssessment("report/unknown", proposal, "sha256:intent", "Correctness"));
        var report = ParsimonyReport("report/null-assessment-ids", [], [], []);
        var reportJson = JsonNode.Parse(report.ReportJson)!.AsObject();
        reportJson["assessmentIds"] = null;
        new ReportRepository(database).Save(report with
        {
            ReportJson = reportJson.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)),
        });

        Assert.Throws<InvalidDataException>(() => repository.DeleteByProposal(proposal, exceptAssessmentId: null));

        Assert.Equal("report/unknown", repository.Get("report/unknown").AssessmentId);
    }

    [Fact]
    public void SweepRefusesMalformedFrozenParsimonyCitationsWithoutDeletingAssessments()
    {
        var repository = NewRepository("sweep-parsimony-malformed.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);
        repository.Record(NewAssessment("cited-or-unknown", proposal, "sha256:intent", "Correctness"));
        new ReportRepository(database).Save(new ReportRecord(
            "report/malformed", null, null, "{}", "{}", "parsimony"));

        Assert.Throws<InvalidDataException>(() => repository.DeleteByProposal(proposal, exceptAssessmentId: null));

        Assert.Equal("cited-or-unknown", repository.Get("cited-or-unknown").AssessmentId);
    }

    [Fact]
    public void SweepRefusesAnUnknownFrozenParsimonyCitationFieldWithoutDeletingAssessments()
    {
        var repository = NewRepository("sweep-parsimony-unknown-field.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);
        repository.Record(NewAssessment("cited-or-unknown", proposal, "sha256:intent", "Correctness"));
        var report = ParsimonyReport("report/unknown-field", ["cited-or-unknown"], ["cited-or-unknown"],
            ["cited-or-unknown"]);
        var reportJson = report.ReportJson[..^1] + ",\"futureReferences\":[\"cited-or-unknown\"]}";
        new ReportRepository(database).Save(report with { ReportJson = reportJson });

        Assert.Throws<InvalidDataException>(() => repository.DeleteByProposal(proposal, exceptAssessmentId: null));

        Assert.Equal("cited-or-unknown", repository.Get("cited-or-unknown").AssessmentId);
    }

    [Fact]
    public void SweepRefusesAnUnrecognizedReportKindWithoutDeletingAssessments()
    {
        var repository = NewRepository("sweep-unknown-report.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);
        repository.Record(NewAssessment("cited-or-unknown", proposal, "sha256:intent", "Correctness"));
        new ReportRepository(database).Save(new ReportRecord(
            "report/unknown", null, null, "{\"assessmentIds\":[\"cited-or-unknown\"]}", "{}", "future-report"));

        Assert.Throws<InvalidDataException>(() => repository.DeleteByProposal(proposal, exceptAssessmentId: null));

        Assert.Equal("cited-or-unknown", repository.Get("cited-or-unknown").AssessmentId);
    }

    [Fact]
    public void SweepKeepsAPinnedAssessment()
    {
        var repository = NewRepository("sweep-pinned.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);
        repository.Record(NewAssessment("pinned", proposal, "sha256:intent", "Correctness"));
        Execute(database, "INSERT INTO AssessmentPins (AssessmentId, PinnedBy, PinnedUtc) " +
            "VALUES ('pinned', 'linguist', '2026-10-13T00:00:00Z');", proposal.Value);

        repository.DeleteByProposal(proposal, exceptAssessmentId: null);

        Assert.Equal("pinned", repository.Get("pinned").AssessmentId);
    }

    [Fact]
    public void SweepKeepsARetainedMemberAndTakesALooseNeighbour()
    {
        var repository = NewRepository("sweep-retained.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);
        repository.Record(NewAssessment("retained", proposal, "sha256:intent", "Correctness"));
        repository.Record(NewAssessment("loose", proposal, "sha256:intent", "ParseTime"));
        // Membership is what the sweep reads; the aggregate's own rows are not needed to show it is honoured.
        Execute(database, "PRAGMA foreign_keys = OFF; INSERT INTO RetainedInvocationMembers (InvocationId, Kind, " +
            "AssessmentId) VALUES ('invocation/1', 'Correctness', 'retained'); PRAGMA foreign_keys = ON;", proposal.Value);

        repository.DeleteByProposal(proposal, exceptAssessmentId: null);

        Assert.Equal("retained", repository.Get("retained").AssessmentId);
        Assert.Equal("retained", Scalar(database, "SELECT AssessmentId FROM RetainedInvocationMembers;"));
        Assert.Throws<KeyNotFoundException>(() => repository.Get("loose"));
    }

    [Fact]
    public void SweepKeepsEveryRootASurvivingReplacementNeedsWithItsLineageUnchanged()
    {
        var repository = NewRepository("sweep-replaced.fwdata", out var database);
        var swept = CanonicalId.Mint("proposal/");
        var other = CanonicalId.Mint("proposal/");
        SeedProposal(database, swept.Value);
        SeedProposal(database, other.Value);
        repository.Record(NewAssessment("root", swept, "sha256:intent", "Correctness"));
        repository.Record(NewAssessment("middle", swept, "sha256:intent", "Correctness"));
        repository.Record(NewAssessment("survivor", other, "sha256:intent", "Correctness"));
        Execute(database, "UPDATE Assessments SET ReplacesAssessmentId = 'root' WHERE AssessmentId = 'middle';", swept.Value);
        Execute(database, "UPDATE Assessments SET ReplacesAssessmentId = 'middle' WHERE AssessmentId = 'survivor';", swept.Value);
        var before = Scalar(database, "SELECT group_concat(AssessmentId || '>' || IFNULL(ReplacesAssessmentId, '-'), ',') " +
            "FROM (SELECT * FROM Assessments ORDER BY AssessmentId);");

        repository.DeleteByProposal(swept, exceptAssessmentId: null);

        Assert.Equal(before, Scalar(database, "SELECT group_concat(AssessmentId || '>' || IFNULL(ReplacesAssessmentId, '-'), ',') " +
            "FROM (SELECT * FROM Assessments ORDER BY AssessmentId);"));
    }

    [Fact]
    public void ASweepThatFailsLateLeavesEveryRowItHadTouched()
    {
        var repository = NewRepository("sweep-rollback.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);
        repository.Record(NewAssessment("first", proposal, "sha256:intent", "Correctness"));
        repository.Record(NewAssessment("second", proposal, "sha256:intent", "ParseTime"));
        repository.Record(NewAssessment("uncited", proposal, "sha256:intent", "ParseTime"));
        var differences = new ProposalDifferenceRepository(database);
        differences.Publish(new NewProposalDifference(
            DifferenceSummary("difference/rollback", proposal.Value, "first", "second"), []));
        var words = Scalar(database, "SELECT CAST(COUNT(*) AS TEXT) FROM AssessedWords;");
        Execute(database, "CREATE TRIGGER FailLateSweep BEFORE DELETE ON Assessments " +
            "BEGIN SELECT RAISE(ABORT, 'injected late failure'); END;", proposal.Value);

        Assert.ThrowsAny<SqliteException>(() => repository.DeleteByProposal(proposal, exceptAssessmentId: null));

        Assert.Equal("first", repository.Get("first").AssessmentId);
        Assert.Equal("second", repository.Get("second").AssessmentId);
        Assert.Equal("uncited", repository.Get("uncited").AssessmentId);
        Assert.Equal(("first", "second"),
            (differences.GetSummary("difference/rollback")!.BeforeAssessmentId,
                differences.GetSummary("difference/rollback")!.AfterAssessmentId));
        Assert.Equal(words, Scalar(database, "SELECT CAST(COUNT(*) AS TEXT) FROM AssessedWords;"));
    }

    [Fact]
    public void DeletingAProposalWithAPinnedTrialLeavesCurrentEvidenceOnTheRealBaseline()
    {
        var repository = NewRepository("delete-pinned.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);
        Execute(database, "UPDATE Proposals SET Status = 'rejected' WHERE ProposalId = $proposal;", proposal.Value);
        repository.Record(NewAssessment("baseline", null, null, "ParseTime", savedUtc: "2026-10-13T00:00:00Z"));
        repository.Record(NewAssessment("trial", proposal, "sha256:intent", "ParseTime", savedUtc: "2026-10-13T01:00:00Z"));
        Execute(database, "INSERT INTO AssessmentPins (AssessmentId, PinnedBy, PinnedUtc) " +
            "VALUES ('trial', 'linguist', '2026-10-13T00:00:00Z');", proposal.Value);
        var selection = Selection.Create("selection-1", ["bo", "za"]);

        Assert.Throws<InvalidOperationException>(() => new ProposalRepository(database).DeleteArchived(proposal));

        Assert.Equal("baseline", repository.FindLatestBaselineAssessment(
            "ParseTime", "{\"baseline\":true}", selection.Sha256, selection.Words)!.AssessmentId);
        Assert.Equal(proposal, repository.Get("trial").ProposalId);
    }

    [Fact]
    public void DeletingAClosedProposalTakesItsOwnReportsAndUnneededAssessmentsWithIt()
    {
        var repository = NewRepository("delete-closed.fwdata", out var database);
        var proposal = CanonicalId.Mint("proposal/");
        SeedProposal(database, proposal.Value);
        Execute(database, "UPDATE Proposals SET Status = 'rejected' WHERE ProposalId = $proposal;", proposal.Value);
        repository.Record(NewAssessment("reported", proposal, "sha256:intent", "Correctness"));
        Execute(database, "INSERT INTO Reports (ReportId, ProposalId, AssessmentId, ReportJson, CreatedUtc) " +
            "VALUES ('report/1', $proposal, 'reported', '{}', '2026-10-13T00:00:00Z');", proposal.Value);

        new ProposalRepository(database).DeleteArchived(proposal);

        Assert.Equal("0", Scalar(database, "SELECT CAST(COUNT(*) AS TEXT) FROM Assessments;"));
        Assert.Equal("0", Scalar(database, "SELECT CAST(COUNT(*) AS TEXT) FROM Reports;"));
        Assert.Equal("0", Scalar(database, "SELECT CAST(COUNT(*) AS TEXT) FROM Proposals;"));
    }

    private static void Execute(MotifDatabase database, string sql, string proposalId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$proposal", proposalId);
        command.ExecuteNonQuery();
    }

    private static ReportRecord ParsimonyReport(string reportId, IReadOnlyList<string> responseAssessmentIds,
        IReadOnlyList<string> inputAssessmentIds, IReadOnlyList<string> evidenceAssessmentIds)
    {
        var token = new BaselineToken("project", "sha256:" + new string('1', 64), "1",
            "2026-10-13T00:00:00Z", "sha256:" + new string('2', 64));
        var inputs = ParsimonyInputs(token, inputAssessmentIds);
        var response = new ParsimonyReportResponse(reportId, inputs, [], "saved Parsimony report")
        {
            AssessmentIds = responseAssessmentIds,
            JoinQuality = new ParsimonyJoinQuality("default-selection", false, 0, 0, 0, 0, 0, 0, 0,
                null, null, null, null, null),
            Notes = [],
        };
        var evidence = new
        {
            inputs = ParsimonyInputs(token, evidenceAssessmentIds),
            grammarFactsPath = "grammar-facts.sqlite",
            evidencePath = "evidence.sqlite",
            findings = Array.Empty<object>(),
            joinQuality = new ParsimonyJoinQuality("default-selection", false, 0, 0, 0, 0, 0, 0, 0,
                null, null, null, null, null),
        };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        return new ReportRecord(reportId, null, null, JsonSerializer.Serialize(response, options),
            JsonSerializer.Serialize(evidence, options), "parsimony");
    }

    private static ParsimonyReportInputs ParsimonyInputs(BaselineToken token, IReadOnlyList<string> assessmentIds) =>
        new("bundle/1", token, "baseline", null, "model/1",
            new ParsimonyArtifactDigest(1, new string('3', 64)),
            new ParsimonyArtifactDigest(1, new string('4', 64)), null, null, assessmentIds);

    private static ProposalDifferenceSummary DifferenceSummary(string differenceId, string proposalId,
        string beforeAssessmentId, string afterAssessmentId) =>
        new(differenceId, proposalId, "trial/1", beforeAssessmentId, afterAssessmentId, "text/1", "Text", "baseline/1",
            new ProposalDifferencePolicy(), 0, 0, 0, 0, 0, 0, [], [], "2026-10-13T00:00:00Z");

    // A wall-clock bound flakes under load; the statement count inside the write lock does not.
    [Fact]
    public void RecordingATrialSizedAssessmentRunsAFewStatementsPerHundredWordsWhileItHoldsTheWriteLock()
    {
        _ = NewRepository("statement-budget.fwdata", out var database);
        using var ownedDatabase = database;
        var words = Enumerable.Range(0, 1000).Select(index => new AssessedWord("w" + index, "analysed",
            Enumerable.Range(0, 3).Select(analysis => new ParsedAnalysis("cat-guid",
                ["root-" + index, "suffix-" + analysis], 0, $"sha256:{index}-{analysis}")).ToArray())).ToArray();
        var record = NewAssessment("assessment/statement-budget", null, null, "Correctness", words);
        using var connection = database.OpenConnection();
        var statements = 0;
        SQLitePCL.raw.sqlite3_trace(connection.Handle, (_, sql) =>
        {
            if (!sql.StartsWith("--", StringComparison.Ordinal)) statements++;
        }, null);

        using (var transaction = connection.BeginTransaction())
        {
            AssessmentRepository.InsertRecords(connection, transaction, [record]);
            transaction.Commit();
        }

        // 10 word and 30 analysis batches, plus BEGIN, COMMIT, the header and the id read; row at a time is 5,000.
        Assert.True(statements <= 40 + 6, $"{statements} statements recorded 1,000 words and 3,000 analyses.");
        Assert.Equal(1000L, long.Parse(Scalar(database, "SELECT CAST(COUNT(*) AS TEXT) FROM AssessedWords")));
        Assert.Equal(3000L, long.Parse(Scalar(database, "SELECT CAST(COUNT(*) AS TEXT) FROM ParsedAnalyses")));
    }

    [Theory]
    [InlineData(2L, true)]
    [InlineData(1L, false)]
    public void IdAllocationNeverWraps(
        long headroom, bool fits)
    {
        var repository = NewRepository($"id-allocation-{headroom}.fwdata", out var database);
        using var ownedDatabase = database;
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "INSERT OR REPLACE INTO sqlite_sequence (name, seq) VALUES ('AssessedWords', $seq);";
            command.Parameters.AddWithValue("$seq", long.MaxValue - headroom);
            command.ExecuteNonQuery();
        }
        var record = NewAssessment($"assessment/id-{headroom}", null, null, "Correctness");

        if (fits)
        {
            repository.RecordBatch([record]);
            Assert.Equal(long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Scalar(database, "SELECT CAST(MAX(AssessedWordId) AS TEXT) FROM AssessedWords"));
            return;
        }
        Assert.Throws<InvalidOperationException>(() => repository.RecordBatch([record]));
        Assert.Equal("0", Scalar(database, "SELECT CAST(COUNT(*) AS TEXT) FROM Assessments"));
        Assert.Equal("0", Scalar(database, "SELECT CAST(COUNT(*) AS TEXT) FROM AssessedWords"));
        Assert.Equal((long.MaxValue - headroom).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Scalar(database, "SELECT CAST(seq AS TEXT) FROM sqlite_sequence WHERE name = 'AssessedWords'"));
    }

    private static string Scalar(MotifDatabase database, string sql)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (string)command.ExecuteScalar()!;
    }

    private IAssessmentRepository NewRepository(string fileName, out MotifDatabase database)
    {
        var project = new ProjectLocator(Path.Combine(_root, fileName), Path.GetFileNameWithoutExtension(fileName));
        database = MotifDatabase.OpenOwned(
            Path.Combine(_root, Path.GetFileNameWithoutExtension(fileName) + ".motif.db"), project,
            MotifSchema.CurrentSchema, new Version(1, 0));
        _databases.Add(database);
        return new AssessmentRepository(database);
    }

    private static void SeedProposal(MotifDatabase database, string proposalId)
    {
        using var connection = database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO Proposals (ProposalId, CurrentIntentDigest, Status) " +
            "VALUES ($id, 'sha256:intent', 'proposed');";
        command.Parameters.AddWithValue("$id", proposalId);
        command.ExecuteNonQuery();
    }

    private static NewAssessmentRecord NewAssessment(
        string assessmentId, CanonicalId? proposalId, string? proposalIntentDigest, string kind,
        IReadOnlyList<AssessedWord>? words = null, string? savedUtc = null)
    {
        var selection = Selection.Create("selection-1", ["bo", "za"]);
        return new NewAssessmentRecord(
            AssessmentId: assessmentId,
            ProposalId: proposalId,
            ProposalIntentDigest: proposalIntentDigest,
            Assessor: "pangloss",
            Kind: kind,
            ScopeJson: "{\"words\":\"all\"}",
            ScopeDigest: "sha256:scope",
            Subject: new AssessmentSubject.Words(selection, "whitespace", "1"),
            BaselineToken: "{\"baseline\":true}",
            OutcomeDigest: "sha256:outcome",
            SemanticDigest: "sha256:semantic",
            GrammarSourceSha256: "sha256:grammar",
            ModelFingerprint: "fingerprint",
            Pipeline: "pipeline",
            DiagnosticCount: 0,
            Material: new AssessmentRecordMaterial.Words(words ??
            [
                new AssessedWord("bo", "complete", [new ParsedAnalysis("cat-guid", ["guid-root"], 0, "sha256:identity")]),
                new AssessedWord("za", "incomplete", [])
            ], null, null, []),
            SavedUtc: savedUtc
        );
    }

    public void Dispose()
    {
        // An open store keeps its pooled database handles, which Windows will not let a delete pass.
        foreach (var database in _databases) database.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (DirectoryNotFoundException) { }
    }
}
