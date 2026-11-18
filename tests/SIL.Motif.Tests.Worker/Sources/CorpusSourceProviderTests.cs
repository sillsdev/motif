using System.Diagnostics;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Host.Store;
using SIL.Motif.Scripture;
using SIL.Motif.Worker.Sources;
using SIL.Scripture;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Worker.Sources;

public sealed class CorpusSourceProviderTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("motif-corpus-source-").FullName;
    private readonly WhitespaceAndPunctuationTokeniser _tokeniser = new();
    private MotifDatabase? _database;

    private MotifDatabase Database => _database ??= MotifDatabase.OpenOwned(
        Path.Combine(_root, "project.motif.db"), new ProjectLocator(Path.Combine(_root, "project.fwdata"), "fixture"),
        MotifSchema.CurrentSchema, new Version(1, 0));

    private CorpusSourceIndex Index => SourceProviders.Corpus.Open(Database);

    [Fact]
    public void CorpusKindHasAProvider()
    {
        var provider = Assert.IsType<CorpusSourceProvider>(SourceProviders.For(SourceKind.Corpus));
        Assert.False(provider.MeasuresApprovals);
    }

    [Fact]
    public void DuplicateJsonlIdsAcrossFilesNameTheIdAndLeaveTheHeadIntact()
    {
        var folder = SourceFolder();
        var first = Path.Combine(folder, "a.jsonl");
        File.WriteAllText(first, Record("same", "old word"));
        var source = Declaration(CorpusSourceFormat.Jsonl, folder);
        var before = Index.Refresh(source, _tokeniser);
        File.WriteAllText(Path.Combine(folder, "b.jsonl"), Record("same", "different word"));

        var failure = Assert.Throws<InvalidDataException>(() => Index.Refresh(source, _tokeniser));
        Assert.Contains("'same'", failure.Message);
        var current = Index.Pin(source.SourceId)!;
        Assert.Equal(before.Stamp, current.Stamp);
        Assert.Equal(CorpusFreshness.Unavailable, current.Freshness);
        Assert.Equal(0L, Stages());
    }

    [Fact]
    public void MalformedJsonlAndBlankRecordsRefusePublication()
    {
        var path = Path.Combine(SourceFolder(), "data.jsonl");
        var source = Declaration(CorpusSourceFormat.Jsonl, path);
        foreach (var malformed in new[] { """{"id":"doc","text":"one","text":"two"}""",
                     """{"id":"doc","title":"missing text"}""", Record("doc", "one") + "\n\n" })
        {
            File.WriteAllText(path, malformed);
            Assert.Throws<InvalidDataException>(() => Index.Refresh(source, _tokeniser));
            Assert.Null(Index.Pin(source.SourceId));
        }
    }

    [Fact]
    public void SameLengthAndMtimeEditChangesHashAndReusesUnchangedJsonlDocuments()
    {
        var folder = SourceFolder();
        var file = Path.Combine(folder, "a.jsonl");
        var first = Record("doc", "red red fox") + "\n" + Record("other", "fox");
        var updated = Record("doc", "tan red fox") + "\n" + Record("other", "fox");
        File.WriteAllText(file, first);
        File.WriteAllText(Path.Combine(folder, "b.jsonl"), Record("extra", "extra"));
        var timestamp = File.GetLastWriteTimeUtc(file);
        var source = Declaration(CorpusSourceFormat.Jsonl, folder);
        var before = Index.Refresh(source, _tokeniser);
        Assert.Equal(CorpusFreshness.Verified, Index.Verify(source).Freshness);

        File.WriteAllText(file, updated);
        File.SetLastWriteTimeUtc(file, timestamp);
        Assert.Equal(first.Length, new FileInfo(file).Length);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(file));
        Assert.Equal(CorpusFreshness.Changed, Index.Verify(source).Freshness);
        Assert.Equal(CorpusFreshness.Changed, Index.Pin(source.SourceId)!.Freshness);

        var after = Index.Refresh(source, _tokeniser);
        Assert.NotEqual(before.Stamp.InventoryDigest, after.Stamp.InventoryDigest);
        Assert.Equal((1, 2), (after.ShardsBuilt, after.ShardsReused));
        Assert.Equal("tan", Index.Pin(source.SourceId)!.Occurrences("doc").Items[0].Surface);
        Assert.Equal("red", Index.Pin(source.SourceId, before.Stamp.Id)!.Occurrences("doc").Items[0].Surface);
    }

    [Fact]
    public void PlainTextByteOffsetsPreserveBomCrlfUnicodeAndCase()
    {
        var folder = SourceFolder();
        var sub = Directory.CreateDirectory(Path.Combine(folder, "Sub")).FullName;
        var file = Path.Combine(sub, "Note.txt");
        File.WriteAllText(file, "Café\r\n\r\n𝔘 cafe\u0301", new UTF8Encoding(true));
        var source = Declaration(CorpusSourceFormat.PlainText, folder);
        Index.Refresh(source, _tokeniser);
        var pin = Index.Pin(source.SourceId)!;
        var document = Assert.Single(pin.Documents().Items);
        Assert.Equal("Sub/Note.txt", document.Id);
        var words = pin.Occurrences(document.Id).Items;

        Assert.Equal(new[] { "Café", "𝔘", "cafe\u0301" }, words.Select(word => word.Surface));
        Assert.Equal(new[] { "Cafe\u0301", "𝔘", "cafe\u0301" }, words.Select(word => word.Wordform!.FormNfd));
        Assert.Equal(new[] { (1, 1, 4L, 5), (3, 1, 13L, 4), (3, 2, 18L, 6) }, words.Select(word =>
            (word.Key.Anchor.Segment, word.Key.Anchor.Word, word.Key.Anchor.Offset!.Value, word.Key.Anchor.Length!.Value)));
        var bytes = File.ReadAllBytes(file);
        Assert.All(words, word => Assert.Equal(word.Surface, Encoding.UTF8.GetString(bytes,
            (int)word.Key.Anchor.Offset!.Value - 1, word.Key.Anchor.Length!.Value)));
        Assert.Equal("𝔘 cafe\u0301", pin.ReadContext(source, words[1].Key).Text);
        var key = words[1].Key;
        foreach (var invalid in new[]
        {
            key.Anchor with { Offset = 0 },
            key.Anchor with { Offset = key.Anchor.Offset + 1 },
            key.Anchor with { Length = key.Anchor.Length + 1 },
            key.Anchor with { Word = 99 },
        })
            Assert.Equal(CorpusSourceFailure.ContextBasisMismatch,
                Assert.Throws<CorpusSourceRefusal>(() => pin.ReadContext(source, key with { Anchor = invalid })).Reason);
    }

    [Fact]
    public void JsonlOffsetsReferToDecodedDocumentAndMetadataChangesReuseItsShard()
    {
        var path = Path.Combine(SourceFolder(), "rows.jsonl");
        var source = Declaration(CorpusSourceFormat.Jsonl, path) with { JsonlIdField = "guid" };
        File.WriteAllText(path, """{"guid":"doc","text":"é\r\nÉ","source":"example","other":{"split":"dev"}}""" + "\r\n",
            new UTF8Encoding(true));
        var first = Index.Refresh(source, _tokeniser);
        var pin = Index.Pin(source.SourceId)!;
        var document = Assert.Single(pin.Documents().Items);
        Assert.Contains("\"split\":\"dev\"", document.Metadata);
        var words = pin.Occurrences("doc").Items;
        Assert.Equal(new long?[] { 1, 5 }, words.Select(word => word.Key.Anchor.Offset));
        var key = words[0].Key;
        Assert.Equal(CorpusSourceFailure.ContextBasisMismatch, Assert.Throws<CorpusSourceRefusal>(() =>
            pin.ReadContext(source, key with { Anchor = key.Anchor with { Offset = 0 } })).Reason);

        File.WriteAllText(path, File.ReadAllText(path).Replace("example", "updated", StringComparison.Ordinal));
        var second = Index.Refresh(source, _tokeniser);
        var changed = Assert.Single(Index.Pin(source.SourceId)!.Documents().Items);
        Assert.NotEqual(first.Stamp, second.Stamp);
        Assert.NotEqual(document.RawHash, changed.RawHash);
        Assert.Equal(document.TextHash, changed.TextHash);
        Assert.Equal((0, 1), (second.ShardsBuilt, second.ShardsReused));
    }

    [Fact]
    public void UsfmUsesChapterIdentitiesAndVerseBridgeAnchorsAndReusesUnchangedChapters()
    {
        var folder = SourceFolder();
        var path = Path.Combine(folder, "GEN.usfm");
        const string usfm = """
            \id GEN Fixture
            \c 1
            \p
            \v 1-2 Café phrase \f + \ft hidden note\f* again
            \v 3 Continuing
            \c 2
            \p
            \v 1 Unchanged
            """;
        File.WriteAllText(path, usfm, new UTF8Encoding(true));
        var source = Declaration(CorpusSourceFormat.Usfm, folder) with { Usfm = new("Fixture edition", ScrVers.English) };
        var first = Index.Refresh(source, _tokeniser);
        var pin = Index.Pin(source.SourceId)!;
        Assert.Equal(new[] { "GEN 1", "GEN 2" }, pin.Documents().Items.Select(item => item.Id));
        var words = pin.Occurrences("GEN 1").Items;
        Assert.Equal(new[] { "Café", "phrase", "again", "Continuing" }, words.Select(item => item.Surface));
        Assert.Equal(new[] { "GEN 1:1#1", "GEN 1:1#2", "GEN 1:1#3", "GEN 1:3#1" },
            words.Select(item => $"{item.Key.Anchor.Verse}#{item.Key.Anchor.Word}"));
        Assert.All(words, word => Assert.Null(word.Key.Anchor.Offset));
        var key = words[0].Key;
        Assert.Equal(CorpusSourceFailure.ContextBasisMismatch, Assert.Throws<CorpusSourceRefusal>(() =>
            pin.ReadContext(source, key with { Anchor = key.Anchor with { Verse = "GEN 1:2" } })).Reason);

        File.WriteAllText(path, usfm.Replace("Continuing", "Changed", StringComparison.Ordinal));
        var after = Index.Refresh(source, _tokeniser);
        Assert.Equal((1, 1), (after.ShardsBuilt, after.ShardsReused));
        Assert.NotEqual(first.Stamp.Id, after.Stamp.Id);
    }

    [Fact]
    public void EveryUsfmBookUsesTheSameFingerprintedStylesheetBytes()
    {
        var folder = SourceFolder();
        File.WriteAllText(Path.Combine(folder, "a.usfm"), "\\id GEN\n\\c 1\n\\p\n\\v 1 First");
        File.WriteAllText(Path.Combine(folder, "b.usfm"),
            "\\id EXO\n\\c 1\n\\p\n\\v 1 Second \\zhidden + \\ft hidden note\\zhidden*");
        var stylesheet = Path.Combine(folder, "fixture.sty");
        using var resource = typeof(SIL.Machine.Corpora.UsfmStylesheet).Assembly
            .GetManifestResourceStream("SIL.Machine.Corpora.usfm.sty")!;
        using var reader = new StreamReader(resource);
        var original = reader.ReadToEnd() + "\n\\Marker zhidden\n\\Endmarker zhidden*\n\\TextType NoteText\n\\StyleType Note\n";
        File.WriteAllText(stylesheet, original);
        var source = Declaration(CorpusSourceFormat.Usfm, folder) with
        {
            Usfm = new("Fixture edition", ScrVers.English, StylesheetPath: stylesheet),
        };
        var tokeniser = new CallbackTokeniser(() => File.Delete(stylesheet));
        var index = Index;
        index.BeforePublish = () => File.WriteAllText(stylesheet, original);

        index.Refresh(source, tokeniser);
        var pin = index.Pin(source.SourceId)!;
        Assert.Equal(CorpusContent.Hash(original), pin.Inventory.Single(file => file.Name == "@stylesheet").RawHash);
        Assert.Equal("Second", Assert.Single(pin.Occurrences("EXO 1").Items).Surface);
    }

    [Fact]
    public void PagesAndSelectionRemainOnTheirPinAcrossRefreshAndSourceRemoval()
    {
        var folder = SourceFolder();
        var path = Path.Combine(folder, "a.txt");
        File.WriteAllText(path, "Fox fox fox");
        File.WriteAllText(Path.Combine(folder, "b.txt"), "fox Owl");
        var source = Declaration(CorpusSourceFormat.PlainText, folder);
        var before = Index.Refresh(source, _tokeniser);
        using var readOnly = CorpusSourceIndex.OpenExisting(Database.FullPath, Database.Project);
        var readIndex = SourceProviders.Corpus.Open(readOnly);
        var pin = readIndex.Pin(source.SourceId)!;
        var first = pin.Occurrences("a.txt", limit: 1);
        var resolution = pin.ResolveSelection(["a.txt", "a.txt"]);
        Assert.Equal((2L, 3L), (resolution.Scope.TypeCount, resolution.Scope.TokenCount));
        var fox = Assert.Single(resolution.Stream(default), row => row.Wordform!.FormNfd == "fox");
        Assert.Equal((3L, 2L), (fox.SourceFrequency, fox.SelectedWeight));
        Assert.Equal(2, pin.Occurrences("a.txt", fox.FormId).Items.Count);

        File.WriteAllText(path, "New word");
        Index.Refresh(source, _tokeniser);
        File.Delete(path);
        var current = readIndex.Pin(source.SourceId)!;
        Assert.Throws<CorpusSourceRefusal>(() => current.Occurrences("a.txt", cursor: first.Next));
        Assert.Equal(new[] { "fox", "fox" }, pin.Occurrences("a.txt", cursor: first.Next).Items.Select(row => row.Surface));
        Assert.Equal(before.Stamp, readIndex.Pin(source.SourceId, before.Stamp.Id)!.Stamp);
        Assert.Equal(3, resolution.Stream(default).Sum(row => row.SelectedWeight));
        var historical = readIndex.Pin(source.SourceId, before.Stamp.Id)!;
        Assert.Equal(resolution.Stream(default), historical.ResolveSelection(["a.txt"],
            expectedMembershipDigest: resolution.Scope.MembershipDigest).Stream(default));
        Assert.Equal(CorpusSourceFailure.SelectionBasisMismatch, Assert.Throws<CorpusSourceRefusal>(() =>
            historical.ResolveSelection(["b.txt"], expectedMembershipDigest: resolution.Scope.MembershipDigest)).Reason);
        Assert.Equal(CorpusSourceFailure.SelectionBasisMismatch, Assert.Throws<CorpusSourceRefusal>(() =>
            historical.ResolveSelection(["a.txt"])).Reason);
        Assert.Equal(CorpusSourceFailure.SelectionBasisMismatch, Assert.Throws<CorpusSourceRefusal>(() =>
            pin.ResolveSelection(["a.txt"], expectedMembershipDigest: resolution.Scope.MembershipDigest)).Reason);
        Assert.Equal(CorpusSourceFailure.ContextUnavailable,
            Assert.Throws<CorpusSourceRefusal>(() => pin.ReadContext(source, first.Items[0].Key)).Reason);
        Assert.Equal(CorpusFreshness.Changed, Index.Verify(source).Freshness);
        File.Delete(Path.Combine(folder, "b.txt"));
        Assert.Equal(CorpusFreshness.Unavailable, Index.Verify(source).Freshness);
        Assert.Equal(CorpusSourceFailure.SourceUnavailable, Assert.Throws<CorpusSourceRefusal>(() =>
            current.Occurrences("a.txt")).Reason);
        Assert.Equal(CorpusSourceFailure.BucketUnavailable,
            Assert.Throws<CorpusSourceRefusal>(() => pin.ResolveSelection(bucket: "no-analysis")).Reason);
    }

    [Fact]
    public void PublicationRefusesAFileEditedAfterItsFinalFingerprint()
    {
        var folder = SourceFolder();
        var first = Path.Combine(folder, "a.txt");
        var second = Path.Combine(folder, "b.txt");
        File.WriteAllText(first, "old word");
        File.WriteAllText(second, "another word");
        var source = Declaration(CorpusSourceFormat.PlainText, folder);
        var index = Index;
        var before = index.Refresh(source, _tokeniser);
        var original = index.Pin(source.SourceId)!.ResolveSelection(["a.txt"]);
        File.WriteAllText(second, "stage a new generation");
        index.AfterRecheckFile = name =>
        {
            if (name == "a.txt") File.WriteAllText(first, "edit during the final scan");
        };

        Assert.ThrowsAny<IOException>(() => index.Refresh(source, _tokeniser));
        var stale = index.Pin(source.SourceId)!;
        Assert.Equal(before.Stamp, stale.Stamp);
        Assert.Equal(CorpusFreshness.Changed, stale.Freshness);
        Assert.Equal(CorpusSourceFailure.SourceChanged, Assert.Throws<CorpusSourceRefusal>(() =>
            stale.ResolveSelection()).Reason);
        Assert.Equal(0L, Stages());
        index.AfterRecheckFile = null;
        Assert.NotEqual(before.Stamp, index.Refresh(source, _tokeniser).Stamp);
        var historical = index.Pin(source.SourceId, before.Stamp.Id)!;
        Assert.Equal(2L, historical.ResolveSelection(["a.txt"],
            expectedMembershipDigest: original.Scope.MembershipDigest).Stream(default).Sum(row => row.SelectedWeight));
    }

    [Fact]
    public void MissingInputDuringRefreshLeavesTheOldHeadUnavailable()
    {
        var path = Path.Combine(SourceFolder(), "a.txt");
        File.WriteAllText(path, "old word");
        var source = Declaration(CorpusSourceFormat.PlainText, path);
        var before = Index.Refresh(source, _tokeniser);
        File.Delete(path);

        Assert.ThrowsAny<IOException>(() => Index.Refresh(source, _tokeniser));

        var stale = Index.Pin(source.SourceId)!;
        Assert.Equal(before.Stamp, stale.Stamp);
        Assert.Equal(CorpusFreshness.Unavailable, stale.Freshness);
        Assert.Equal(CorpusSourceFailure.SourceUnavailable, Assert.Throws<CorpusSourceRefusal>(() =>
            stale.ResolveSelection()).Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefreshRestatsFilesAfterCommittingTheHead(bool reuseHead)
    {
        var path = Path.Combine(SourceFolder(), "a.txt");
        File.WriteAllText(path, "old word");
        var source = Declaration(CorpusSourceFormat.PlainText, path);
        var index = Index;
        var before = index.Refresh(source, _tokeniser);
        if (!reuseHead) File.WriteAllText(path, "new generation");
        index.AfterHeadCommit = () =>
        {
            Assert.Equal(CorpusFreshness.Changed, index.Pin(source.SourceId)!.Freshness);
            File.WriteAllText(path, "ordinary edit after the head commit");
        };

        Assert.ThrowsAny<IOException>(() => index.Refresh(source, _tokeniser));

        var stale = index.Pin(source.SourceId)!;
        Assert.Equal(reuseHead, stale.Stamp == before.Stamp);
        Assert.Equal(CorpusFreshness.Changed, stale.Freshness);
        Assert.Equal(CorpusSourceFailure.SourceChanged, Assert.Throws<CorpusSourceRefusal>(() =>
            stale.ResolveSelection()).Reason);
    }

    [Fact]
    public void VerifyRestatsFilesAfterCommittingItsCheck()
    {
        var path = Path.Combine(SourceFolder(), "a.txt");
        File.WriteAllText(path, "old word");
        var source = Declaration(CorpusSourceFormat.PlainText, path);
        var index = Index;
        index.Refresh(source, _tokeniser);
        index.AfterHeadCommit = () => File.WriteAllText(path, "ordinary edit after the status commit");

        Assert.Equal(CorpusFreshness.Changed, index.Verify(source).Freshness);
        Assert.Equal(CorpusFreshness.Changed, index.Pin(source.SourceId)!.Freshness);
    }

    [Fact]
    public void UnchangedLargeJsonlRefreshStreamsItsFingerprint()
    {
        const int padding = 8 * 1024 * 1024;
        var path = Path.Combine(SourceFolder(), "data.jsonl");
        File.WriteAllText(path, Record("large", "word") + new string(' ', padding));
        var source = Declaration(CorpusSourceFormat.Jsonl, path);
        var index = Index;
        var before = index.Refresh(source, _tokeniser);

        var start = GC.GetAllocatedBytesForCurrentThread();
        var time = Stopwatch.StartNew();
        var refreshed = index.Refresh(source, _tokeniser);
        time.Stop();
        var allocated = GC.GetAllocatedBytesForCurrentThread() - start;

        output.WriteLine($"Unchanged Refresh used {allocated:N0} thread-allocated bytes in {time.Elapsed.TotalMilliseconds:F1} ms.");
        Assert.Equal(before.Stamp, refreshed.Stamp);
        Assert.Equal((0, 1), (refreshed.ShardsBuilt, refreshed.ShardsReused));
        Assert.True(allocated < padding / 2, $"Unchanged Refresh allocated {allocated:N0} bytes.");
    }

    [Theory]
    [InlineData("IndexedCorpusOccurrences", "CHECK (ByteOffset > 0)")]
    [InlineData("IndexedCorpusOccurrences", "CHECK (ByteLength > 0)")]
    [InlineData("IndexedCorpusShardWeights", "CHECK (Frequency > 0)")]
    [InlineData("IndexedCorpusGenerations", "CHECK (Published IN (0, 1))")]
    [InlineData("IndexedCorpusGenerations", "CHECK (ProjectionValid IN (0, 1))")]
    [InlineData("IndexedCorpusWeights", "CHECK (Frequency > 0)")]
    [InlineData("IndexedCorpusDocuments", ",\n    UNIQUE (Generation, DocumentId)", "", "sqlite_autoindex_IndexedCorpusDocuments_2")]
    [InlineData("IndexedCorpusForms", "FormKey TEXT NOT NULL UNIQUE", "FormKey TEXT NOT NULL", "sqlite_autoindex_IndexedCorpusForms_1")]
    public void OpeningCorpusStoreRejectsEachMissingInvariant(string table, string invariant, string replacement = "",
        string uniqueIndex = "")
    {
        ChangeStore("""
            PRAGMA writable_schema = ON;
            UPDATE sqlite_schema SET sql=replace(sql,$invariant,$replacement)
            WHERE name=$table AND instr(sql,$invariant)>0;
            DELETE FROM sqlite_schema WHERE tbl_name=$table AND name=$index;
            PRAGMA writable_schema = OFF;
            """, ("$table", table), ("$invariant", invariant), ("$replacement", replacement), ("$index", uniqueIndex));

        var error = Assert.Throws<InvalidDataException>(() =>
            CorpusSourceIndex.OpenExisting(Database.FullPath, Database.Project).Dispose());
        Assert.Equal($"Motif table {table} is missing a required invariant.", error.Message);
    }

    [Theory]
    [InlineData(false, 99L)]
    [InlineData(false, (long)CorpusFreshness.Historical)]
    [InlineData(false, 4294967296L)]
    [InlineData(true, 2L)]
    [InlineData(true, 4294967297L)]
    public void InvalidStoredStatusRefusesPinsAndRebuildReplacesTheHead(bool projectionFlag, long value)
    {
        var path = Path.Combine(SourceFolder(), "a.txt");
        File.WriteAllText(path, "one");
        var source = Declaration(CorpusSourceFormat.PlainText, path);
        Index.Refresh(source, _tokeniser);
        var pin = Index.Pin(source.SourceId)!;
        ChangeStore(projectionFlag
            ? """
                PRAGMA ignore_check_constraints=ON;
                UPDATE IndexedCorpusGenerations SET ProjectionValid=$value;
                PRAGMA ignore_check_constraints=OFF;
                """
            : "UPDATE IndexedCorpusHeads SET Freshness=$value;", ("$value", value));

        var currentFailure = Xunit.Record.Exception(() => Index.Pin(source.SourceId));
        var retainedFailure = Xunit.Record.Exception(() => pin.Occurrences("a.txt"));
        Assert.Equal(CorpusSourceFailure.InvalidProjection, Assert.IsType<CorpusSourceRefusal>(currentFailure).Reason);
        Assert.Equal(CorpusSourceFailure.InvalidProjection, Assert.IsType<CorpusSourceRefusal>(retainedFailure).Reason);

        var rebuilt = Index.Rebuild(source, _tokeniser);

        Assert.NotEqual(pin.Stamp, rebuilt.Stamp);
        Assert.Equal(rebuilt.Stamp, Index.Pin(source.SourceId)!.Stamp);
        Assert.Equal(CorpusFreshness.Verified, Index.Verify(source).Freshness);
        Assert.Equal("one", Assert.Single(Index.Pin(source.SourceId)!.Occurrences("a.txt").Items).Surface);
        Assert.Equal(CorpusFreshness.InvalidProjection, Index.Pin(source.SourceId, pin.Stamp.Id)!.Freshness);
        Assert.Equal(CorpusSourceFailure.InvalidProjection,
            Assert.Throws<CorpusSourceRefusal>(() => pin.Occurrences("a.txt")).Reason);
    }

    // Share-read-write open: Windows blocks an exclusive read while a live connection holds the store.
    private static byte[] ReadSharedBytes(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }
        catch (FileNotFoundException)
        {
            return []; // a sidecar file the last connection close deleted after it was listed
        }
    }

    [Fact]
    public void OpeningAndPagingDoesNotWriteTheMainDatabaseOrWal()
    {
        var path = Path.Combine(SourceFolder(), "a.txt");
        File.WriteAllText(path, "one two");
        var source = Declaration(CorpusSourceFormat.PlainText, path);
        Index.Refresh(source, _tokeniser);
        using var activeConnection = Database.OpenConnection();
        var before = StoreBytes();

        using (var readOnly = CorpusSourceIndex.OpenExisting(Database.FullPath, Database.Project))
        {
            var pin = SourceProviders.Corpus.Open(readOnly).Pin(source.SourceId)!;
            Assert.Single(pin.Documents().Items);
            Assert.Equal(2, pin.Occurrences("a.txt").Items.Count);
            Assert.Equal(2, pin.ResolveSelection().Stream(default).Count());
        }

        Assert.Equal(before, StoreBytes());

        string?[] StoreBytes() => new[] { Database.FullPath, Database.FullPath + "-wal" }
            .Select(file => File.Exists(file) ? CorpusContent.Hash(ReadSharedBytes(file)) : null).ToArray();
    }

    [Fact]
    public void CommittedStagingBatchesStayInvisibleOnFaultCancellationOrSourceRace()
    {
        var path = Path.Combine(SourceFolder(), "a.txt");
        File.WriteAllText(path, "old place");
        var source = Declaration(CorpusSourceFormat.PlainText, path);
        var index = Index;
        var before = index.Refresh(source, _tokeniser);

        File.WriteAllText(path, string.Join(" ", Enumerable.Repeat("new", 5000)));
        index.BeforePublish = () =>
        {
            Assert.Equal(before.Stamp, Index.Pin(source.SourceId)!.Stamp);
            throw new IOException("injected stage fault");
        };
        Assert.Equal("injected stage fault", Assert.Throws<IOException>(() => index.Refresh(source, _tokeniser)).Message);
        Assert.Equal(0L, Stages());
        using var cancel = new CancellationTokenSource();
        index.BeforePublish = cancel.Cancel;
        Assert.Throws<OperationCanceledException>(() => index.Refresh(source, _tokeniser, cancel.Token));
        Assert.Equal(0L, Stages());
        Assert.Equal(CorpusFreshness.Changed, Index.Pin(source.SourceId)!.Freshness);

        index.BeforePublish = () => File.WriteAllText(path, "another version");
        Assert.ThrowsAny<IOException>(() => index.Refresh(source, _tokeniser));
        Assert.Equal(0L, Stages());
        Assert.Equal(CorpusFreshness.Changed, Index.Pin(source.SourceId)!.Freshness);
        Assert.Equal(2L, Index.Refresh(source, _tokeniser).Tokens);
        Assert.Equal("old", Index.Pin(source.SourceId, before.Stamp.Id)!.Occurrences("a.txt").Items[0].Surface);
    }

    [Theory]
    [InlineData("one")]
    [InlineData("one two")]
    public void VerifyDetectsCorruptProjectionAndRebuildPublishesNewShards(string text)
    {
        var path = Path.Combine(SourceFolder(), "a.txt");
        File.WriteAllText(path, text);
        var source = Declaration(CorpusSourceFormat.PlainText, path);
        var first = Index.Refresh(source, _tokeniser);
        var pin = Index.Pin(source.SourceId)!;
        var selection = pin.ResolveSelection();
        using var retained = selection.Stream(default).GetEnumerator();
        Assert.True(retained.MoveNext());
        using (var connection = Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE IndexedCorpusWeights SET Frequency=Frequency+1;";
            command.ExecuteNonQuery();
        }
        Assert.Equal(CorpusFreshness.InvalidProjection, Index.Verify(source).Freshness);
        Assert.Equal(CorpusSourceFailure.InvalidProjection, Assert.Throws<CorpusSourceRefusal>(() =>
            Index.Pin(source.SourceId)!.ResolveSelection()).Reason);
        Assert.Equal(CorpusSourceFailure.InvalidProjection, Assert.Throws<CorpusSourceRefusal>(() =>
            pin.ResolveSelection()).Reason);
        Assert.Equal(CorpusSourceFailure.InvalidProjection, Assert.Throws<CorpusSourceRefusal>(() =>
            selection.Stream(default).ToArray()).Reason);
        Assert.Equal(CorpusSourceFailure.InvalidProjection, Assert.Throws<CorpusSourceRefusal>(() =>
            retained.MoveNext()).Reason);
        Assert.Equal(CorpusSourceFailure.InvalidProjection, Assert.Throws<CorpusSourceRefusal>(() =>
            pin.Documents()).Reason);
        Assert.Equal(CorpusSourceFailure.InvalidProjection, Assert.Throws<CorpusSourceRefusal>(() =>
            pin.Occurrences("a.txt")).Reason);
        var rebuilt = Index.Rebuild(source, _tokeniser);
        Assert.Equal(1, rebuilt.ShardsBuilt);
        Assert.NotEqual(first.Stamp.Id, rebuilt.Stamp.Id);
        Assert.Equal(CorpusFreshness.InvalidProjection, Index.Pin(source.SourceId, first.Stamp.Id)!.Freshness);
        Assert.Equal(CorpusFreshness.Verified, Index.Verify(source).Freshness);
    }

    [Theory]
    [InlineData("UPDATE IndexedCorpusShardWeights SET Frequency=Frequency+1 WHERE ShardId=$shard;")]
    [InlineData("UPDATE IndexedCorpusOccurrences SET Surface='altered' WHERE ShardId=$shard;")]
    [InlineData("""
        UPDATE IndexedCorpusForms SET FormNfd='altered' WHERE Id IN
            (SELECT FormId FROM IndexedCorpusShardWeights WHERE ShardId=$shard);
        """)]
    public void VerifyInvalidatesHistoricalSelectionsThatShareCorruptProjection(string corruption)
    {
        var folder = SourceFolder();
        var first = Path.Combine(folder, "a.txt");
        var second = Path.Combine(folder, "b.txt");
        File.WriteAllText(first, "independent");
        File.WriteAllText(second, "initial");
        var source = Declaration(CorpusSourceFormat.PlainText, folder);
        var index = Index;
        var independent = index.Refresh(source, _tokeniser).Stamp;
        File.WriteAllText(first, "one one two");
        var old = index.Refresh(source, _tokeniser).Stamp;
        var pin = index.Pin(source.SourceId, old.Id)!;
        var selection = pin.ResolveSelection(["a.txt"]);
        using var retained = selection.Stream(default).GetEnumerator();
        Assert.True(retained.MoveNext());

        File.WriteAllText(second, "replacement");
        var refreshed = index.Refresh(source, _tokeniser);
        Assert.Equal(1, refreshed.ShardsReused);
        var shard = pin.Documents().Items.Single(row => row.Id == "a.txt").ShardId;
        using (var connection = Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = corruption;
            command.Parameters.AddWithValue("$shard", shard);
            command.ExecuteNonQuery();
        }

        Assert.Equal(CorpusFreshness.InvalidProjection, index.Verify(source).Freshness);
        Assert.Equal(CorpusFreshness.Historical, index.Pin(source.SourceId, independent.Id)!.Freshness);
        Assert.Equal(CorpusSourceFailure.InvalidProjection, Assert.Throws<CorpusSourceRefusal>(() =>
            pin.ResolveSelection(["a.txt"], expectedMembershipDigest: selection.Scope.MembershipDigest)).Reason);
        Assert.Equal(CorpusSourceFailure.InvalidProjection, Assert.Throws<CorpusSourceRefusal>(() =>
            retained.MoveNext()).Reason);
    }

    [Fact]
    public void VerifyKeepsHistoricalSelectionsWhenOnlyCurrentGenerationWeightsAreCorrupt()
    {
        var folder = SourceFolder();
        File.WriteAllText(Path.Combine(folder, "a.txt"), "one one");
        var second = Path.Combine(folder, "b.txt");
        File.WriteAllText(second, "initial");
        var source = Declaration(CorpusSourceFormat.PlainText, folder);
        var index = Index;
        var historical = index.Refresh(source, _tokeniser).Stamp;
        var pin = index.Pin(source.SourceId, historical.Id)!;
        var selection = pin.ResolveSelection(["a.txt"]);
        File.WriteAllText(second, "replacement");
        var current = index.Refresh(source, _tokeniser).Stamp;
        using (var connection = Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "UPDATE IndexedCorpusWeights SET Frequency=Frequency+1 WHERE Generation=$id;";
            command.Parameters.AddWithValue("$id", current.Id);
            command.ExecuteNonQuery();
        }

        Assert.Equal(CorpusFreshness.InvalidProjection, index.Verify(source).Freshness);
        Assert.Equal(2L, pin.ResolveSelection(["a.txt"], expectedMembershipDigest: selection.Scope.MembershipDigest)
            .Stream(default).Sum(row => row.SelectedWeight));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VerifyRejectsReassignedEqualWeightDocumentShards(bool swap)
    {
        var folder = SourceFolder();
        File.WriteAllText(Path.Combine(folder, "a.txt"), "one");
        File.WriteAllText(Path.Combine(folder, "b.txt"), " one");
        var source = Declaration(CorpusSourceFormat.PlainText, folder);
        var index = Index;
        index.Refresh(source, _tokeniser);
        var pin = index.Pin(source.SourceId)!;
        var documents = pin.Documents().Items;
        Assert.Equal(new long?[] { 1, 2 }, documents.Select(document =>
            Assert.Single(pin.Occurrences(document.Id).Items).Key.Anchor.Offset));
        Assert.Equal(swap ? 2 : 1, ChangeStore("""
            UPDATE IndexedCorpusDocuments SET ShardId=CASE DocumentId WHEN 'b.txt' THEN $a ELSE $b END
            WHERE DocumentId='b.txt' OR $swap;
            """, ("$a", documents[0].ShardId), ("$b", documents[1].ShardId), ("$swap", swap)));

        Assert.Equal(CorpusFreshness.InvalidProjection, index.Verify(source).Freshness);
        Assert.Equal(CorpusSourceFailure.InvalidProjection,
            Assert.Throws<CorpusSourceRefusal>(() => pin.Occurrences("b.txt")).Reason);
    }

    [Fact]
    public void RefreshRepairsWeightsAndInvalidatesTheCorruptBaseGeneration()
    {
        var folder = SourceFolder();
        File.WriteAllText(Path.Combine(folder, "a.txt"), "one one");
        var second = Path.Combine(folder, "b.txt");
        File.WriteAllText(second, "initial");
        var source = Declaration(CorpusSourceFormat.PlainText, folder);
        var index = Index;
        var before = index.Refresh(source, _tokeniser).Stamp;
        var pin = index.Pin(source.SourceId, before.Id)!;
        var selection = pin.ResolveSelection();
        Assert.Equal(1, ChangeStore("""
            UPDATE IndexedCorpusWeights SET Frequency=Frequency+1 WHERE Generation=$id
            AND FormId=(SELECT Id FROM IndexedCorpusForms WHERE FormNfd='one');
            """, ("$id", before.Id)));
        File.WriteAllText(second, "two two two");

        var after = index.Refresh(source, _tokeniser);
        Assert.Equal(5L, after.Tokens);
        Assert.Equal(new[] { ("one", 2L), ("two", 3L) }, index.Pin(source.SourceId)!.ResolveSelection().Stream(default)
            .Select(row => (row.Wordform!.FormNfd, row.SourceFrequency)).OrderBy(row => row.FormNfd, StringComparer.Ordinal));
        Assert.Equal(CorpusSourceFailure.InvalidProjection, Assert.Throws<CorpusSourceRefusal>(() =>
            pin.ResolveSelection(expectedMembershipDigest: selection.Scope.MembershipDigest)).Reason);
        Assert.Equal(CorpusFreshness.Verified, index.Verify(source).Freshness);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RebuildRepairsCorruptPublicFormWithoutRestoringDependentGenerations(bool verifyFirst)
    {
        var folder = SourceFolder();
        File.WriteAllText(Path.Combine(folder, "a.txt"), "one one");
        var second = Path.Combine(folder, "b.txt");
        File.WriteAllText(second, "initial");
        var source = Declaration(CorpusSourceFormat.PlainText, folder);
        var index = Index;
        var before = index.Refresh(source, _tokeniser).Stamp;
        var retained = index.Pin(source.SourceId, before.Id)!;
        File.WriteAllText(second, "replacement");
        var current = index.Refresh(source, _tokeniser).Stamp;
        Assert.Equal(1, ChangeStore("UPDATE IndexedCorpusForms SET FormNfd='altered' WHERE FormNfd='one';"));
        if (verifyFirst) Assert.Equal(CorpusFreshness.InvalidProjection, index.Verify(source).Freshness);

        var rebuilt = index.Rebuild(source, _tokeniser);
        Assert.Equal(2, rebuilt.ShardsBuilt);
        Assert.Equal(CorpusFreshness.Verified, index.Verify(source).Freshness);
        Assert.All(index.Pin(source.SourceId)!.Occurrences("a.txt").Items, row => Assert.Equal("one", row.Surface));
        Assert.Equal(CorpusFreshness.InvalidProjection, index.Pin(source.SourceId, current.Id)!.Freshness);
        Assert.Equal(CorpusSourceFailure.InvalidProjection,
            Assert.Throws<CorpusSourceRefusal>(() => retained.Occurrences("a.txt")).Reason);
    }

    [Fact]
    public void RebuildRefusesAnIncompatibleKeyWithoutEvidenceOfPublicFormCorruption()
    {
        var path = Path.Combine(SourceFolder(), "a.txt");
        File.WriteAllText(path, "one");
        var source = Declaration(CorpusSourceFormat.PlainText, path);
        var index = Index;
        index.Refresh(source, _tokeniser);
        Assert.Equal(1, ChangeStore("UPDATE IndexedCorpusForms SET FormNfd=NULL;"));

        Assert.Contains("key collision", Assert.Throws<InvalidDataException>(() => index.Rebuild(source, _tokeniser)).Message);
        Assert.Equal(0L, Stages());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectionRefusesKnownStaleInputsOnNewAndExistingPins(bool unavailable)
    {
        var path = Path.Combine(SourceFolder(), "a.txt");
        File.WriteAllText(path, "one two");
        var source = Declaration(CorpusSourceFormat.PlainText, path);
        Index.Refresh(source, _tokeniser);
        var pin = Index.Pin(source.SourceId)!;
        var selection = pin.ResolveSelection();
        if (unavailable) File.Delete(path);
        else File.WriteAllText(path, "another word");
        Assert.Equal(unavailable ? CorpusFreshness.Unavailable : CorpusFreshness.Changed, Index.Verify(source).Freshness);

        var expected = unavailable ? CorpusSourceFailure.SourceUnavailable : CorpusSourceFailure.SourceChanged;
        Assert.Equal(expected, Assert.Throws<CorpusSourceRefusal>(() => Index.Pin(source.SourceId)!.ResolveSelection()).Reason);
        Assert.Equal(expected, Assert.Throws<CorpusSourceRefusal>(() => pin.ResolveSelection()).Reason);
        Assert.Equal(expected, Assert.Throws<CorpusSourceRefusal>(() => selection.Stream(default).ToArray()).Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AbsentOccurrenceQueryRefusesAnAlreadyOpenPinAfterVerify(bool unavailable)
    {
        var path = Path.Combine(SourceFolder(), "a.txt");
        File.WriteAllText(path, "one two");
        var source = Declaration(CorpusSourceFormat.PlainText, path);
        var index = Index;
        index.Refresh(source, _tokeniser);
        var pin = index.Pin(source.SourceId)!;
        Assert.Empty(pin.Occurrences("a.txt", "absent-key").Items);
        if (unavailable) File.Delete(path);
        else File.WriteAllText(path, "another word");
        Assert.Equal(unavailable ? CorpusFreshness.Unavailable : CorpusFreshness.Changed, index.Verify(source).Freshness);

        var expected = unavailable ? CorpusSourceFailure.SourceUnavailable : CorpusSourceFailure.SourceChanged;
        Assert.Equal(expected, Assert.Throws<CorpusSourceRefusal>(() => pin.Occurrences("a.txt", "absent-key")).Reason);
    }

    [Fact]
    public void DocumentSelectionUsesPublishedWeightsWithoutReadingPrimaryOccurrences()
    {
        var folder = SourceFolder();
        File.WriteAllText(Path.Combine(folder, "a.txt"), "one one two");
        File.WriteAllText(Path.Combine(folder, "b.txt"), "two three");
        var source = Declaration(CorpusSourceFormat.PlainText, folder);
        Index.Refresh(source, _tokeniser);
        var pin = Index.Pin(source.SourceId)!;
        using (var connection = Database.OpenConnection())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "DELETE FROM IndexedCorpusOccurrences;";
            command.ExecuteNonQuery();
        }

        var selected = pin.ResolveSelection(["a.txt"]);
        Assert.Equal((2L, 3L), (selected.Scope.TypeCount, selected.Scope.TokenCount));
        Assert.Equal(new[] { ("one", 2L, 2L), ("two", 2L, 1L) }, selected.Stream(default)
            .Select(row => (row.Wordform!.FormNfd, row.SourceFrequency, row.SelectedWeight)));
        Assert.Equal(pin.ResolveSelection().Stream(default), pin.ResolveSelection(["b.txt", "a.txt"]).Stream(default));
        Assert.Equal(CorpusFreshness.InvalidProjection, Index.Verify(source).Freshness);
    }

    [Fact]
    public void SikorHeldOutIdsAreSkippedBeforeAnyWordFieldsAndNeverAffectThePin()
    {
        var path = Path.Combine(SourceFolder(), "private.xml");
        const string selected = """<text id="chosen-0" gt_domain="news"><p><s><w word="SyntheticCanary"/><w word="!"/><w word="word pair"/></s></p></text>""";
        const string held = """<text id="held-1"><p><s><w word="HeldoutCanary"/><w/></s></p></text>""";
        const string excluded = """<text id="held-0"><p><s><w word="OffListCanary"/><w/></s></p></text>""";
        File.WriteAllText(path, "<corpus>" + selected + held + excluded + "</corpus>");
        var source = Declaration(CorpusSourceFormat.Sikor, path) with { Sikor = Split("chosen-0") };

        var before = Index.Refresh(source, _tokeniser);
        var pin = Index.Pin(source.SourceId)!;
        var document = Assert.Single(pin.Documents().Items);
        Assert.DoesNotContain("chosen-0", document.Id);
        var words = pin.Occurrences(document.Id).Items;
        Assert.Equal(new[] { (1, 1), (3, 1), (3, 2) },
            words.Select(word => (word.Key.Anchor.RawWordIndex!.Value, word.Key.Anchor.PartIndex!.Value)));
        Assert.All(words, word =>
        {
            Assert.Equal("token-index", word.Key.Anchor.Unit);
            Assert.Null(word.Surface);
            Assert.Null(word.Wordform);
        });
        Assert.Equal(CorpusSourceFailure.PrivateSelection,
            Assert.Throws<CorpusSourceRefusal>(() => pin.ResolveSelection()).Reason);
        Assert.Equal(CorpusSourceFailure.PrivateContext,
            Assert.Throws<CorpusSourceRefusal>(() => pin.ReadContext(source, words[0].Key)).Reason);
        foreach (var storeFile in Directory.EnumerateFiles(_root, "project.motif.db*"))
        {
            var store = Encoding.UTF8.GetString(ReadSharedBytes(storeFile));
            Assert.DoesNotContain("SyntheticCanary", store);
            Assert.DoesNotContain("HeldoutCanary", store);
            Assert.DoesNotContain("OffListCanary", store);
            Assert.DoesNotContain("chosen-0", store);
        }

        File.WriteAllText(path, "<corpus>" + selected + held.Replace("HeldoutCanary", "OtherCanary", StringComparison.Ordinal) +
            excluded + "</corpus>");
        Assert.Equal(CorpusFreshness.Verified, Index.Verify(source).Freshness);
        Assert.Equal(before.Stamp, Index.Refresh(source, _tokeniser).Stamp);
    }

    [Fact]
    public void CancellingSikorRefreshAfterAChangedDocumentRefusesCurrentOccurrences()
    {
        var path = Path.Combine(SourceFolder(), "private.xml");
        var source = Declaration(CorpusSourceFormat.Sikor, path) with { Sikor = Split("chosen-0") };
        File.WriteAllText(path, """<corpus><text id="chosen-0"><w word="old"/></text></corpus>""");
        var before = Index.Refresh(source, _tokeniser);
        var pin = Index.Pin(source.SourceId)!;
        var documentId = Assert.Single(pin.Documents().Items).Id;

        File.WriteAllText(path, """<corpus><text id="chosen-0"><w word="new"/></text></corpus>""");
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() =>
            Index.Refresh(source, new CallbackTokeniser(cancellation.Cancel), cancellation.Token));

        Assert.Equal(CorpusSourceFailure.SourceChanged,
            Assert.Throws<CorpusSourceRefusal>(() => pin.Occurrences(documentId)).Reason);
        var current = Index.Pin(source.SourceId)!;
        Assert.Equal(before.Stamp, current.Stamp);
        Assert.Equal(CorpusFreshness.Changed, current.Freshness);
        Assert.Equal(0L, Stages());
    }

    [Fact]
    public void VerifyRejectsReplacedPrivateFormKeyAcrossDependentGenerations()
    {
        var path = Path.Combine(SourceFolder(), "private.xml");
        var source = Declaration(CorpusSourceFormat.Sikor, path) with { Sikor = Split("first-1", "second-0") };
        void Write(string first, string second) => File.WriteAllText(path,
            $"""<corpus><text id="first-1">{first}</text><text id="second-0"><w word="{second}"/></text></corpus>""");
        const string first = """<w word="SyntheticCanary"/>""";

        var index = Index;
        Write(first.Replace("SyntheticCanary", "Independent", StringComparison.Ordinal), "initial");
        var independent = index.Refresh(source, _tokeniser).Stamp;
        Write(first + """<w word="different"/>""", "initial");
        var distinctShard = index.Refresh(source, _tokeniser).Stamp;
        var distinctPin = index.Pin(source.SourceId, distinctShard.Id)!;
        Write(first, "initial");
        var historical = index.Refresh(source, _tokeniser).Stamp;
        var pin = index.Pin(source.SourceId, historical.Id)!;
        var document = pin.Documents().Items[0];
        var occurrence = Assert.Single(pin.Occurrences(document.Id).Items);
        Assert.NotEqual(document.ShardId, distinctPin.Documents().Items[0].ShardId);
        Assert.Single(distinctPin.Occurrences(document.Id, occurrence.FormId).Items);
        Write(first, "replacement");
        var current = index.Refresh(source, _tokeniser);
        Assert.Equal(1, current.ShardsReused);
        Assert.Single(index.Pin(source.SourceId)!.Occurrences(occurrence.Key.Document.DocumentId, occurrence.FormId).Items);
        Assert.Equal(1, ChangeStore("UPDATE IndexedCorpusForms SET FormKey=$new WHERE FormKey=$old;",
            ("$new", new string('0', 64)), ("$old", occurrence.FormId)));

        Assert.Equal(CorpusFreshness.InvalidProjection, index.Verify(source).Freshness);
        Assert.Equal(CorpusFreshness.Historical, index.Pin(source.SourceId, independent.Id)!.Freshness);
        Assert.Equal(CorpusSourceFailure.InvalidProjection, Assert.Throws<CorpusSourceRefusal>(() =>
            pin.Occurrences(document.Id, occurrence.FormId)).Reason);
        Assert.Equal(CorpusSourceFailure.InvalidProjection, Assert.Throws<CorpusSourceRefusal>(() =>
            distinctPin.Occurrences(document.Id, occurrence.FormId)).Reason);
        Assert.Equal(CorpusSourceFailure.InvalidProjection, Assert.Throws<CorpusSourceRefusal>(() =>
            index.Pin(source.SourceId)!.Occurrences(document.Id, occurrence.FormId)).Reason);
        foreach (var storeFile in Directory.EnumerateFiles(_root, "project.motif.db*"))
            Assert.DoesNotContain("SyntheticCanary", Encoding.UTF8.GetString(ReadSharedBytes(storeFile)));
    }

    [Fact]
    public void SikorRefusesUnpinnedHeldOutDuplicateMissingAndBibleIds()
    {
        var path = Path.Combine(SourceFolder(), "private.xml");
        var source = Declaration(CorpusSourceFormat.Sikor, path) with { Sikor = Split("chosen-0") };
        File.WriteAllText(path, """<corpus><text id="chosen-0"><w word="synthetic"/></text></corpus>""");
        Assert.Throws<InvalidDataException>(() => Index.Refresh(source with { Sikor = Split("held-1") }, _tokeniser));
        Assert.Throws<InvalidDataException>(() => Index.Refresh(
            source with { Sikor = source.Sikor! with { DevIdsSha256 = new string('0', 64) } }, _tokeniser));

        foreach (var xml in new[]
        {
            """<corpus><text id="held-0"><w/></text></corpus>""",
            """<corpus><text id="chosen-0"/><text id="chosen-0"/></corpus>""",
            """<corpus><text/></corpus>""",
            """<corpus><text id="chosen-0" gt_domain="bible"><w word="synthetic"/></text></corpus>""",
            """<!DOCTYPE corpus [<!ENTITY injected "synthetic">]><corpus>&injected;</corpus>""",
        })
        {
            File.WriteAllText(path, xml);
            Assert.Throws<InvalidDataException>(() => Index.Refresh(source, _tokeniser));
            Assert.Null(Index.Pin(source.SourceId));
        }
    }

    [Fact]
    public void TokeniserMustMatchTheDeclaredBinding()
    {
        var path = Path.Combine(SourceFolder(), "a.txt");
        File.WriteAllText(path, "one two");
        var source = Declaration(CorpusSourceFormat.PlainText, path);
        var incompatible = source with { Segmentation = source.Segmentation with { TokeniserVersion = "other" } };
        Assert.Throws<InvalidDataException>(() => Index.Refresh(incompatible, _tokeniser));
        var unverifiedProfile = source with { Segmentation = source.Segmentation with { ProfileSha256 = new string('0', 64) } };
        Assert.Throws<InvalidDataException>(() => Index.Refresh(unverifiedProfile, _tokeniser));
        Assert.Throws<InvalidDataException>(() => Index.Refresh(source, new CallbackTokeniser(() => { }, proveProfile: false)));
        var ldml = Path.Combine(_root, "en.ldml");
        File.WriteAllText(ldml, """<ldml><identity><language type="en"/></identity></ldml>""");
        var profile = WritingSystemWordTokeniser.FromLdml(ldml, "en");
        Assert.Throws<InvalidDataException>(() => Index.Refresh(source with
        {
            Segmentation = new("en", new string('0', 64), profile.Name, profile.Version),
        }, profile));
        Assert.Null(Index.Pin(source.SourceId));
        Index.Refresh(source with
        {
            Segmentation = new(profile.WritingSystemId, profile.ProfileSha256!, profile.Name, profile.Version),
        }, profile);
    }

    private sealed class CallbackTokeniser(Action onFirstWord, bool proveProfile = true) : IWordTokeniser, IWordSpanTokeniser
    {
        private readonly WhitespaceAndPunctuationTokeniser _inner = new();
        private bool _called;

        public string Name => _inner.Name;
        public string Version => _inner.Version;
        public string Notes => _inner.Notes;
        public WordSpanProfile? Profile => proveProfile ? _inner.Profile : null;
        public IEnumerable<string> Tokenise(string text) => TokeniseSpans(text).Select(word => word.Surface);
        public IEnumerable<CorpusWordSpan> TokeniseSpans(string text)
        {
            if (!_called)
            {
                _called = true;
                onFirstWord();
            }
            return _inner.TokeniseSpans(text);
        }
    }

    private CorpusSourceDeclaration Declaration(CorpusSourceFormat format, string location) => new("fixture", format,
        location, new("en", _tokeniser.Profile.Sha256, _tokeniser.Name, _tokeniser.Version), "synthetic fixture");

    private string SourceFolder() => Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;

    private static string Record(string id, string text) => JsonSerializer.Serialize(new { id, text });

    private static CorpusSikor Split(params string[] ids) => new(ids,
        CorpusContent.Digest(ids.Order(StringComparer.Ordinal).ToArray()), Enumerable.Repeat((byte)13, 32).ToArray());

    private int ChangeStore(string sql, params (string Name, object Value)[] parameters)
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        return command.ExecuteNonQuery();
    }

    private long Stages()
    {
        using var connection = Database.OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM IndexedCorpusGenerations WHERE Published=0;";
        return (long)command.ExecuteScalar()!;
    }

    public void Dispose()
    {
        _database?.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
