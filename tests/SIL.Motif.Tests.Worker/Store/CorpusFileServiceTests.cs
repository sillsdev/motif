using System.Security.Cryptography;
using System.Text;
using SIL.Motif.Host.Corpus;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class CorpusFileServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-corpus-files", Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); GC.SuppressFinalize(this); }

    [Theory]
    [InlineData("bad\0name")]
    [InlineData("../escape")]
    public void InvalidDocumentNamesIdentifyTheirSidecar(string invalidName)
    {
        var provenance = new CorpusProvenance(new CorpusOrigin("source", null, DateTimeOffset.UnixEpoch, null),
            new TokenisationRecord("method", "1", "notes"));
        var document = new CorpusDocument("doc", "Title", new DocumentSource.File("source.txt"), "text",
            "hash", DateTimeOffset.UnixEpoch);
        var service = new CorpusFileService(_root);
        service.Export(StoredCorpus.Create("corpus", provenance).With(document));
        var path = Directory.GetFiles(_root, "corpus.json", SearchOption.AllDirectories).Single();
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        json["Documents"]![0]!["RelativePath"] = invalidName;
        File.WriteAllText(path, json.ToJsonString());
        var error = Assert.Throws<InvalidDataException>(() => service.ReadSidecar("corpus"));
        Assert.Contains(path, error.Message);
        Assert.Contains("corpus", error.Message);
    }

    [Fact]
    public void IncompleteJournalBeforeDisplacementDoesNotHideTheSidecar()
    {
        var provenance = new CorpusProvenance(new CorpusOrigin("source", null, DateTimeOffset.UnixEpoch, null),
            new TokenisationRecord("method", "1", "notes"));
        var service = new CorpusFileService(_root);
        service.Export(StoredCorpus.Create("corcorpus", provenance));
        var path = Directory.GetFiles(_root, "corpus.json", SearchOption.AllDirectories).Single();
        var journal = path + "." + Guid.NewGuid().ToString("N") + ".pending";
        File.WriteAllText(journal, "{");
        Assert.True(service.Exists("corcorpus"));
        Assert.False(File.Exists(journal));
        Assert.Equal("corcorpus", service.ReadSidecar("corcorpus").CorpusId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InterruptedReplacementRecoversOnReadAndReindex(bool afterPublication)
    {
        var provenance = new CorpusProvenance(new CorpusOrigin("source", null, DateTimeOffset.UnixEpoch, null),
            new TokenisationRecord("method", "1", "notes"));
        var document = new CorpusDocument("doc", "Original", new DocumentSource.File("source.txt"), "text",
            "hash", DateTimeOffset.UnixEpoch);
        var service = new CorpusFileService(_root);
        service.Export(StoredCorpus.Create("corpus", provenance).With(document));
        var snapshot = service.ReadPublished("corpus");
        var interruptedRoot = Path.Combine(_root, "interrupted");
        void Capture(string journal, byte[] journalBytes)
        {
            var corpusDirectory = Path.GetDirectoryName(journal)!;
            var target = Path.Combine(interruptedRoot, Path.GetRelativePath(_root, corpusDirectory));
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(corpusDirectory).Where(f => f != journal))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            File.WriteAllBytes(Path.Combine(target, Path.GetFileName(journal)), journalBytes);
        }
        if (afterPublication) service.AfterSidecarPublication = Capture;
        else service.AfterSidecarDisplacement = Capture;
        service.Export(snapshot with { Documents = new[] { document with { Title = "Changed" } } });
        var resumed = new CorpusFileService(interruptedRoot);
        Assert.True(resumed.Exists("corpus"));
        Assert.Equal(afterPublication ? "Changed" : "Original", resumed.ReadPublished("corpus").Documents.Single().Title);
        Assert.Equal(new[] { "corpus" }, resumed.ListPublished());
        Assert.Empty(Directory.GetFiles(interruptedRoot, "*.pending", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.GetFiles(interruptedRoot, "*.backup", SearchOption.AllDirectories));
        var retry = resumed.ReadPublished("corpus");
        resumed.Export(retry with { Documents = new[] { document with { Title = "Changed" } } });
        Assert.Equal("Changed", resumed.ReadSidecar("corpus").Documents.Single().Title);
    }

    [Fact]
    public void MissingSidecarAfterPublicationIsNotAutomaticallyRestored()
    {
        var provenance = new CorpusProvenance(new CorpusOrigin("source", null, DateTimeOffset.UnixEpoch, null),
            new TokenisationRecord("method", "1", "notes"));
        var service = new CorpusFileService(_root);
        service.Export(StoredCorpus.Create("corpus", provenance));
        var snapshot = service.ReadPublished("corpus");
        var interruptedRoot = Path.Combine(_root, "interrupted");
        service.AfterSidecarPublication = (journal, bytes) =>
        {
            var directory = Path.GetDirectoryName(journal)!;
            var target = Path.Combine(interruptedRoot, Path.GetRelativePath(_root, directory));
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(directory).Where(f => f != journal))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            File.WriteAllBytes(Path.Combine(target, Path.GetFileName(journal)), bytes);
            File.Delete(Path.Combine(target, "corpus.json"));
        };
        service.Export(snapshot with { Provenance = provenance with { Tokenisation = new TokenisationRecord("changed", "1", "") } });
        var resumed = new CorpusFileService(interruptedRoot);
        Assert.Throws<IOException>(() => resumed.Exists("corpus"));
        Assert.Empty(Directory.GetFiles(interruptedRoot, "corpus.json", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.GetFiles(interruptedRoot, "*.backup", SearchOption.AllDirectories));
    }

    [Fact]
    public void InterruptedPublicationRefusesCompetingSidecarAndKeepsBothVersions()
    {
        var provenance = new CorpusProvenance(new CorpusOrigin("source", null, DateTimeOffset.UnixEpoch, null),
            new TokenisationRecord("method", "1", "notes"));
        var service = new CorpusFileService(_root);
        service.Export(StoredCorpus.Create("corpus", provenance));
        var snapshot = service.ReadPublished("corpus");
        var interruptedRoot = Path.Combine(_root, "interrupted");
        service.AfterSidecarDisplacement = (journal, bytes) =>
        {
            var directory = Path.GetDirectoryName(journal)!;
            var target = Path.Combine(interruptedRoot, Path.GetRelativePath(_root, directory));
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(directory).Where(f => f != journal))
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            File.WriteAllBytes(Path.Combine(target, Path.GetFileName(journal)), bytes);
            File.WriteAllText(Path.Combine(target, "corpus.json"), "external edit");
        };
        service.Export(snapshot with { Provenance = provenance with { Tokenisation = new TokenisationRecord("changed", "1", "") } });
        var resumed = new CorpusFileService(interruptedRoot);
        Assert.Throws<IOException>(() => resumed.Exists("corpus"));
        Assert.Equal("external edit", File.ReadAllText(Directory.GetFiles(interruptedRoot, "corpus.json", SearchOption.AllDirectories).Single()));
        Assert.NotEmpty(Directory.GetFiles(interruptedRoot, "*.pending", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.GetFiles(interruptedRoot, "*.backup", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"CorpusId\":\"corpus\",\"Provenance\":null,\"Documents\":[]}")]
    public void InvalidSidecarsAreControlledDataErrors(string invalid)
    {
        var provenance = new CorpusProvenance(new CorpusOrigin("source", null, DateTimeOffset.UnixEpoch, null),
            new TokenisationRecord("method", "1", "notes"));
        var service = new CorpusFileService(_root);
        service.Export(StoredCorpus.Create("corpus", provenance));
        var path = Directory.GetFiles(_root, "corpus.json", SearchOption.AllDirectories).Single();
        File.WriteAllText(path, invalid);
        Assert.Contains(path, Assert.Throws<InvalidDataException>(() => service.ReadSidecar("corpus")).Message);
        Assert.Throws<InvalidDataException>(() => service.ReadPublished("corpus"));
        Assert.Throws<InvalidDataException>(() => service.ListPublished());
        Assert.Equal(invalid, File.ReadAllText(path));
    }

    [Fact]
    public void ExportRefusesAnExternalSidecarReplacementDuringPublication()
    {
        var provenance = new CorpusProvenance(new CorpusOrigin("source", null, DateTimeOffset.UnixEpoch, null),
            new TokenisationRecord("method", "1", "notes"));
        var document = new CorpusDocument("doc", "Title", new DocumentSource.File("source.txt"), "text",
            "hash", DateTimeOffset.UnixEpoch);
        var service = new CorpusFileService(_root);
        service.Export(StoredCorpus.Create("corpus", provenance).With(document));
        var snapshot = service.ReadPublished("corpus");
        var sidecar = Directory.GetFiles(_root, "corpus.json", SearchOption.AllDirectories).Single();
        service.BeforeSidecarReplacement = () =>
        {
            var external = File.ReadAllText(sidecar).Replace("\"Title\": \"Title\"", "\"Title\": \"Owner Edit\"");
            var temp = sidecar + ".external";
            File.WriteAllText(temp, external);
            File.Move(temp, sidecar, true);
        };
        Assert.Throws<IOException>(() => service.Export(snapshot with { Documents = new[] { document with { Title = "Changed" } } }));
        Assert.Equal("Owner Edit", service.ReadSidecar("corpus").Documents.Single().Title);
        Assert.Contains(Directory.GetFiles(Path.GetDirectoryName(sidecar)!, "*.backup"),
            path => File.ReadAllText(path).Contains("Owner Edit"));
    }

    [Fact]
    public void ViewerRanksNormalizedWordformsAndKeepsOriginalContexts()
    {
        var provenance = new CorpusProvenance(new CorpusOrigin("source", null, DateTimeOffset.UnixEpoch, null),
            new TokenisationRecord("whitespace-and-punctuation", "1", "notes"));
        var document = new CorpusDocument("doc", "Title", new DocumentSource.File("source.txt"), "é e\u0301 other",
            "source", DateTimeOffset.UnixEpoch);
        var files = new CorpusFileService(_root);
        files.Export(StoredCorpus.Create("corpus", provenance).With(document));
        ICorpusViewerService viewer = new CorpusViewerService(files, new WhitespaceAndPunctuationTokeniser());
        var result = viewer.Open("corpus", "doc");
        Assert.Contains("not CEB-compatible", result.BoundaryDescription);
        Assert.Equal("e\u0301", result.Wordforms[0].Wordform);
        Assert.Equal(2, result.Wordforms[0].Frequency);
        Assert.Equal(new[] { "é", "e\u0301" }, result.Wordforms[0].Occurrences.Select(o => o.Surface));
        Assert.Equal("é", Encoding.UTF8.GetString(viewer.ReadRange("corpus", "doc", result.Sha256, 0, 2)));
    }

    [Fact]
    public void ExportCanResumeAndRangeReadsRejectChangedBytes()
    {
        var provenance = new CorpusProvenance(new CorpusOrigin("source", null, DateTimeOffset.UnixEpoch, null),
            new TokenisationRecord("method", "1", "notes"));
        var document = new CorpusDocument("doc", "Title", new DocumentSource.File("source.txt"), "aé😀z",
            "original-source-hash", DateTimeOffset.UnixEpoch);
        var corpus = StoredCorpus.Create("corpus", provenance).With(document);
        var service = new CorpusFileService(_root);
        var sidecar = service.Export(corpus);
        Assert.Equal(sidecar, service.ReadSidecar("corpus") with { Documents = sidecar.Documents });
        Assert.Equal("original-source-hash", sidecar.Documents[0].SourceSha256);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("aé😀z"))).ToLowerInvariant(),
            sidecar.Documents[0].TextSha256);
        service.Export(corpus);
        Assert.Equal("é😀", Encoding.UTF8.GetString(service.ReadRange("corpus", "doc", sidecar.Documents[0].TextSha256, 1, 6)));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.ReadRange("corpus", "doc", sidecar.Documents[0].TextSha256, 0, 1024 * 1024 + 1));
        var path = Directory.GetFiles(_root, "*.txt", SearchOption.AllDirectories).Single();
        File.WriteAllText(path, "changed");
        Assert.Throws<InvalidDataException>(() => service.ReadRange("corpus", "doc", sidecar.Documents[0].TextSha256, 0, 1));
        Assert.Throws<IOException>(() => service.Export(corpus));
        Assert.Equal("changed", File.ReadAllText(path));
    }
}
