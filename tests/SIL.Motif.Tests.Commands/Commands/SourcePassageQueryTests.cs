using System.Diagnostics;
using System.Security.Cryptography;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Tests.QueryScale;
using Xunit.Abstractions;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Host;
using SIL.Motif.Host.Sources;
using SIL.Motif.Host.Store;
using SIL.Motif.Scripture;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Sources;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class SourcePassageQueryTests(ITestOutputHelper output) : IDisposable
{
    private IDisposable? _environment;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-passage-query-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void ReadOfAnAbsentStoreDoesNotCreateAnything()
    {
        var project = Project();
        var before = Directory.GetFiles(_root);
        Assert.Equal(RefusalCodes.SourceIndexMissing, Query(project).Refusal?.Code);
        Assert.Equal(before, Directory.GetFiles(_root));
    }

    [Fact]
    public void PagesAreBoundedAndCursorsCannotCrossScopeProjectOrGeneration()
    {
        var project = Project();
        using var database = Open(project);
        Publish(database, 501);
        var before = SHA256.HashData(File.ReadAllBytes(project.FullFwDataPath));
        var counts = new List<int>();
        SourceIndexRepository.RowsRead.Value = counts.Add;
        ProgressSourceIdentity.BeforeHash.Value = () => throw new InvalidOperationException("Read hashed project content.");
        try
        {
            var first = Query(project);
            Assert.True(first.Succeeded, first.Refusal?.Message);
            Assert.Equal(100, first.Value!.Places.Count);
            Assert.Equal("MAT 1:1#1", first.Value.Places[0].Anchor);
            Assert.Equal("unavailable", first.Value.ApprovedState);
            Assert.All(first.Value.Places, row => Assert.Equal("unmeasured", row.State));
            var second = SourcePassageQuery.Query(new(project.FullFwDataPath, "web", "MAT 1", 200, first.Value.NextCursor));
            Assert.True(second.Succeeded, second.Refusal?.Message);
            Assert.Equal(101, second.Value!.Places[0].Ordinal);
            Assert.Equal(300, second.Value.Places[^1].Ordinal);
            var third = SourcePassageQuery.Query(new(project.FullFwDataPath, "WEB", "MAT 1", 200, second.Value.NextCursor));
            var last = SourcePassageQuery.Query(new(project.FullFwDataPath, "WEB", "MAT 1", 200, third.Value!.NextCursor));
            Assert.Single(last.Value!.Places);
            Assert.Null(last.Value.NextCursor);
            Assert.Equal([101, 201, 201, 1], counts);
            Assert.Equal(RefusalCodes.SourcePassageCursorInvalid,
                SourcePassageQuery.Query(new(project.FullFwDataPath, "other", "MAT 1", Cursor: first.Value.NextCursor)).Refusal?.Code);
            Assert.Equal(RefusalCodes.SourcePassageCursorInvalid,
                SourcePassageQuery.Query(new(project.FullFwDataPath, "WEB", "MAT 2", Cursor: first.Value.NextCursor)).Refusal?.Code);
            ProgressSourceIdentity.BeforeHash.Value = null;
            var other = Project("other");
            using var otherDb = Open(other);
            Publish(otherDb, 501);
            Assert.Equal(RefusalCodes.SourcePassageCursorInvalid,
                SourcePassageQuery.Query(new(other.FullFwDataPath, "WEB", "MAT 1", Cursor: first.Value.NextCursor)).Refusal?.Code);
            Publish(database, 501);
            Assert.Equal(RefusalCodes.SourceGenerationChanged,
                SourcePassageQuery.Query(new(project.FullFwDataPath, "WEB", "MAT 1", Cursor: first.Value.NextCursor)).Refusal?.Code);
        }
        finally { SourceIndexRepository.RowsRead.Value = null; ProgressSourceIdentity.BeforeHash.Value = null; }
        Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(project.FullFwDataPath)));
    }

    [Fact]
    public void SourceProgressReadsDenominatorsWithoutReadingPlacesAndRefusesStaleGenerations()
    {
        var project = Project();
        Assert.Equal(RefusalCodes.SourceIndexMissing, SourceProgressQuery.Query(project.FullFwDataPath, "WEB").Refusal?.Code);
        using var database = Open(project);
        Publish(database, 501);
        using (var connection = database.OpenConnection())
        using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO SourcePassages VALUES ('WEB','MAT 2','MAT 2','hash',7,5);";
            insert.ExecuteNonQuery();
        }
        var work = new List<(int Statements, int Rows)>();
        SourceProgressQuery.ReadWork.Value = (statements, rows) => work.Add((statements, rows));
        SourceIndexRepository.RowsRead.Value = _ => throw new InvalidOperationException("Read word places.");
        ProgressSourceIdentity.BeforeHash.Value = () => throw new InvalidOperationException("Read hashed project content.");
        try
        {
            var summary = SourceProgressQuery.Query(project.FullFwDataPath, "web");
            Assert.True(summary.Succeeded, summary.Refusal?.Message);
            Assert.Equal(506, summary.Value!.EligiblePlaces);
            Assert.Null(summary.Value.ApprovedPlaces);
            Assert.Equal("unavailable", summary.Value.ApprovedState);
            var page = SourceProgressQuery.Query(project.FullFwDataPath, "WEB", "passages", 1);
            Assert.True(page.Succeeded, page.Refusal?.Message);
            Assert.Equal(501, Assert.Single(page.Value!.Passages).EligiblePlaces);
            Assert.Null(page.Value.Passages[0].ApprovedPlaces);
            Assert.Equal(summary.Value.EvidenceRevision, page.Value.EvidenceRevision);
            Assert.NotNull(page.Value.NextCursor);
            var next = SourceProgressQuery.Query(project.FullFwDataPath, "WEB", "passages", 1, page.Value.NextCursor);
            Assert.True(next.Succeeded, next.Refusal?.Message);
            Assert.Equal(5, Assert.Single(next.Value!.Passages).EligiblePlaces);
            Assert.Null(next.Value.NextCursor);
            Assert.Equal([(1, 0), (2, 2), (2, 1)], work);
            Assert.Equal(RefusalCodes.ProgressInvalidCursor,
                SourceProgressQuery.Query(project.FullFwDataPath, "OTHER", "passages", 1, page.Value.NextCursor).Refusal?.Code);
            new SourceIndexRepository(database).MarkPending("WEB");
            Assert.Equal(RefusalCodes.SourceIndexStale, SourceProgressQuery.Query(project.FullFwDataPath, "WEB").Refusal?.Code);
            Assert.Equal(RefusalCodes.SourceGenerationChanged,
                SourceProgressQuery.Query(project.FullFwDataPath, "WEB", "passages", 1, page.Value.NextCursor).Refusal?.Code);
        }
        finally
        {
            SourceProgressQuery.ReadWork.Value = null;
            SourceIndexRepository.RowsRead.Value = null;
            ProgressSourceIdentity.BeforeHash.Value = null;
        }
    }

    [Fact]
    public void UnicodeSourceIdsUseTheSameCaseInsensitiveIdentityAsTheSourceList()
    {
        var project = Project();
        using var database = Open(project);
        Publish(database, 1, sourceId: "ñandú");
        var result = SourcePassageQuery.Query(new(project.FullFwDataPath, "ÑANDÚ", "MAT 1"));
        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Single(result.Value!.Places);
    }

    [Fact]
    public void MissingRemovedUnsupportedAndUnknownPassagesAreExplicit()
    {
        var project = Project();
        using var database = Open(project);
        Assert.Equal(RefusalCodes.SourceIndexMissing, Query(project).Refusal?.Code);
        Publish(database, 1);
        Assert.Equal(RefusalCodes.SourceNotFound,
            SourcePassageQuery.Query(new(project.FullFwDataPath, "NOPE", "MAT 1")).Refusal?.Code);
        Assert.Equal(RefusalCodes.SourceChapterNotInSource,
            SourcePassageQuery.Query(new(project.FullFwDataPath, "WEB", "MAT 99")).Refusal?.Code);
        Publish(database, 1, status: "Removed");
        Assert.Equal(RefusalCodes.SourceIdRemoved, Query(project).Refusal?.Code);
        Publish(database, 1, kind: "Corpus");
        Assert.Equal(RefusalCodes.SourceKindUnsupported, Query(project).Refusal?.Code);
        Publish(database, 1);
        File.Delete(Path.Combine(Folder(project), "Settings.xml"));
        Assert.Equal(RefusalCodes.SourceFolderMissing, Query(project).Refusal?.Code);
    }

    [Fact]
    public void PendingSourceProjectWritingSystemAndPolicyChangesRefuseWithoutRebuilding()
    {
        var project = Project();
        using var database = Open(project);
        Publish(database, 1);
        new SourceIndexRepository(database).MarkPending("WEB");
        Assert.Equal(RefusalCodes.SourceIndexStale, Query(project).Refusal?.Code);
        Publish(database, 1);
        File.AppendAllText(Path.Combine(Folder(project), "book"), "changed");
        Assert.Equal(RefusalCodes.SourceIndexStale, Query(project).Refusal?.Code);
        Publish(database, 1);
        File.AppendAllText(project.FullFwDataPath, "changed");
        Assert.Equal(RefusalCodes.SourceIndexStale, Query(project).Refusal?.Code);
        Publish(database, 1);
        var ws = Path.Combine(Path.GetDirectoryName(project.FullFwDataPath)!, "WritingSystemStore");
        Directory.CreateDirectory(ws);
        File.WriteAllText(Path.Combine(ws, "test.ldml"), "changed");
        Assert.Equal(RefusalCodes.SourceIndexStale, Query(project).Refusal?.Code);
        Publish(database, 1, tokenizer: "unknown-policy");
        Assert.Equal(RefusalCodes.SourceIndexStale, Query(project).Refusal?.Code);
    }

    [Fact]
    public void FieldWorksTextsPagesAndTotalsUseTheSameQueriesWithoutAFolderOrKindBranching()
    {
        var project = Project();
        using var database = Open(project);
        var textId = Guid.NewGuid().ToString("D");
        PublishTexts(database, textId, 450);
        var counts = new List<int>();
        SourceIndexRepository.RowsRead.Value = counts.Add;
        ProgressSourceIdentity.BeforeHash.Value = () => throw new InvalidOperationException("Read hashed project content.");
        try
        {
            var first = SourcePassageQuery.Query(new(project.FullFwDataPath, FieldWorksTextsIndex.SourceId.ToLowerInvariant(),
                textId.ToUpperInvariant(), 200));
            Assert.True(first.Succeeded, first.Refusal?.Message);
            Assert.Equal(200, first.Value!.Places.Count);
            Assert.Equal("seg#0", first.Value.Places[0].Anchor);
            Assert.Equal("fw:word", first.Value.Places[0].WordformKey);
            Assert.Equal("fw-word-id", first.Value.Places[0].FieldWorksWordformId);
            Assert.Equal("unavailable", first.Value.ApprovedState);
            Assert.Equal(textId, first.Value.Ref);
            var second = SourcePassageQuery.Query(new(project.FullFwDataPath, FieldWorksTextsIndex.SourceId, textId, 200,
                first.Value.NextCursor));
            Assert.Equal(201, second.Value!.Places[0].Ordinal);
            var last = SourcePassageQuery.Query(new(project.FullFwDataPath, FieldWorksTextsIndex.SourceId, textId, 200,
                second.Value.NextCursor));
            Assert.Equal(50, last.Value!.Places.Count);
            Assert.Null(last.Value.NextCursor);
            Assert.Equal([201, 201, 50], counts);
            var work = new List<(int Statements, int Rows)>();
            SourceProgressQuery.ReadWork.Value = (statements, rows) => work.Add((statements, rows));
            SourceIndexRepository.RowsRead.Value = _ => throw new InvalidOperationException("Read word places.");
            var total = SourceProgressQuery.Query(project.FullFwDataPath, FieldWorksTextsIndex.SourceId);
            Assert.True(total.Succeeded, total.Refusal?.Message);
            Assert.Equal(440, total.Value!.EligiblePlaces);
            Assert.Equal("unavailable", total.Value.ApprovedState);
            Assert.Equal([(1, 0)], work);
            Assert.Equal(RefusalCodes.SourceRefInvalid,
                SourcePassageQuery.Query(new(project.FullFwDataPath, FieldWorksTextsIndex.SourceId, "MAT 1")).Refusal?.Code);
            Assert.Equal(RefusalCodes.SourceChapterNotInSource,
                SourcePassageQuery.Query(new(project.FullFwDataPath, FieldWorksTextsIndex.SourceId, Guid.NewGuid().ToString())).Refusal?.Code);
        }
        finally
        {
            SourceIndexRepository.RowsRead.Value = null;
            SourceProgressQuery.ReadWork.Value = null;
            ProgressSourceIdentity.BeforeHash.Value = null;
        }
    }

    [Fact]
    public void FieldWorksTextsAreExplicitlyMissingOrStaleAndAParatextSourceRefusesATextId()
    {
        var project = Project();
        using var database = Open(project);
        var textId = Guid.NewGuid().ToString("D");
        Assert.Equal(RefusalCodes.SourceIndexMissing,
            SourcePassageQuery.Query(new(project.FullFwDataPath, FieldWorksTextsIndex.SourceId, textId)).Refusal?.Code);
        PublishTexts(database, textId, 3);
        Publish(database, 1);
        Assert.Equal(RefusalCodes.SourceRefInvalid,
            SourcePassageQuery.Query(new(project.FullFwDataPath, "WEB", textId)).Refusal?.Code);
        new SourceIndexRepository(database).MarkPending(FieldWorksTextsIndex.SourceId);
        Assert.Equal(RefusalCodes.SourceIndexStale,
            SourcePassageQuery.Query(new(project.FullFwDataPath, FieldWorksTextsIndex.SourceId, textId)).Refusal?.Code);
        Assert.Equal(RefusalCodes.SourceIndexStale, SourceProgressQuery.Query(project.FullFwDataPath, FieldWorksTextsIndex.SourceId).Refusal?.Code);
        PublishTexts(database, textId, 3);
        File.AppendAllText(project.FullFwDataPath, "changed");
        Assert.Equal(RefusalCodes.SourceIndexStale,
            SourcePassageQuery.Query(new(project.FullFwDataPath, FieldWorksTextsIndex.SourceId, textId)).Refusal?.Code);
        Assert.Equal(RefusalCodes.SourceIndexStale, SourceProgressQuery.Query(project.FullFwDataPath, FieldWorksTextsIndex.SourceId).Refusal?.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    [InlineData(-1)]
    public void InvalidLimitsAreRefusedBeforeStorage(int limit) => Assert.Equal(RefusalCodes.SourcePassageLimitInvalid,
        SourcePassageQuery.Query(new("unused", "WEB", "MAT 1", limit)).Refusal?.Code);

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("e30=")]
    public void InvalidCursorsAreRefusedBeforeStorage(string cursor) => Assert.Equal(RefusalCodes.SourcePassageCursorInvalid,
        SourcePassageQuery.Query(new("unused", "WEB", "MAT 1", Cursor: cursor)).Refusal?.Code);

    [Fact]
    public void CorruptLocalSettingsAreLeftByteForByteInPlace()
    {
        var project = Project();
        using var database = Open(project);
        Publish(database, 1);
        var local = ScriptureLocalStore.ForInstallation().FilePath;
        var old = File.ReadAllText(local);
        try
        {
            File.WriteAllText(local, "invalid-json");
            Assert.Equal(RefusalCodes.SourceFolderMissing, Query(project).Refusal?.Code);
            Assert.Equal("invalid-json", File.ReadAllText(local));
        }
        finally { File.WriteAllText(local, old); }
    }

    [SlowScaleFact]
    [Trait("Category", "SlowScale")]
    public void CebSizedIndexReportsWarmPageAndPendingPublicationCosts()
    {
        var project = Project();
        using var database = Open(project);
        var watch = Stopwatch.StartNew();
        Publish(database, 835650);
        output.WriteLine($"835650-place generation: {watch.Elapsed.TotalSeconds:F2} s");
        _ = Query(project);
        foreach (var limit in new[] { 100, 200 })
        {
            watch.Restart();
            var result = SourcePassageQuery.Query(new(project.FullFwDataPath, "WEB", "MAT 1", limit));
            watch.Stop();
            Assert.True(result.Succeeded, result.Refusal?.Message);
            Assert.Equal(limit, result.Value!.Places.Count);
            output.WriteLine($"Warm {limit}-place page: {watch.Elapsed.TotalMilliseconds:F2} ms");
            Assert.True(watch.ElapsedMilliseconds < 300, "Warm pages should stay below 300 ms.");
        }
        watch.Restart();
        SourceIndexRefresh.Enqueue(database);
        watch.Stop();
        output.WriteLine($"Apply-side pending publication and durable enqueue: {watch.Elapsed.TotalMilliseconds:F2} ms");
        Assert.True(watch.ElapsedMilliseconds < 1000, "Apply-side work should stay below one second.");
    }

    private ProjectLocator Project(string name = "project")
    {
        Directory.CreateDirectory(_root);
        _environment ??= AmbientEnvironment.Push(new Dictionary<string, string?>
        {
            [ScriptureLocalStore.PathEnvironmentVariable] = Path.Combine(_root, "local.json"),
        });
        var path = Path.Combine(_root, name + ".fwdata");
        File.WriteAllText(path, "saved-file-placeholder");
        return new(path, name);
    }

    private static MotifDatabase Open(ProjectLocator project) => MotifDatabase.OpenOwned(
        ProjectDatabaseCatalog.DatabasePathFor(project), project, MotifSchema.CurrentSchema, MotifProductVersion.Current);
    private static string Folder(ProjectLocator project) => Path.Combine(Path.GetDirectoryName(project.FullFwDataPath)!,
        project.FieldWorksProjectIdentity + "-source");
    private static CommandOutcome<SIL.Motif.Contract.Responses.SourcePassageResponse> Query(ProjectLocator project) =>
        SourcePassageQuery.Query(new(project.FullFwDataPath, "WEB", "MAT 1"));

    private static void Publish(MotifDatabase database, int count, string status = "Active", string kind = "Paratext",
        string? tokenizer = null, string sourceId = "WEB")
    {
        var project = database.Project;
        var folder = Folder(project);
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Settings.xml"), "settings-placeholder");
        File.WriteAllText(Path.Combine(folder, "book"), "source-placeholder");
        ScriptureLocalStore.ForInstallation().SetSourcePath(ProjectWorkspaceKey.Compute(project), sourceId, folder);
        var identity = new SourceIndexIdentity(folder, "MAT", "English", status, "en",
            ProgressSourceIdentity.Capture(project.FullFwDataPath)!.Serialize(), SourceIndexIdentity.Metadata(folder));
        var state = new SourceIndexState(sourceId, kind, Guid.NewGuid().ToString("N"), "digest", identity.Serialize(), "ready",
            tokenizer ?? ParatextPassages.TokenizerVersion, ParatextPassages.AnchorVersion);
        new SourceIndexRepository(database).Publish(state, [new("MAT 1", "MAT 1", "hash", count, count)],
            [new("word", "en", "word", "Word")], Enumerable.Range(1, count)
                .Select(i => new SourceWordPlace("MAT 1", i, "MAT 1:1#" + i, "MAT 1:1", "Word", "word")));
    }

    private static void PublishTexts(MotifDatabase database, string textId, int count)
    {
        var identity = new SourceIndexIdentity("", "", "", "Active", "en",
            ProgressSourceIdentity.Capture(database.Project.FullFwDataPath)!.Serialize(), []);
        var state = new SourceIndexState(FieldWorksTextsIndex.SourceId, FieldWorksTextsIndex.Kind, Guid.NewGuid().ToString("N"),
            "digest", identity.Serialize(), "ready", FieldWorksTextsIndex.TokenizerVersion, FieldWorksTextsIndex.AnchorVersion);
        new SourceIndexRepository(database).Publish(state, [new(textId, textId, "hash", count, count - Math.Min(count, 10))],
            [new("fw:word", "en", "word", "Word", "fw-word-id")], Enumerable.Range(1, count)
                .Select(i => new SourceWordPlace(textId, i, "seg#" + (i - 1), "1", "Word", "fw:word")));
    }

    public void Dispose()
    {
        _environment?.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
