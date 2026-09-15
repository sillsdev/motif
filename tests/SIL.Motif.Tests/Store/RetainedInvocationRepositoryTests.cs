using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class RetainedInvocationRepositoryTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "motif-retained-invocations-" + Guid.NewGuid().ToString("N"));

    public RetainedInvocationRepositoryTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void InvalidAggregateOrMemberLeavesNoRowsBehind()
    {
        using var database = OpenDatabase("atomic.fwdata");
        var repository = new RetainedInvocationRepository(database);
        var retained = NewRetained("atomic");
        var first = NewAssessment("atomic-parse", "ParseTime", "atomic");
        var invalid = first with { AssessmentId = "atomic-stats", Kind = "WrongKind" };

        Assert.Throws<InvalidDataException>(() => repository.Record(retained, [first, invalid]));

        using var connection = database.OpenConnection();
        Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM RetainedInvocations;"));
        Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM RetainedInvocationMembers;"));
        Assert.Equal(0L, Scalar(connection, "SELECT COUNT(*) FROM Assessments;"));
    }

    [Fact]
    public void GrammarSourceMustMatchSharedInvocationSourceBytes()
    {
        using var database = OpenDatabase("grammar-source.fwdata");
        var repository = new RetainedInvocationRepository(database);
        var assessment = NewAssessment("grammar-assessment", "ParseTime", "grammar") with
        {
            GrammarSourceSha256 = "sha256:wrong",
        };

        Assert.Throws<InvalidDataException>(() => repository.Record(NewRetained("grammar"), [assessment]));
    }

    [Fact]
    public void MemberInsertFailureRollsBackEarlierMembersAndAggregate()
    {
        using var database = OpenDatabase("atomic-insert.fwdata");
        var repository = new RetainedInvocationRepository(database);
        var retained = NewRetained("atomic") with
        {
            Members = [new RetainedInvocationMember("ParseTime", "atomic-parse"),
                new RetainedInvocationMember("ObjectTiming", "atomic-timing")]
        };
        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TRIGGER FailSecondRetainedMember
                AFTER INSERT ON RetainedInvocationMembers
                WHEN NEW.Kind = 'ObjectTiming'
                BEGIN SELECT RAISE(ABORT, 'forced member failure'); END;
                """;
            command.ExecuteNonQuery();
        }

        Assert.Throws<SqliteException>(() => repository.Record(retained, [
            NewAssessment("atomic-parse", "ParseTime", "atomic"),
            NewAssessment("atomic-timing", "ObjectTiming", "atomic")
        ]));

        using var verificationConnection = database.OpenConnection();
        Assert.Equal(0L, Scalar(verificationConnection, "SELECT COUNT(*) FROM RetainedInvocations;"));
        Assert.Equal(0L, Scalar(verificationConnection, "SELECT COUNT(*) FROM RetainedInvocationMembers;"));
        Assert.Equal(0L, Scalar(verificationConnection, "SELECT COUNT(*) FROM Assessments;"));
        Assert.Equal(0L, Scalar(verificationConnection, "SELECT COUNT(*) FROM AssessedWords;"));
        Assert.Equal(0L, Scalar(verificationConnection, "SELECT COUNT(*) FROM AssessmentInvocations;"));
    }

    [Fact]
    public void IdenticalResolvedWordsRemainDistinctWhenSelectionInputsDiffer()
    {
        using var database = OpenDatabase("different-inputs.fwdata");
        var repository = new RetainedInvocationRepository(database);
        var first = NewRetained("first") with
        {
            Selection = NewSelectionDescriptor([Guid.Empty], [], false, false, null, null)
        };
        var second = NewRetained("second") with
        {
            Selection = NewSelectionDescriptor([], ["word"], false, false, null, null)
        };

        repository.Record(first, [NewAssessment("first-assessment", "ParseTime", "first")]);
        repository.Record(second, [NewAssessment("second-assessment", "ParseTime", "second")]);

        var records = repository.List("different-inputs");
        Assert.Equal(["first", "second"], records.Select(record => record.InvocationId));
        Assert.NotEqual(records[0].Selection.TextIds, records[1].Selection.TextIds);
        Assert.NotEqual(records[0].Selection.PastedWords, records[1].Selection.PastedWords);
        Assert.Equal(records[0].Selection.ResolvedWords, records[1].Selection.ResolvedWords);
        Assert.NotEqual(records[0].Selection.DescriptorSha256, records[1].Selection.DescriptorSha256);
    }

    [Fact]
    public void GetAndListValidateAfterRepositoryReopen()
    {
        var path = Path.Combine(_root, "reopen.motif.db");
        var project = Locator("reopen.fwdata");
        using (var database = MotifDatabase.OpenOwned(path, project, MotifSchema.CurrentSchema, new Version(1, 0)))
        {
            var repository = new RetainedInvocationRepository(database);
            repository.Record(NewRetained("reopen") with { ProjectKey = "project" },
                [NewAssessment("reopen-assessment", "ParseTime", "reopen")]);
        }

        using var reopened = MotifDatabase.OpenOwned(path, project, MotifSchema.CurrentSchema, new Version(1, 0));
        var reopenedRepository = new RetainedInvocationRepository(reopened);
        Assert.Equal("reopen", reopenedRepository.Get("reopen").InvocationId);
        Assert.Single(reopenedRepository.List("project"));
    }

    [Fact]
    public void WrongMemberIdentityIsRefusedOnRead()
    {
        using var database = OpenDatabase("wrong-member.fwdata");
        var repository = new RetainedInvocationRepository(database);
        repository.Record(NewRetained("retained"),
            [NewAssessment("retained-assessment", "ParseTime", "retained")]);
        new AssessmentRepository(database).Record(NewAssessment("other", "ParseTime", "other"));

        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE RetainedInvocationMembers SET AssessmentId = 'other' WHERE InvocationId = 'retained';";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidDataException>(() => repository.Get("retained"));
    }

    [Fact]
    public void ArtifactReferenceToAnotherValidInvocationIsRefusedByGetAndList()
    {
        using var database = OpenDatabase("wrong-artifact.fwdata");
        var repository = new RetainedInvocationRepository(database);
        repository.Record(NewRetained("first"), [NewAssessment("first-assessment", "ParseTime", "first")]);
        repository.Record(NewRetained("second"), [NewAssessment("second-assessment", "ParseTime", "second")]);

        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE RetainedInvocations SET ArtifactInvocationId = 'second' " +
                "WHERE InvocationId = 'first';";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidDataException>(() => repository.Get("first"));
        Assert.Throws<InvalidDataException>(() => repository.List("different-inputs"));
    }

    [Fact]
    public void WrongResolvedHashIsRefusedOnWriteEvenWhenDescriptorDigestMatches()
    {
        using var database = OpenDatabase("wrong-resolved-hash.fwdata");
        var repository = new RetainedInvocationRepository(database);
        var selection = NewSelectionDescriptor([], ["word"], false, false, null, null) with
        {
            ResolvedSha256 = "sha256:" + new string('f', 64)
        };
        selection = selection with { DescriptorSha256 = SelectionDescriptorDigest.Compute(selection) };
        var assessment = NewAssessment("wrong-resolved-hash-assessment", "ParseTime", "wrong-resolved-hash") with
        {
            Selection = new Selection("project", ["word"], selection.ResolvedSha256)
        };

        Assert.Throws<InvalidDataException>(() => repository.Record(
            NewRetained("wrong-resolved-hash") with { Selection = selection }, [assessment]));
    }

    [Fact]
    public void NoncanonicalResolvedWordsAreRefusedOnReadWhenDescriptorDigestMatches()
    {
        using var database = OpenDatabase("noncanonical-resolved-words.fwdata");
        var repository = new RetainedInvocationRepository(database);
        repository.Record(NewRetained("noncanonical-resolved-words"),
            [NewAssessment("noncanonical-resolved-words-assessment", "ParseTime", "noncanonical-resolved-words")]);

        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            var selectionJson = (string)Scalar(connection,
                "SELECT SelectionDescriptorJson FROM RetainedInvocations " +
                "WHERE InvocationId = 'noncanonical-resolved-words';")!;
            var selection = JsonSerializer.Deserialize<SelectionDescriptor>(selectionJson)! with
            {
                ResolvedWords = ["z-word", "word", "word"],
            };
            selection = selection with
            {
                ResolvedSha256 = Selection.Create("project", selection.ResolvedWords).Sha256,
            };
            selection = selection with { DescriptorSha256 = SelectionDescriptorDigest.Compute(selection) };
            command.CommandText = "UPDATE RetainedInvocations SET SelectionDescriptorJson = $selection, " +
                "SelectionDescriptorSha256 = $digest WHERE InvocationId = 'noncanonical-resolved-words';";
            command.Parameters.AddWithValue("$selection", JsonSerializer.Serialize(selection));
            command.Parameters.AddWithValue("$digest", selection.DescriptorSha256);
            command.ExecuteNonQuery();
        }

        Assert.Equal("A retained Selection descriptor has invalid resolved words or hash.",
            Assert.Throws<InvalidDataException>(() => repository.Get("noncanonical-resolved-words")).Message);
        Assert.Equal("A retained Selection descriptor has invalid resolved words or hash.",
            Assert.Throws<InvalidDataException>(() => repository.List("different-inputs")).Message);
    }

    [Fact]
    public void SelectionDescriptorCorruptionIsRefusedByGetAndList()
    {
        using var database = OpenDatabase("corrupt-selection.fwdata");
        var repository = new RetainedInvocationRepository(database);
        repository.Record(NewRetained("corrupt-selection"),
            [NewAssessment("corrupt-selection-assessment", "ParseTime", "corrupt-selection")]);

        using (var connection = database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE RetainedInvocations SET SelectionDescriptorJson = " +
                "REPLACE(SelectionDescriptorJson, 'word', 'tampered') WHERE InvocationId = 'corrupt-selection';";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidDataException>(() => repository.Get("corrupt-selection"));
        Assert.Throws<InvalidDataException>(() => repository.List("different-inputs"));
    }

    [Fact]
    public void MissingMemberIsRefusedOnRead()
    {
        using var database = OpenDatabase("missing-member.fwdata");
        var repository = new RetainedInvocationRepository(database);
        repository.Record(NewRetained("retained"), [NewAssessment("retained-assessment", "ParseTime", "retained")]);

        using (var connection = database.OpenConnection())
        {
            using var pragma = connection.CreateCommand();
            pragma.CommandText = "PRAGMA foreign_keys = OFF;";
            pragma.ExecuteNonQuery();
            using var delete = connection.CreateCommand();
            delete.CommandText = "DELETE FROM RetainedInvocationMembers WHERE InvocationId = 'retained';";
            delete.ExecuteNonQuery();
        }

        Assert.Throws<InvalidDataException>(() => repository.Get("retained"));
    }

    [Fact]
    public void RetainedRunKeepsItsBaselineReferenceWhenAnotherRunUsesAnotherBaseline()
    {
        using var database = OpenDatabase("baseline-refresh.fwdata");
        var repository = new RetainedInvocationRepository(database);
        var baselineA = NewBaseline('b');
        var baselineB = NewBaseline('c');
        repository.Record(NewRetained("run-a", baselineA) with
        {
            BaselineRootDirectory = "C:/managed/baseline-a",
            BaselineFwDataPath = "C:/managed/baseline-a/project.fwdata"
        }, [NewAssessment("run-a-assessment", "ParseTime", "run-a", baselineA)]);
        repository.Record(NewRetained("run-b", baselineB) with
        {
            BaselineRootDirectory = "C:/managed/baseline-b",
            BaselineFwDataPath = "C:/managed/baseline-b/project.fwdata"
        }, [NewAssessment("run-b-assessment", "ParseTime", "run-b", baselineB)]);

        var runA = repository.Get("run-a");
        Assert.Equal(baselineA, runA.BaselineToken);
        Assert.Equal("C:/managed/baseline-a", runA.BaselineRootDirectory);
        Assert.Equal("C:/managed/baseline-a/project.fwdata", runA.BaselineFwDataPath);
    }

    private RetainedInvocationRecord NewRetained(string invocationId, BaselineToken? baseline = null) => new(
        invocationId, "different-inputs", baseline ?? NewBaseline('b'), "C:/managed/baseline-a",
        "C:/managed/baseline-a/project.fwdata", Utc("2020-01-01T00:00:00Z"),
        Utc("2020-01-01T00:01:00Z"), Utc("2020-01-01T00:02:00Z"),
        NewSelectionDescriptor([], ["word"], false, false, null, null), "pangloss",
        "{\"words\":\"all\"}", "sha256:scope", invocationId,
        [new RetainedInvocationMember("ParseTime", invocationId + "-assessment")]);

    private static SelectionDescriptor NewSelectionDescriptor(
        IReadOnlyList<Guid> textIds, IReadOnlyList<string> pastedWords, bool allWordforms,
        bool retryFailed, string? retrySource, TimeSpan? retrySlowerThan)
    {
        var descriptor = new SelectionDescriptor(
            textIds, pastedWords, allWordforms, retryFailed, retrySource, retrySlowerThan,
            ["word"], Selection.Create("project", ["word"]).Sha256, [new("pasted-words", 1)]);
        return descriptor with { DescriptorSha256 = SelectionDescriptorDigest.Compute(descriptor) };
    }

    private static NewAssessmentRecord NewAssessment(
        string id, string kind, string invocationId, BaselineToken? baseline = null) =>
        new(id, null, null, "pangloss", kind, "{\"words\":\"all\"}", "sha256:scope", "none", "1",
            JsonSerializer.Serialize(baseline ?? NewBaseline('b')), Selection.Create("project", ["word"]), "sha256:outcome",
            "sha256:semantic", "sha256:source", "fingerprint", "pipeline", 0,
            [new AssessedWord("word", "complete", [])])
        {
            Invocation = new BatchInvocationEvidence(
                invocationId, "source.fwdata", "sha256:source", "sha256:executable", "words.txt", "sha256:words",
                "rows.tsv", "sha256:rows", "stderr.txt", "sha256:stderr", 1000, 200000, 1, true)
        };

    private static BaselineToken NewBaseline(char bundleMarker) => new(
        "project", "sha256:" + new string('a', 64), "projection-1", "2020-01-01T00:00:00Z",
        "sha256:" + new string(bundleMarker, 64));

    private MotifDatabase OpenDatabase(string fileName) => MotifDatabase.OpenOwned(
        Path.Combine(_root, Path.GetFileNameWithoutExtension(fileName) + ".motif.db"), Locator(fileName),
        MotifSchema.CurrentSchema, new Version(1, 0));

    private ProjectLocator Locator(string fileName) => new(Path.Combine(_root, fileName), "project");

    private static DateTimeOffset Utc(string value) => DateTimeOffset.ParseExact(
        value, "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
    }
}
