using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Machine.Corpora;
using SIL.Motif.Scripture;

namespace SIL.Motif.Worker.Sources;

internal sealed record CorpusFileObservation(string Name, string Path, long Length, DateTime LastWrite, DateTime Created);

internal sealed record CorpusCapturedFile(CorpusFileVersion Version, CorpusFileObservation Observation, byte[] Bytes);

internal sealed record CorpusInventoryCheck(CorpusFileVersion[] Versions, CorpusFileObservation[] Observations);

internal sealed class CorpusSourceChangedException()
    : IOException("The Corpus Source changed during the operation. Retry against its new inputs.");

internal sealed class CorpusSourceInput
{
    private const string StylesheetEntry = "@stylesheet";
    private readonly CorpusSourceDeclaration _source;
    private readonly string _locator;
    private readonly string[]? _files;
    private readonly string? _stylesheet;
    private readonly Encoding? _usfmEncoding;

    public CorpusSourceBinding Binding { get; }
    public SikorCorpusInput? Sikor { get; }
    public bool HasStylesheet => _stylesheet is not null;

    public CorpusSourceInput(CorpusSourceDeclaration source, string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source.SourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Provenance);
        if (!Enum.IsDefined(source.Format) || source.Format == CorpusSourceFormat.Sikor != (source.Sikor is not null) ||
            source.Format == CorpusSourceFormat.Usfm != (source.Usfm is not null) ||
            source.Format == CorpusSourceFormat.Jsonl &&
                (string.IsNullOrWhiteSpace(source.JsonlIdField) || source.JsonlIdField == "text"))
            throw new InvalidDataException("The Corpus Source format and extraction declaration disagree.");
        _source = source;
        _locator = Path.GetFullPath(source.Locator);
        _files = source.Files?.ToArray();
        if (source.Sikor is { } split)
        {
            if (_files is not null) throw new InvalidDataException("A SIKOR Source is a single XML file.");
            Sikor = new(_locator, CorpusContent.Digest(new[] { projectId, source.SourceId }), split);
        }
        if (source.Usfm is { } scripture)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scripture.Edition);
            ArgumentNullException.ThrowIfNull(scripture.Versification);
            _usfmEncoding = CodePagesEncodingProvider.Instance.GetEncoding(scripture.EncodingName,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback) ??
                Encoding.GetEncoding(scripture.EncodingName, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            _stylesheet = scripture.StylesheetPath is null ? null : Path.GetFullPath(scripture.StylesheetPath);
        }

        var extraction = source.Format switch
        {
            CorpusSourceFormat.Jsonl => source.JsonlIdField,
            CorpusSourceFormat.Usfm => CorpusContent.Digest(new[] { source.Usfm!.Edition,
                source.Usfm.Versification.Name, _usfmEncoding!.WebName, _stylesheet is null ? "built-in" : "declared" }),
            CorpusSourceFormat.Sikor => Sikor!.KeyId,
            _ => null,
        };
        Binding = new(source.Format, source.Segmentation.WritingSystemId, source.Segmentation.Id, source.Provenance,
            CorpusSourceProvider.Version, extraction, Sikor?.SplitDigest, Sikor is not null);
    }

    public string[] FileNames()
    {
        if (Sikor is not null) throw new InvalidOperationException("SIKOR inventories use authorized Documents.");
        var paths = File.Exists(_locator)
            ? _files is null ? new[] { Path.GetFileName(_locator) }
                : throw new InvalidDataException("An ordered file list requires a Source folder.")
            : _files ?? Enumerate(_locator).Order(StringComparer.Ordinal).ToArray();
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seen = new HashSet<string>(comparison);
        foreach (var name in paths)
        {
            if (!IsCanonicalRelativePath(name) || !IsSourceFile(name) || !seen.Add(name))
                throw new InvalidDataException("The Corpus Source has an invalid or duplicate input path.");
        }
        if (paths.Length == 0) throw new InvalidDataException("The Corpus Source has no input files.");
        return _stylesheet is null ? paths : [StylesheetEntry, .. paths];
    }

    public CorpusCapturedFile Capture(string name, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var observation = Observe(name);
        var bytes = File.ReadAllBytes(observation.Path);
        cancellationToken.ThrowIfCancellationRequested();
        return new(new(name, CorpusContent.Hash(bytes), bytes.LongLength), observation, bytes);
    }

    public CorpusCapturedFile? CaptureStylesheet(CancellationToken cancellationToken) =>
        _stylesheet is null ? null : Capture(StylesheetEntry, cancellationToken);

    public CorpusInventoryCheck Recheck(string[] names, CancellationToken cancellationToken,
        Action<CorpusFileVersion, int>? afterFingerprint = null)
    {
        var versions = new CorpusFileVersion[names.Length];
        var observations = new CorpusFileObservation[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observation = observations[i] = Observe(names[i]);
            versions[i] = Fingerprint(observation.Name, observation.Path, cancellationToken);
            afterFingerprint?.Invoke(versions[i], i);
        }
        return new(versions, observations);
    }

    public void EnsureUnchanged(IReadOnlyList<CorpusFileObservation> observations, CancellationToken cancellationToken)
    {
        if (!FileNames().SequenceEqual(observations.Select(file => file.Name))) throw Changed();
        EnsureFilesUnchanged(observations, cancellationToken);
    }

    public void EnsureFilesUnchanged(IEnumerable<CorpusFileObservation> observations, CancellationToken cancellationToken)
    {
        foreach (var observation in observations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Observe(observation.Name) != observation) throw Changed();
        }
    }

    public CorpusFileObservation ObserveSikor() => Observe(Path.GetFileName(_locator));

    private CorpusFileObservation Observe(string name)
    {
        var path = name == StylesheetEntry ? _stylesheet! : FilePath(name);
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("A Corpus Source cannot traverse a file or directory alias.");
        return new(name, path, info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc);
    }

    private IEnumerable<string> Enumerate(string directory)
    {
        RejectAlias(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var flags = File.GetAttributes(entry);
            if ((flags & FileAttributes.Directory) != 0)
            {
                foreach (var nested in Enumerate(entry)) yield return nested;
            }
            else if (IsSourceFile(entry))
                yield return Path.GetRelativePath(_locator, entry).Replace(Path.DirectorySeparatorChar, '/');
        }
    }

    private bool IsSourceFile(string path)
    {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        return _source.Format switch
        {
            CorpusSourceFormat.PlainText => extension == ".txt",
            CorpusSourceFormat.Jsonl => extension == ".jsonl",
            CorpusSourceFormat.Usfm => extension is ".usfm" or ".sfm",
            _ => false,
        };
    }

    private static bool IsCanonicalRelativePath(string name) =>
        !string.IsNullOrWhiteSpace(name) && !Path.IsPathRooted(name) &&
        name.Split('/').All(part => part is not ("" or "." or "..") &&
            part.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !part.Contains('\\'));

    private string FilePath(string name)
    {
        if (!IsCanonicalRelativePath(name)) throw new InvalidDataException("The Corpus file locator is invalid.");
        if (File.Exists(_locator))
        {
            if (name != Path.GetFileName(_locator)) throw Changed();
            return _locator;
        }
        var path = _locator;
        RejectAlias(path);
        foreach (var part in name.Split('/'))
        {
            path = Path.Combine(path, part);
            RejectAlias(path);
        }
        return path;
    }

    private static void RejectAlias(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("A Corpus Source cannot traverse a file or directory alias.");
    }

    private static CorpusFileVersion Fingerprint(string name, string path, CancellationToken cancellationToken)
    {
        RejectAlias(path);
        using var stream = File.OpenRead(path);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        long bytes = 0;
        int count;
        while ((count = stream.Read(buffer)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(buffer, 0, count);
            bytes += count;
        }
        return new(name, Convert.ToHexStringLower(hash.GetHashAndReset()), bytes);
    }

    public bool Matches(CorpusCapturedFile captured, CancellationToken cancellationToken) =>
        captured.Version == Fingerprint(captured.Version.Name, captured.Observation.Path, cancellationToken);

    public static bool IsDocumentFile(CorpusFileVersion version) => version.Name != StylesheetEntry;

    public IEnumerable<CorpusInputDocument> Read(CorpusCapturedFile captured, UsfmStylesheet? stylesheet,
        CancellationToken cancellationToken, int? knownPosition = null)
    {
        var (file, _, bytes) = captured;
        return _source.Format switch
        {
            CorpusSourceFormat.PlainText => [new(new(file.Name, file.Name, 1, file.RawHash, file.RawHash, null),
                PlainLines(bytes))],
            CorpusSourceFormat.Jsonl => JsonlDocuments(file.Name, bytes, knownPosition, cancellationToken),
            CorpusSourceFormat.Usfm => ScriptureDocuments(file, bytes, stylesheet!, cancellationToken),
            _ => throw new InvalidOperationException("The Source has no file-backed Document decoder."),
        };
    }

    public IEnumerable<CorpusInputDocument> ReadSikor(CancellationToken cancellationToken)
    {
        RejectAlias(_locator);
        using var rows = Sikor!.Read(cancellationToken).GetEnumerator();
        while (true)
        {
            CorpusInputDocument row;
            try
            {
                if (!rows.MoveNext()) yield break;
                row = rows.Current;
            }
            catch (System.Xml.XmlException)
            {
                throw new InvalidDataException("The SIKOR Source cannot be read as XML.");
            }
            yield return row;
        }
    }

    private IEnumerable<CorpusInputDocument> ScriptureDocuments(CorpusFileVersion file, byte[] bytes, UsfmStylesheet stylesheet,
        CancellationToken cancellationToken)
    {
        var scripture = _source.Usfm!;
        var text = Decode(bytes, _usfmEncoding!);
        var position = 0;
        foreach (var chapter in ParatextPassages.ReadUsfm(text, stylesheet, scripture.Versification, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new(new(chapter.Locator, file.Name, ++position, file.RawHash, chapter.Hash,
                JsonSerializer.Serialize(new { scripture.Edition, Versification = scripture.Versification.Name })),
                chapter.Verses.Select((verse, index) => new CorpusSegment(index + 1, verse.Text, Verse: verse.AnchorReference)));
        }
    }

    private IEnumerable<CorpusInputDocument> JsonlDocuments(string name, byte[] bytes, int? knownPosition,
        CancellationToken cancellationToken)
    {
        foreach (var (position, _, row) in Lines(bytes))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (position < knownPosition) continue;
            if (position > knownPosition) yield break;
            JsonDocument json;
            try { json = JsonDocument.Parse(row); }
            catch (JsonException) { throw InvalidRow(name, position); }
            using (json)
            {
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object ||
                    root.EnumerateObject().Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() !=
                        root.EnumerateObject().Count() ||
                    !TryString(root, _source.JsonlIdField, out var id) || string.IsNullOrWhiteSpace(id) ||
                    !TryString(root, "text", out var text) ||
                    root.TryGetProperty("title", out var title) && title.ValueKind != JsonValueKind.String ||
                    root.TryGetProperty("source", out var source) && source.ValueKind != JsonValueKind.String)
                    throw InvalidRow(name, position);
                var metadata = root.EnumerateObject().Where(property =>
                    property.Name != _source.JsonlIdField && property.Name != "text")
                    .ToDictionary(property => property.Name, property => property.Value);
                yield return new(new(id, name, position, CorpusContent.Hash(row.Span), CorpusContent.Hash(text),
                    JsonSerializer.Serialize(metadata)), PlainLines(CorpusContent.Utf8.GetBytes(text)));
            }
        }
    }

    private static InvalidDataException InvalidRow(string name, int position) =>
        new($"Invalid JSONL Document in '{name}' at row {position}.");

    private static bool TryString(JsonElement root, string property, out string text)
    {
        text = "";
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String) return false;
        text = value.GetString()!;
        return true;
    }

    private static IEnumerable<CorpusSegment> PlainLines(byte[] bytes) =>
        Lines(bytes).Select(line => new CorpusSegment(line.Number, CorpusContent.Utf8.GetString(line.Bytes.Span), line.Offset + 1));

    private static IEnumerable<(int Number, long Offset, ReadOnlyMemory<byte> Bytes)> Lines(byte[] bytes)
    {
        var start = bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble) ? Encoding.UTF8.Preamble.Length : 0;
        var number = 0;
        while (start < bytes.Length)
        {
            var separator = bytes.AsSpan(start).IndexOfAny((byte)'\r', (byte)'\n');
            var end = separator < 0 ? bytes.Length : start + separator;
            yield return (++number, start, bytes.AsMemory(start, end - start));
            start = end + 1;
            if (start < bytes.Length && bytes[end] == '\r' && bytes[start] == '\n') start++;
        }
    }

    private static string Decode(byte[] bytes, Encoding encoding)
    {
        var preamble = encoding.Preamble;
        if (encoding.CodePage == Encoding.UTF8.CodePage) preamble = Encoding.UTF8.Preamble;
        return encoding.GetString(bytes.AsSpan().StartsWith(preamble) ? bytes.AsSpan(preamble.Length) : bytes);
    }

    public static CorpusSourceChangedException Changed() => new();
}
