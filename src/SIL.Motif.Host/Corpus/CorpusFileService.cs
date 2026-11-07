using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SIL.Motif.Host.Corpus;

/// <summary>Published source metadata for a Document, independent of any analysis.</summary>
public sealed record CorpusFileDocument(
    string DocumentId, string Title, string RelativePath, string Source, string SourceSha256,
    string TextSha256, DateTimeOffset IngestedUtc, string? Licence,
    LicenceCapabilities? Capabilities, IReadOnlyDictionary<string, string>? Attributes);

/// <summary>The authoritative metadata and ordered file identities of a Corpus.</summary>
public sealed record CorpusFileSidecar(string CorpusId, CorpusProvenance Provenance,
    IReadOnlyList<CorpusFileDocument> Documents);

/// <summary>Publishes Corpus files and serves ranges bound to the bytes' SHA-256.</summary>
public sealed class CorpusFileService
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _root;
    internal Action? BeforeSidecarReplacement { get; set; }
    internal Action<string, byte[]>? AfterSidecarDisplacement { get; set; }
    internal Action<string, byte[]>? AfterSidecarPublication { get; set; }

    /// <param name="projectDirectory">The directory containing the FieldWorks project.</param>
    public CorpusFileService(string projectDirectory) => _root = Path.GetFullPath(projectDirectory);

    /// <summary>Publishes a snapshot without replacing an existing Document or sidecar.</summary>
    public CorpusFileSidecar Export(StoredCorpus corpus, bool requireMatchingSidecar = false)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        var directory = CorpusDirectory(corpus.CorpusId);
        Directory.CreateDirectory(directory);
        var sidecarPath = Path.Combine(directory, "corpus.json");
        RecoverPending(sidecarPath);
        var previousBytes = File.Exists(sidecarPath) ? File.ReadAllBytes(sidecarPath) : null;
        if (corpus.PublicationRevision is not null &&
            (previousBytes is null || corpus.PublicationRevision != Hash(previousBytes)))
            throw new IOException("The Corpus sidecar changed while the Corpus was being updated.");
        var previous = previousBytes is null ? null : ParseSidecar(corpus.CorpusId, sidecarPath, previousBytes);
        var documents = new List<CorpusFileDocument>();
        foreach (var document in corpus.Documents)
        {
            var bytes = Utf8.GetBytes(document.Text);
            var hash = Hash(bytes);
            var old = previous?.Documents.SingleOrDefault(d => d.DocumentId == document.DocumentId);
            var name = old?.RelativePath ?? Identity(document.DocumentId) + ".txt";
            Publish(DocumentPath(corpus.CorpusId, name), bytes);
            documents.Add(new CorpusFileDocument(document.DocumentId, document.Title, name,
                document.Source.Describe(), document.ContentSha256, hash, document.IngestedUtc,
                document.Licence, document.Capabilities, document.Attributes));
        }
        var sidecar = new CorpusFileSidecar(corpus.CorpusId, corpus.Provenance, documents);
        var sidecarBytes = JsonSerializer.SerializeToUtf8Bytes(sidecar, JsonOptions);
        if (previous is null) Publish(sidecarPath, sidecarBytes);
        else if (requireMatchingSidecar && !previousBytes!.AsSpan().SequenceEqual(sidecarBytes))
            throw new IOException("The existing Corpus sidecar conflicts with the legacy database content.");
        else if (!previousBytes!.AsSpan().SequenceEqual(sidecarBytes))
        {
            BeforeSidecarReplacement?.Invoke();
            Replace(sidecarPath, previousBytes!, sidecarBytes);
        }
        return sidecar;
    }

    /// <summary>Whether a published sidecar exists for this Corpus identity.</summary>
    public bool Exists(string corpusId)
    {
        var path = Path.Combine(CorpusDirectory(corpusId), "corpus.json");
        RecoverPending(path);
        return File.Exists(path);
    }

    /// <summary>Reads a sidecar, retaining the identities and metadata supplied at publication.</summary>
    public CorpusFileSidecar ReadSidecar(string corpusId)
    {
        var path = Path.Combine(CorpusDirectory(corpusId), "corpus.json");
        RecoverPending(path);
        return ParseSidecar(corpusId, path, File.ReadAllBytes(path));
    }

    private static CorpusFileSidecar ParseSidecar(string? corpusId, string path, byte[] bytes)
    {
        try
        {
            using var json = JsonDocument.Parse(bytes);
            var root = json.RootElement;
            Require(root, "CorpusId", JsonValueKind.String);
            var provenance = Require(root, "Provenance", JsonValueKind.Object);
            var origin = Require(provenance, "Origin", JsonValueKind.Object);
            Require(origin, "Description", JsonValueKind.String);
            Require(origin, "RetrievedUtc", JsonValueKind.String);
            ValidateCapabilities(origin);
            if (provenance.TryGetProperty("Qualification", out var qualification) && qualification.ValueKind != JsonValueKind.Null)
            {
                RequireBoolean(qualification, "KnownClean");
                RequireBoolean(qualification, "InScope");
                foreach (var name in new[] { "Attestor", "AttestedUtc", "Note" }) Require(qualification, name, JsonValueKind.String);
            }
            var tokenisation = Require(provenance, "Tokenisation", JsonValueKind.Object);
            foreach (var name in new[] { "Method", "Version", "Notes" }) Require(tokenisation, name, JsonValueKind.String);
            var documents = Require(root, "Documents", JsonValueKind.Array);
            foreach (var document in documents.EnumerateArray())
            {
                foreach (var name in new[] { "DocumentId", "Title", "RelativePath", "Source", "SourceSha256", "TextSha256", "IngestedUtc" })
                    Require(document, name, JsonValueKind.String);
                ValidateCapabilities(document);
                if (document.TryGetProperty("Attributes", out var attributes) && attributes.ValueKind != JsonValueKind.Null)
                {
                    if (attributes.ValueKind != JsonValueKind.Object || attributes.EnumerateObject().Any(a => a.Value.ValueKind != JsonValueKind.String))
                        throw new JsonException("Document attributes must contain strings.");
                }
            }
            var sidecar = JsonSerializer.Deserialize<CorpusFileSidecar>(bytes, JsonOptions)
                ?? throw new JsonException("The sidecar is empty.");
            if (string.IsNullOrWhiteSpace(sidecar.CorpusId) ||
                (corpusId is not null && !StringComparer.Ordinal.Equals(sidecar.CorpusId, corpusId)) ||
                sidecar.Documents.Any(d => string.IsNullOrWhiteSpace(d.DocumentId) || !IsValidDocumentName(d.RelativePath) || string.IsNullOrWhiteSpace(d.Source)) ||
                sidecar.Documents.Select(d => d.DocumentId).Distinct(StringComparer.Ordinal).Count() != sidecar.Documents.Count)
                throw new JsonException("The sidecar has invalid or duplicate identities.");
            return sidecar;
        }
        catch (JsonException error)
        {
            throw new InvalidDataException($"Invalid Corpus sidecar for '{corpusId ?? "unknown"}' at '{path}': {error.Message}", error);
        }
    }

    private static void ValidateCapabilities(JsonElement parent)
    {
        if (!parent.TryGetProperty("Capabilities", out var capabilities) || capabilities.ValueKind == JsonValueKind.Null) return;
        RequireBoolean(capabilities, "RequiresAttribution");
        Require(capabilities, "Basis", JsonValueKind.String);
        foreach (var name in new[] { "MayRedistribute", "MayDerive", "MayUseCommercially" })
            if (!capabilities.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null))
                throw new JsonException($"Required member '{name}' is missing or invalid.");
    }

    private static void RequireBoolean(JsonElement parent, string name)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new JsonException($"Required member '{name}' is missing or invalid.");
    }

    private static JsonElement Require(JsonElement parent, string name, JsonValueKind kind)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(name, out var value) || value.ValueKind != kind)
            throw new JsonException($"Required member '{name}' is missing or invalid.");
        return value;
    }

    /// <summary>Enumerates published Corpus identities without depending on the database index.</summary>
    public IReadOnlyList<string> ListPublished()
    {
        var root = Path.Combine(_root, "corpora");
        if (!Directory.Exists(root)) return Array.Empty<string>();
        var result = new List<string>();
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            var path = Path.Combine(directory, "corpus.json");
            RecoverPending(path);
            if (!File.Exists(path)) continue;
            var sidecar = ParseSidecar(null, path, File.ReadAllBytes(path));
            if (!StringComparer.Ordinal.Equals(Path.GetFileName(directory), Identity(sidecar.CorpusId)))
                throw new InvalidDataException("A Corpus sidecar is in a different identity directory.");
            result.Add(sidecar.CorpusId);
        }
        return result.Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>Reconstructs Corpus content and metadata from the published source files.</summary>
    public StoredCorpus ReadPublished(string corpusId)
    {
        var path = Path.Combine(CorpusDirectory(corpusId), "corpus.json");
        RecoverPending(path);
        var sidecarBytes = File.ReadAllBytes(path);
        var sidecar = ParseSidecar(corpusId, path, sidecarBytes);
        var documents = new List<CorpusDocument>();
        foreach (var published in sidecar.Documents)
        {
            var bytes = File.ReadAllBytes(DocumentPath(corpusId, published.RelativePath));
            var text = Utf8.GetString(bytes);
            documents.Add(new CorpusDocument(published.DocumentId, published.Title,
                DocumentSource.Parse(published.Source), text, published.SourceSha256, published.IngestedUtc,
                published.Licence, published.Capabilities, published.Attributes));
        }
        return new StoredCorpus(corpusId, sidecar.Provenance, documents) { PublicationRevision = Hash(sidecarBytes) };
    }

    /// <summary>Reads a Document snapshot and identifies the exact bytes returned by its hash.</summary>
    public (string Text, string Sha256) ReadDocument(string corpusId, string documentId)
    {
        var document = ReadSidecar(corpusId).Documents.SingleOrDefault(d => d.DocumentId == documentId)
            ?? throw new FileNotFoundException("The Document is absent from the corpus sidecar.", documentId);
        var path = DocumentPath(corpusId, document.RelativePath);
        var bytes = File.ReadAllBytes(path);
        return (Utf8.GetString(bytes), Hash(bytes));
    }

    /// <summary>Returns an exact byte range only if the entire file still has the expected hash.</summary>
    public byte[] ReadRange(string corpusId, string documentId, string expectedSha256, long offset, int length)
    {
        var sidecar = ReadSidecar(corpusId);
        var document = sidecar.Documents.SingleOrDefault(d => d.DocumentId == documentId)
            ?? throw new FileNotFoundException("The Document is absent from the corpus sidecar.", documentId);
        using var stream = new FileStream(DocumentPath(corpusId, document.RelativePath),
            FileMode.Open, FileAccess.Read, FileShare.Read);
        if (offset < 0 || length < 0 || length > 1024 * 1024 || offset > stream.Length || length > stream.Length - offset)
            throw new ArgumentOutOfRangeException(nameof(offset));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var result = new byte[length];
        var buffer = new byte[64 * 1024];
        long position = 0;
        int count;
        while ((count = stream.Read(buffer)) != 0)
        {
            hash.AppendData(buffer, 0, count);
            var start = Math.Max(position, offset);
            var end = Math.Min(position + count, offset + length);
            if (start < end)
                Buffer.BlockCopy(buffer, (int)(start - position), result, (int)(start - offset), (int)(end - start));
            position += count;
        }
        var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!StringComparer.OrdinalIgnoreCase.Equals(actual, expectedSha256))
            throw new InvalidDataException("The Document changed since the requested hash was recorded.");
        return result;
    }

    private string DocumentPath(string corpusId, string relativePath)
    {
        if (!IsValidDocumentName(relativePath))
            throw new InvalidDataException("The Document path must be a file name in its Corpus directory.");
        return Path.Combine(CorpusDirectory(corpusId), relativePath);
    }

    private static bool IsValidDocumentName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value is not ("." or "..") &&
        value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && value.IndexOfAny(['/', '\\', '\0']) < 0 &&
        !Path.IsPathRooted(value);

    private string CorpusDirectory(string corpusId) => Path.Combine(_root, "corpora", Identity(corpusId));
    private static string Identity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return Hash(Utf8.GetBytes(value));
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record PendingPublication(string BackupName, string ExpectedHash, string ReplacementHash);

    private void Replace(string path, byte[] expected, byte[] bytes)
    {
        var id = Guid.NewGuid().ToString("N");
        var temporary = path + "." + id + ".tmp";
        var displaced = path + "." + id + ".backup";
        var journal = path + "." + id + ".pending";
        var moved = false;
        var finished = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            using (var record = new FileStream(journal, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                var journalBytes = JsonSerializer.SerializeToUtf8Bytes(
                    new PendingPublication(Path.GetFileName(displaced), Hash(expected), Hash(bytes)));
                record.Write(journalBytes);
                record.Flush(flushToDisk: true);
                try
                {
                    File.Move(path, displaced);
                    moved = true;
                    AfterSidecarDisplacement?.Invoke(journal, journalBytes);
                    if (!File.ReadAllBytes(displaced).AsSpan().SequenceEqual(expected))
                        throw new IOException($"The Corpus sidecar changed while it was being updated: {path}");
                    File.Move(temporary, path);
                    AfterSidecarPublication?.Invoke(journal, journalBytes);
                    finished = true;
                }
                catch
                {
                    if (moved && !File.Exists(path))
                    {
                        try { RestoreBackup(displaced, path); }
                        catch (IOException) { }
                    }
                    finished = File.Exists(path);
                    throw;
                }
            }
        }
        finally
        {
            if (finished && File.Exists(journal)) File.Delete(journal);
            if (finished && File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static void RecoverPending(string path)
    {
        var directory = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(directory)) return;
        foreach (var journal in Directory.EnumerateFiles(directory, Path.GetFileName(path) + ".*.pending"))
        {
            using (var stream = new FileStream(journal, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                PendingPublication? pending = null;
                try
                {
                    pending = JsonSerializer.Deserialize<PendingPublication>(stream) ?? throw new JsonException();
                    if (pending.BackupName != Path.GetFileName(journal)[..^"pending".Length] + "backup" ||
                        pending.ExpectedHash?.Length != 64 || pending.ReplacementHash?.Length != 64)
                        throw new JsonException();
                }
                catch (JsonException error)
                {
                    var possibleBackup = journal[..^"pending".Length] + "backup";
                    if (!File.Exists(path) || File.Exists(possibleBackup))
                        throw new InvalidDataException($"Invalid pending Corpus publication: {journal}", error);
                    pending = null;
                }
                if (pending is not null)
                {
                    var backup = Path.Combine(directory, pending.BackupName);
                    if (!File.Exists(path))
                    {
                        var temporary = journal[..^"pending".Length] + "tmp";
                        if (!File.Exists(backup) || !File.Exists(temporary) || Hash(File.ReadAllBytes(temporary)) != pending.ReplacementHash)
                            throw new IOException($"Interrupted Corpus publication has no recoverable sidecar: {journal}");
                        RestoreBackup(backup, path);
                    }
                    else
                    {
                        var actual = Hash(File.ReadAllBytes(path));
                        var backupHash = File.Exists(backup) ? Hash(File.ReadAllBytes(backup)) : null;
                        if (actual != pending.ExpectedHash && actual != pending.ReplacementHash && actual != backupHash)
                            throw new IOException($"Interrupted Corpus publication conflicts with an external edit: {path}");
                    }
                }
            }
            File.Delete(journal);
        }
    }

    private static void RestoreBackup(string backup, string path)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".restore";
        try
        {
            using (var source = new FileStream(backup, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                source.CopyTo(target);
                target.Flush(flushToDisk: true);
            }
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Publish(string path, byte[] bytes)
    {
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                throw new IOException($"Existing corpus asset differs: {path}");
            return;
        }
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            try { File.Move(temporary, path); }
            catch (IOException) when (File.Exists(path))
            {
                if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) throw;
            }
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
                throw new IOException($"Corpus asset failed verification: {path}");
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
