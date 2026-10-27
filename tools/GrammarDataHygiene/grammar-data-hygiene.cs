using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

#if !GRAMMAR_DATA_HYGIENE_UNDER_TEST
return GrammarDataHygiene.Run(args);
#endif

internal enum FindingSeverity { Reject, Review }

internal sealed record GrammarFinding(string Rule, FindingSeverity Severity, string Explanation);

internal static class GrammarDataHygiene
{
    private const int MaxDepth = 2;
    private const int MaxExpandedBytes = 32 * 1024 * 1024;
    private const int MaxArchiveMembers = 256;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly HashSet<string> ForbiddenSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ".fwdata", ".fwbackup", ".fwdb", ".lift", ".plip", ".ldml",
    };
    private static readonly HashSet<string> DataKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "lexicon", "entries", "lexentries", "wordlist", "words", "wordforms", "corpus",
        "phonemes", "morphemes", "affixes", "allomorphs", "grammar", "grammarspec",
        "rules", "paradigms", "senses", "lexicalitems", "entry", "word",
    };
    private static readonly HashSet<string> RecordKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "form", "lexeme", "lexemeform", "word", "surface", "citationform", "phoneme",
        "morpheme", "affix", "gloss", "sense", "allomorph", "rule", "environment",
    };
    private static readonly HashSet<string> StrongRecordKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "form", "lexeme", "lexemeform", "word", "citationform", "phoneme", "morpheme", "affix", "gloss", "allomorph", "rule",
    };
    private static readonly Regex CSharpLiteral = new(
        "(?<![\\w$])(?:@\"(?<verbatim>(?:\"\"|[^\"])*?)\"|\"\"\"(?<raw>.*?)\"\"\"|\"(?<regular>(?:\\\\.|[^\"\\\\])*)\")",
        RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Base64Run = new("(?<![A-Za-z0-9+/])(?:[A-Za-z0-9+/]{4}){8,}(?:[A-Za-z0-9+/]{2}==|[A-Za-z0-9+/]{3}=)?(?![A-Za-z0-9+/])", RegexOptions.Compiled);
    private static readonly Regex HexRun = new("(?<![0-9A-Fa-f])(?:[0-9A-Fa-f]{2}){32,}(?![0-9A-Fa-f])", RegexOptions.Compiled);

    internal static IReadOnlyList<GrammarFinding> Inspect(string path, ReadOnlyMemory<byte> bytes)
    {
        var findings = new List<GrammarFinding>();
        var budget = new DecodeBudget();
        InspectCore(path, bytes.ToArray(), 0, budget, findings, true);
        return findings.DistinctBy(f => (f.Rule, f.Severity)).ToArray();
    }

    public static int Run(string[] args)
    {
        try
        {
            var options = Parse(args);
            string? resolvedBase = null, resolvedHead = null;
            if (options.Mode is "tip" or "range") resolvedHead = ResolveCommit(options.Repository, options.Head!);
            if (options.Mode == "range") resolvedBase = ResolveCommit(options.Repository, options.Base!);
            if (resolvedBase is not null) Console.WriteLine($"grammar-data-hygiene: range base={resolvedBase} head={resolvedHead}");
            else if (resolvedHead is not null) Console.WriteLine($"grammar-data-hygiene: tip={resolvedHead}");
            var findings = options.Mode switch
            {
                "worktree" => ScanWorktree(options.Repository),
                "index" => ScanIndex(options.Repository),
                "tip" => ScanTip(options.Repository, resolvedHead!),
                "range" => ScanRange(options.Repository, resolvedBase!, resolvedHead!),
                _ => throw new ArgumentException("Choose exactly one scan mode."),
            };

            foreach (var finding in findings.OrderBy(f => f.Path, StringComparer.Ordinal).ThenBy(f => f.Finding.Rule, StringComparer.Ordinal))
            {
                var severity = finding.Finding.Severity == FindingSeverity.Reject ? "reject" : "review";
                Console.WriteLine($"{severity}: {EscapePath(finding.Path)} [{finding.Finding.Rule}] sha256:{finding.Digest} {finding.Finding.Explanation}");
            }
            var rejected = findings.Any(f => f.Finding.Severity == FindingSeverity.Reject);
            Console.WriteLine($"grammar-data-hygiene: {findings.Count(f => f.Finding.Severity == FindingSeverity.Reject)} reject; {findings.Count(f => f.Finding.Severity == FindingSeverity.Review)} review.");
            return rejected ? 1 : 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException or CryptographicException)
        {
            Console.Error.WriteLine($"grammar-data-hygiene: {SafeMessage(ex.Message)}");
            return 2;
        }
    }

    private sealed record Options(string Mode, string Repository, string? Base, string? Head);
    private sealed record PathFinding(string Path, GrammarFinding Finding, string Digest);
    private sealed record GitResult(int ExitCode, byte[] Output, byte[] Error);
    private sealed record IndexEntry(string Path, string Mode, string ObjectId, int Stage);
    private sealed record WorktreePathInfo(string? LinkTarget, bool Exists, bool IsDirectory, bool UnknownReparsePoint);
    private sealed class DecodeBudget { public long Bytes; public int Members; }

    private static Options Parse(string[] args)
    {
        string? mode = null, repository = null, baseSha = null, headSha = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--worktree": mode = SetMode(mode, "worktree"); break;
                case "--index": mode = SetMode(mode, "index"); break;
                case "--tip":
                    mode = SetMode(mode, "tip");
                    if (++i >= args.Length) throw new ArgumentException("--tip requires a commit.");
                    headSha = args[i];
                    break;
                case "--range":
                    mode = SetMode(mode, "range");
                    if (i + 2 >= args.Length) throw new ArgumentException("--range requires base and head commits.");
                    baseSha = args[++i]; headSha = args[++i];
                    break;
                case "--repo":
                    if (++i >= args.Length || repository is not null) throw new ArgumentException("--repo requires one repository root.");
                    repository = Path.GetFullPath(args[i]);
                    break;
                default: throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }
        if (mode is null || repository is null) throw new ArgumentException("Usage: (--worktree|--index|--tip <sha>|--range <base> <head>) --repo <root>.");
        if ((mode == "range") != (baseSha is not null) || (mode is "tip" or "range") != (headSha is not null))
            throw new ArgumentException("The selected mode has missing or unexpected commit arguments.");
        return new Options(mode, repository, baseSha, headSha);
    }

    private static string SetMode(string? existing, string mode)
    {
        if (existing is not null) throw new ArgumentException("Choose exactly one scan mode.");
        return mode;
    }

    private static void InspectCore(string path, byte[] bytes, int depth, DecodeBudget budget, List<GrammarFinding> findings, bool checkPath)
    {
        if (checkPath && IsNamedCarrier(path))
        {
            Add(findings, "project-carrier", FindingSeverity.Reject, "recognized project or writing-system store path");
            return;
        }
        if (LooksLikeProjectMagic(bytes))
        {
            Add(findings, "project-signature", FindingSeverity.Reject, "recognized project-store signature");
            return;
        }
        if (bytes.Length > MaxExpandedBytes)
        {
            Add(findings, "opaque-wrapper", FindingSeverity.Review, "candidate exceeds the bounded inspection size");
            return;
        }

        if (depth <= MaxDepth)
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 0x50, 0x4b, 0x03, 0x04 }) || bytes.AsSpan().StartsWith(new byte[] { 0x50, 0x4b, 0x05, 0x06 }))
            {
                InspectZip(path, bytes, depth, budget, findings);
                return;
            }
            if (bytes.AsSpan().StartsWith(new byte[] { 0x1f, 0x8b }))
            {
                InspectGzip(path, bytes, depth, budget, findings);
                return;
            }
        }
        else if (LooksWrapped(bytes))
        {
            Add(findings, "opaque-wrapper", FindingSeverity.Review, "nested wrapper exceeds the supported inspection depth");
            return;
        }

        var codeSource = IsCodeSourcePath(path);
        if (!codeSource) findings.AddRange(InspectStructured(bytes));
        var text = DecodeText(bytes);
        if (text is not null)
        {
            if (!codeSource) InspectTextCarriers(path, text, depth, budget, findings);
            if (codeSource) InspectSourceLiterals(text, depth, budget, findings);
            else if (depth < MaxDepth) InspectEncodedRuns(text, depth, budget, findings);
        }
        else if (LooksSuspiciousOpaque(bytes))
        {
            Add(findings, "opaque-carrier", FindingSeverity.Review, "binary content resembles a serialized carrier but is not a supported format");
        }
        else if (text is null && IsSourcePath(path))
        {
            var ascii = Encoding.ASCII.GetString(bytes.Select(b => b < 0x80 ? b : (byte)' ').ToArray());
            var partial = new List<GrammarFinding>();
            foreach (var finding in InspectStructured(Encoding.UTF8.GetBytes(ascii))) partial.Add(finding);
            if (codeSource) InspectSourceLiterals(ascii, depth, budget, partial);
            findings.AddRange(partial);
            Add(findings, "invalid-text-encoding", FindingSeverity.Review, "source-like content is not valid supported text encoding");
        }
    }

    private static void InspectTextCarriers(string path, string text, int depth, DecodeBudget budget, List<GrammarFinding> findings)
    {
        var marker = Encoding.UTF8.GetBytes(text);
        if (LooksLikeProjectMagic(marker)) Add(findings, "project-signature", FindingSeverity.Reject, "recognized project-store signature");
        if (Path.GetFileName(path).Equals("WritingSystemStore", StringComparison.OrdinalIgnoreCase))
            Add(findings, "writing-system-store", FindingSeverity.Reject, "recognized writing-system store path");
    }

    private static List<GrammarFinding> InspectStructured(byte[] bytes)
    {
        var findings = new List<GrammarFinding>();
        var text = DecodeText(bytes);
        if (text is null || text.Length == 0) return findings;
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 96, CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (HasPopulatedGrammarObject(document.RootElement, false, false, 0))
                Add(findings, "populated-grammar-structure", FindingSeverity.Reject, "populated lexicon or grammar records detected");
            return findings;
        }
        catch (JsonException) { }
        try
        {
            using var reader = XmlReader.Create(new StringReader(text), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxExpandedBytes });
            var document = XDocument.Load(reader);
            if (IsFieldWorksProjectXml(document.Root))
                Add(findings, "project-signature", FindingSeverity.Reject, "recognized FieldWorks project XML signature");
            else if (HasPopulatedXmlGrammar(document.Root))
                Add(findings, "populated-grammar-structure", FindingSeverity.Reject, "populated lexicon or grammar records detected");
            return findings;
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException) { }
        if (HasSupportedTable(text)) Add(findings, "populated-grammar-table", FindingSeverity.Reject, "populated word or grammar table detected");
        return findings;
    }

    private static bool IsFieldWorksProjectXml(XElement? root)
    {
        if (root?.Name.LocalName != "languageproject") return false;
        if (int.TryParse((string?)root.Attribute("version"), out var modelVersion) && modelVersion > 0) return true;
        return root.Elements().Any(element =>
            element.Name.LocalName == "rt" &&
            (string?)element.Attribute("class") == "LangProject" &&
            Guid.TryParse((string?)element.Attribute("guid"), out _));
    }

    private static bool HasPopulatedGrammarObject(JsonElement element, bool insideSchema, bool insideTextSegment, int depth)
    {
        if (depth > 64) return false;
        if (element.ValueKind == JsonValueKind.Object)
        {
            var schema = insideSchema || element.EnumerateObject().Any(p => p.Name is "properties" or "$defs" or "definitions");
            foreach (var property in element.EnumerateObject())
            {
                var key = NormalizeKey(property.Name);
                if (!schema && !(insideTextSegment && key == "words") && DataKeys.Contains(key) && IsPopulatedCollection(property.Value) && HasRecordEvidence(property.Value)) return true;
                if (HasPopulatedGrammarObject(property.Value, schema, insideTextSegment || key == "segments", depth + 1)) return true;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) if (HasPopulatedGrammarObject(item, insideSchema, insideTextSegment, depth + 1)) return true;
        }
        return false;
    }

    private static bool IsPopulatedCollection(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Array => value.GetArrayLength() > 0,
        JsonValueKind.Object => value.EnumerateObject().Any(),
        _ => false,
    };

    private static bool HasRecordEvidence(JsonElement value)
    {
        IEnumerable<JsonElement> items = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [value];
        foreach (var item in items)
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()) && !LooksLikeEntityId(item.GetString()!)) return true;
            if (item.ValueKind != JsonValueKind.Object) continue;
            foreach (var property in item.EnumerateObject())
            {
                var key = NormalizeKey(property.Name);
                if (StrongRecordKeys.Contains(key) && property.Value.ValueKind is JsonValueKind.String or JsonValueKind.Number &&
                    !string.IsNullOrWhiteSpace(property.Value.ToString()) &&
                    (property.Value.ValueKind != JsonValueKind.String || !LooksLikeEntityId(property.Value.GetString()!))) return true;
                if (key is "forms" or "senses" or "allomorphs" && IsPopulatedCollection(property.Value)) return true;
            }
        }
        return false;
    }

    private static bool HasPopulatedXmlGrammar(XElement? root)
    {
        if (root is null) return false;
        foreach (var element in root.DescendantsAndSelf())
        {
            var key = NormalizeKey(element.Name.LocalName);
            if (!DataKeys.Contains(key)) continue;
            if (element.Ancestors().Any(a => NormalizeKey(a.Name.LocalName) is "schema" or "properties" or "definitions")) continue;
            var records = element.Elements().Where(e => RecordKeys.Contains(NormalizeKey(e.Name.LocalName)) || DataKeys.Contains(NormalizeKey(e.Name.LocalName))).ToArray();
            if (records.Any(e => StrongRecordKeys.Contains(NormalizeKey(e.Name.LocalName)) && (!string.IsNullOrWhiteSpace(e.Value) || e.HasElements || e.HasAttributes))) return true;
            if (element.Elements().Any(e => !string.IsNullOrWhiteSpace(e.Value)) && element.Elements().Count() >= 2) return true;
        }
        return false;
    }

    private static bool HasSupportedTable(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 3) return false;
        var delimiter = lines[0].Contains('\t') ? '\t' : lines[0].Contains(',') ? ',' : '\0';
        if (delimiter == '\0')
        {
            var container = Regex.Match(text, @"(?im)^\s*(?:entries|lexicon|words|phonemes|morphemes|affixes|rules)\s*:\s*\r?\n(?<body>(?:[ \t]+[^\r\n]*\r?\n?)+)");
            return container.Success && Regex.IsMatch(container.Groups["body"].Value, @"(?im)^\s*-\s*(?:form|lexeme|word|surface|phoneme|morpheme|affix|rule)\s*:\s*\S");
        }
        var headers = lines[0].Split(delimiter).Select(NormalizeKey).ToArray();
        if (!headers.Any(h => RecordKeys.Contains(h)) || !headers.Any(h => h is "form" or "lexeme" or "word" or "surface")) return false;
        return lines.Skip(1).Take(2).Count(line => line.Split(delimiter).Length == headers.Length && line.Split(delimiter).Any(c => !string.IsNullOrWhiteSpace(c))) >= 2;
    }

    private static void InspectSourceLiterals(string text, int depth, DecodeBudget budget, List<GrammarFinding> findings)
    {
        var matches = CSharpLiteral.Matches(text);
        for (var i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            string value;
            if (match.Groups["verbatim"].Success) value = match.Groups["verbatim"].Value.Replace("\"\"", "\"", StringComparison.Ordinal);
            else if (match.Groups["raw"].Success) value = match.Groups["raw"].Value;
            else value = DecodeCSharpString(match.Groups["regular"].Value);
            var end = match.Index + match.Length;
            while (i + 1 < matches.Count && Regex.IsMatch(text[end..matches[i + 1].Index], @"^\s*\+\s*$"))
            {
                i++;
                var next = matches[i];
                var nextText = next.Groups["verbatim"].Success ? next.Groups["verbatim"].Value.Replace("\"\"", "\"", StringComparison.Ordinal)
                    : next.Groups["raw"].Success ? next.Groups["raw"].Value : DecodeCSharpString(next.Groups["regular"].Value);
                value += nextText;
                end = next.Index + next.Length;
            }
            budget.Bytes += Encoding.UTF8.GetByteCount(value);
            if (budget.Bytes > MaxExpandedBytes)
            {
                Add(findings, "opaque-wrapper", FindingSeverity.Review, "decoded source literals exceed the bounded inspection size");
                return;
            }
            var childFindings = new List<GrammarFinding>();
            InspectCore("literal", Encoding.UTF8.GetBytes(value), depth, budget, childFindings, false);
            if (childFindings.Count == 0) continue;
            var sourceLine = 1;
            for (var character = 0; character < match.Index; character++) if (text[character] == '\n') sourceLine++;
            foreach (var finding in childFindings)
                findings.Add(finding with { Explanation = $"source literal at line {sourceLine}; {finding.Explanation}" });
        }
    }

    private static string DecodeCSharpString(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 == value.Length) { builder.Append(value[i]); continue; }
            var c = value[++i];
            switch (c)
            {
                case 'n': builder.Append('\n'); break; case 'r': builder.Append('\r'); break; case 't': builder.Append('\t'); break;
                case '0': builder.Append('\0'); break; case '\\': builder.Append('\\'); break; case '"': builder.Append('"'); break;
                case 'u' when i + 4 < value.Length && ushort.TryParse(value.AsSpan(i + 1, 4), System.Globalization.NumberStyles.HexNumber, null, out var code): builder.Append((char)code); i += 4; break;
                default: builder.Append('\\').Append(c); break;
            }
        }
        return builder.ToString();
    }

    private static void InspectEncodedRuns(string text, int depth, DecodeBudget budget, List<GrammarFinding> findings)
    {
        foreach (Match match in Base64Run.Matches(text))
        {
            try
            {
                var decoded = Convert.FromBase64String(match.Value);
                if (!InspectDecoded("base64", decoded, depth, budget, findings, decoded.Length >= 32)) return;
            }
            catch (FormatException) { }
        }
        foreach (Match match in HexRun.Matches(text))
        {
            try
            {
                if (!InspectDecoded("hex", Convert.FromHexString(match.Value), depth, budget, findings)) return;
            }
            catch (FormatException) { }
        }
    }

    private static bool InspectDecoded(string kind, byte[] bytes, int depth, DecodeBudget budget, List<GrammarFinding> findings, bool inspectContent = true)
    {
        if (bytes.Length == 0) return true;
        if (budget.Bytes > MaxExpandedBytes || bytes.Length > MaxExpandedBytes - budget.Bytes)
        {
            Add(findings, "opaque-wrapper", FindingSeverity.Review, "encoded runs exceed the bounded inspection size");
            return false;
        }
        budget.Bytes += bytes.Length;
        if (!inspectContent) return true;
        if (depth >= MaxDepth) { Add(findings, "opaque-wrapper", FindingSeverity.Review, "encoded content exceeds the supported inspection depth"); return true; }
        InspectCore(kind, bytes, depth + 1, budget, findings, false);
        return true;
    }

    private static void InspectZip(string path, byte[] bytes, int depth, DecodeBudget budget, List<GrammarFinding> findings)
    {
        if (depth >= MaxDepth) { Add(findings, "opaque-wrapper", FindingSeverity.Review, "archive exceeds the supported inspection depth"); return; }
        try
        {
            using var input = new MemoryStream(bytes, false);
            using var archive = new ZipArchive(input, ZipArchiveMode.Read, false);
            foreach (var entry in archive.Entries)
            {
                if (++budget.Members > MaxArchiveMembers) { Add(findings, "opaque-wrapper", FindingSeverity.Review, "archive exceeds the bounded member count"); return; }
                if (IsNamedCarrier(entry.FullName)) { Add(findings, "archive-member", FindingSeverity.Reject, "archive contains a recognized project or grammar-data path"); continue; }
                if (entry.Length > MaxExpandedBytes || budget.Bytes + entry.Length > MaxExpandedBytes) { Add(findings, "opaque-wrapper", FindingSeverity.Review, "archive exceeds the bounded expanded size"); continue; }
                using var source = entry.Open();
                using var target = new MemoryStream((int)Math.Min(entry.Length, MaxExpandedBytes));
                CopyBounded(source, target, MaxExpandedBytes - budget.Bytes);
                var content = target.ToArray(); budget.Bytes += content.Length;
                InspectCore(entry.FullName, content, depth + 1, budget, findings, true);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or NotSupportedException)
        { Add(findings, "opaque-wrapper", FindingSeverity.Review, "archive is malformed, encrypted, or unsupported"); }
    }

    private static void InspectGzip(string path, byte[] bytes, int depth, DecodeBudget budget, List<GrammarFinding> findings)
    {
        if (depth >= MaxDepth) { Add(findings, "opaque-wrapper", FindingSeverity.Review, "compressed content exceeds the supported inspection depth"); return; }
        try
        {
            using var input = new MemoryStream(bytes, false); using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream(); CopyBounded(gzip, output, MaxExpandedBytes - budget.Bytes);
            var expanded = output.ToArray(); budget.Bytes += expanded.Length;
            InspectCore(path, expanded, depth + 1, budget, findings, false);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        { Add(findings, "opaque-wrapper", FindingSeverity.Review, "compressed content is malformed or exceeds the bounded size"); }
    }

    private static void CopyBounded(Stream source, Stream target, long remaining)
    {
        var buffer = new byte[81920];
        while (true)
        {
            var read = source.Read(buffer, 0, buffer.Length);
            if (read == 0) return;
            if (read > remaining - target.Length) throw new InvalidDataException("expanded content exceeds limit");
            target.Write(buffer, 0, read);
        }
    }

    private static bool IsNamedCarrier(string path)
    {
        var normalized = path.Replace('\\', '/');
        var name = normalized[(normalized.LastIndexOf('/') + 1)..];
        if (name.Equals("WritingSystemStore", StringComparison.OrdinalIgnoreCase) || normalized.Split('/').Any(p => p.Equals("WritingSystemStore", StringComparison.OrdinalIgnoreCase))) return true;
        foreach (var suffix in ForbiddenSuffixes) if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return true;
        return name.EndsWith(".fwdata-wal", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".fwdata-shm", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".fwdata-journal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSourcePath(string path) => IsCodeSourcePath(path) || new[] { ".json", ".xml", ".yaml", ".yml", ".md", ".axaml", ".csproj", ".resx", ".toml" }.Any(s => path.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    private static bool IsCodeSourcePath(string path) => new[] { ".cs", ".ps1", ".py", ".rs", ".js", ".ts", ".sh", ".props" }.Any(s => path.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    private static bool LooksLikeProjectMagic(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.StartsWith("SQLite format 3\0"u8)) return false;
        return ContainsSqliteIdentifier(bytes, "LangProject") && ContainsSqliteIdentifier(bytes, "CmObject") && ContainsSqliteIdentifier(bytes, "LexEntry");
    }
    private static bool ContainsSqliteIdentifier(ReadOnlySpan<byte> bytes, string identifier)
    {
        return bytes.IndexOf(Encoding.UTF8.GetBytes(identifier)) >= 0 ||
            bytes.IndexOf(Encoding.Unicode.GetBytes(identifier)) >= 0 ||
            bytes.IndexOf(Encoding.BigEndianUnicode.GetBytes(identifier)) >= 0;
    }
    private static bool LooksWrapped(ReadOnlySpan<byte> bytes) => bytes.StartsWith(new byte[] { 0x50, 0x4b }) || bytes.StartsWith(new byte[] { 0x1f, 0x8b });
    private static bool LooksSuspiciousOpaque(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length <= 4096 || IsOrdinaryAsset(bytes)) return false;
        var zeros = 0;
        foreach (var value in bytes) if (value == 0) zeros++;
        return zeros > bytes.Length / 8;
    }
    private static string? DecodeText(byte[] bytes)
    {
        try
        {
            if (bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return StrictUtf8.GetString(bytes.AsSpan(3));
            if (bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe })) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff })) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            if (IsOrdinaryAsset(bytes)) return null;
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException) { return null; }
    }
    private static bool IsOrdinaryAsset(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) ||
        bytes.StartsWith(new byte[] { 0xff, 0xd8, 0xff }) ||
        bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8) ||
        bytes.StartsWith("BM"u8) || bytes.StartsWith("OTTO"u8) || bytes.StartsWith("true"u8) || bytes.StartsWith("typ1"u8) ||
        bytes.StartsWith(new byte[] { 0, 1, 0, 0 }) || bytes.StartsWith("wOFF"u8) || bytes.StartsWith("wOF2"u8) ||
        bytes.StartsWith("RIFF"u8) && bytes.Length >= 12 && bytes.Slice(8, 4).SequenceEqual("WEBP"u8);
    private static bool LooksLikeEntityId(string value) => value.Length == 22 && Regex.IsMatch(value, @"^[A-Za-z0-9_-]{22}$");
    private static string NormalizeKey(string value) => new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    private static void Add(List<GrammarFinding> findings, string rule, FindingSeverity severity, string explanation) => findings.Add(new(rule, severity, explanation));

    private static List<PathFinding> ScanWorktree(string repository)
    {
        var findings = new List<PathFinding>();
        var indexEntries = ParseIndex(Git(repository, "ls-files", "--stage", "-z").Output);
        if (indexEntries.Any(e => e.Stage != 0)) throw new InvalidDataException("unresolved index conflict");
        var entriesByPath = new Dictionary<string, IndexEntry>(StringComparer.Ordinal);
        var head = GitOptional(repository, "rev-parse", "--verify", "--quiet", "HEAD^{commit}");
        if (head.ExitCode == 0)
        {
            var commit = Encoding.ASCII.GetString(head.Output).Trim();
            foreach (var entry in ParseTree(Git(repository, "ls-tree", "-r", "-z", "--full-tree", commit).Output)) entriesByPath[entry.Path] = entry;
        }
        else if (head.ExitCode != 1) throw new IOException("could not resolve the current checkout tip");
        foreach (var entry in indexEntries) entriesByPath[entry.Path] = entry;
        foreach (var path in SplitNulBytes(Git(repository, "ls-files", "--others", "--exclude-standard", "-z").Output))
        {
            var displayPath = DecodeDisplayPath(path);
            entriesByPath.TryAdd(displayPath, new IndexEntry(displayPath, "100644", string.Empty, 0));
        }
        var root = Path.GetFullPath(repository);
        foreach (var entry in entriesByPath.Values)
        {
            if (entry.Mode == "160000")
            {
                AddPathFinding(findings, entry.Path, "gitlink", FindingSeverity.Review, "submodule contents are not inspected by this scan", entry.ObjectId);
                continue;
            }
            var path = DecodeFilesystemPath(entry.Path);
            if (path is null)
            {
                AddPathFinding(findings, entry.Path, "invalid-path-encoding", FindingSeverity.Review, "working-tree path cannot be represented as Unicode", entry.ObjectId);
                continue;
            }
            var full = Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
            if (!full.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, PathComparison) && !full.Equals(root, PathComparison))
                throw new InvalidDataException("index path resolves outside the repository root");
            var candidate = InspectWorktreePath(root, path);
            if (candidate.LinkTarget is not null)
            {
                ScanBytes(findings, entry.Path, Encoding.UTF8.GetBytes(candidate.LinkTarget), null);
                continue;
            }
            if (candidate.UnknownReparsePoint) throw new IOException($"cannot inspect worktree link metadata safely for {EscapePath(entry.Path)}");
            if (!candidate.Exists || candidate.IsDirectory) continue;
            byte[] bytes;
            try { bytes = File.ReadAllBytes(full); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { throw new IOException($"cannot read candidate path {EscapePath(entry.Path)}", ex); }
            ScanBytes(findings, entry.Path, bytes, null);
        }
        return findings;
    }

    private static WorktreePathInfo InspectWorktreePath(string root, string relativePath)
    {
        var current = root;
        var components = relativePath.Split('/');
        for (var i = 0; i < components.Length; i++)
        {
            current = Path.Combine(current, components[i]);
            var isLeaf = i == components.Length - 1;
            var linkTarget = ReadLinkTarget(isLeaf ? new FileInfo(current) : new DirectoryInfo(current));
            if (linkTarget is null && isLeaf) linkTarget = ReadLinkTarget(new DirectoryInfo(current));
            if (linkTarget is not null) return new WorktreePathInfo(linkTarget, true, false, false);

            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            { return new WorktreePathInfo(null, false, false, false); }
            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            if ((attributes & FileAttributes.ReparsePoint) != 0) return new WorktreePathInfo(null, true, isDirectory, true);
            if (!isLeaf && !isDirectory) return new WorktreePathInfo(null, false, false, false);
            if (isLeaf) return new WorktreePathInfo(null, true, isDirectory, false);
        }
        return new WorktreePathInfo(null, false, false, false);
    }

    private static string? ReadLinkTarget(FileSystemInfo info)
    {
        try { return info.LinkTarget; }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }

    private static List<PathFinding> ScanIndex(string repository)
    {
        var entries = ParseIndex(Git(repository, "ls-files", "--stage", "-z").Output);
        if (entries.Any(e => e.Stage != 0)) throw new InvalidDataException("unresolved index conflict");
        var findings = new List<PathFinding>();
        var blobs = ReadBlobs(repository, entries.Select(e => e.ObjectId).Distinct(StringComparer.Ordinal).ToArray());
        foreach (var entry in entries)
        {
            if (entry.Mode == "160000")
            {
                AddPathFinding(findings, entry.Path, "gitlink", FindingSeverity.Review, "submodule contents are not inspected by this scan", entry.ObjectId);
                continue;
            }
            if (!blobs.TryGetValue(entry.ObjectId, out var bytes)) throw new InvalidDataException("index blob is missing");
            ScanBytes(findings, entry.Path, bytes, entry.ObjectId);
        }
        return findings;
    }

    private static List<PathFinding> ScanTip(string repository, string commit)
    {
        var entries = ParseTree(Git(repository, "ls-tree", "-r", "-z", "--full-tree", commit).Output);
        return ScanObjects(repository, entries);
    }

    private static List<PathFinding> ScanRange(string repository, string baseCommit, string headCommit)
    {
        var findings = new List<PathFinding>();
        var commits = SplitLines(Git(repository, "rev-list", "--topo-order", headCommit, "^" + baseCommit).Output);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var commit in commits)
        {
            var ancestry = SplitLines(Git(repository, "rev-list", "--parents", "-n", "1", commit).Output).Single().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var parents = ancestry.Skip(1).ToArray();
            if (parents.Length == 0)
            {
                AddIntroducedFromDiff(repository, ["--root", commit], seen, findings);
                continue;
            }
            foreach (var parent in parents) AddIntroducedFromDiff(repository, [parent, commit], seen, findings);
        }
        findings.AddRange(ScanTip(repository, headCommit));
        return findings;
    }

    private static void AddIntroducedFromDiff(string repository, string[] endpoints, HashSet<string> seen, List<PathFinding> findings)
    {
        var args = new List<string> { "diff-tree", "-r", "--raw", "-z", "--no-commit-id", "--no-renames" };
        if (endpoints[0] == "--root") { args.Add("--root"); args.Add(endpoints[1]); }
        else { args.Add(endpoints[0]); args.Add(endpoints[1]); }
        var fields = SplitNulBytes(Git(repository, args.ToArray()).Output);
        if (fields.Count % 2 != 0) throw new InvalidDataException("raw Git diff has incomplete NUL framing");
        var candidates = new List<(string Path, string Oid)>();
        foreach (var (headerBytes, pathBytes) in fields.Chunk(2).Select(pair => (pair[0], pair[1])))
        {
            var header = Encoding.ASCII.GetString(headerBytes);
            if (!header.StartsWith(':')) throw new InvalidDataException("raw Git diff header is malformed");
            var pieces = header[1..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (pieces.Length != 5 || pieces[4] is not ("A" or "M" or "T" or "C" or "R")) continue;
            var oid = pieces[3];
            if (oid.All(c => c == '0')) continue;
            var key = Convert.ToHexString(pathBytes) + ":" + oid;
            if (seen.Add(key)) candidates.Add((DecodeDisplayPath(pathBytes), oid));
        }
        var blobs = ReadBlobs(repository, candidates.Select(c => c.Oid).Distinct(StringComparer.Ordinal).ToArray());
        foreach (var candidate in candidates)
        {
            if (!blobs.TryGetValue(candidate.Oid, out var bytes)) throw new InvalidDataException("range destination blob is missing");
            ScanBytes(findings, candidate.Path, bytes, candidate.Oid);
        }
    }

    private static List<PathFinding> ScanObjects(string repository, IReadOnlyList<IndexEntry> entries)
    {
        var findings = new List<PathFinding>();
        var blobs = ReadBlobs(repository, entries.Where(e => e.Mode != "160000").Select(e => e.ObjectId).Distinct(StringComparer.Ordinal).ToArray());
        foreach (var entry in entries)
        {
            if (entry.Mode == "160000")
            {
                AddPathFinding(findings, entry.Path, "gitlink", FindingSeverity.Review, "submodule contents are not inspected by this scan", entry.ObjectId);
                continue;
            }
            if (!blobs.TryGetValue(entry.ObjectId, out var bytes)) throw new InvalidDataException("tree blob is missing");
            ScanBytes(findings, entry.Path, bytes, entry.ObjectId);
        }
        return findings;
    }

    private static void ScanBytes(List<PathFinding> target, string path, byte[] bytes, string? objectId)
    {
        foreach (var finding in Inspect(path, bytes))
            target.Add(new PathFinding(path, finding, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()));
    }

    private static void AddPathFinding(List<PathFinding> target, string path, string rule, FindingSeverity severity, string explanation, string digest)
        => target.Add(new PathFinding(path, new GrammarFinding(rule, severity, explanation), digest));

    private static List<IndexEntry> ParseIndex(byte[] output)
    {
        var fields = SplitNulBytes(output);
        var entries = new List<IndexEntry>(fields.Count);
        foreach (var field in fields)
        {
            var tab = Array.IndexOf(field, (byte)'\t');
            if (tab < 0) throw new InvalidDataException("index entry has no path separator");
            var header = Encoding.ASCII.GetString(field, 0, tab).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (header.Length != 3 || !int.TryParse(header[2], out var stage)) throw new InvalidDataException("index entry is malformed");
            entries.Add(new IndexEntry(DecodeDisplayPath(field[(tab + 1)..]), header[0], header[1], stage));
        }
        return entries;
    }

    private static List<IndexEntry> ParseTree(byte[] output)
    {
        var entries = new List<IndexEntry>();
        foreach (var field in SplitNulBytes(output))
        {
            var tab = Array.IndexOf(field, (byte)'\t');
            if (tab < 0) throw new InvalidDataException("tree entry has no path separator");
            var header = Encoding.ASCII.GetString(field, 0, tab).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (header.Length != 3 || header[1] is not ("blob" or "commit")) throw new InvalidDataException("tree entry is malformed");
            entries.Add(new IndexEntry(DecodeDisplayPath(field[(tab + 1)..]), header[0], header[2], 0));
        }
        return entries;
    }

    private static Dictionary<string, byte[]> ReadBlobs(string repository, IReadOnlyList<string> objectIds)
    {
        var results = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (objectIds.Count == 0) return results;
        var input = Encoding.ASCII.GetBytes(string.Join('\n', objectIds) + "\n");
        var output = Git(repository, ["cat-file", "--batch"], input).Output;
        var offset = 0;
        while (offset < output.Length)
        {
            var newline = Array.IndexOf(output, (byte)'\n', offset);
            if (newline < 0) throw new InvalidDataException("cat-file batch header is truncated");
            var header = Encoding.ASCII.GetString(output, offset, newline - offset).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (header.Length != 3 || header[1] != "blob" || !long.TryParse(header[2], out var size) || size < 0 || size > int.MaxValue)
                throw new InvalidDataException("cat-file returned a missing or non-blob object");
            var oid = header[0]; offset = newline + 1;
            if (size > output.Length - offset - 1 || output[offset + (int)size] != (byte)'\n') throw new InvalidDataException("cat-file batch content framing is invalid");
            results[oid] = output.AsSpan(offset, (int)size).ToArray(); offset += (int)size + 1;
        }
        if (results.Count != objectIds.Distinct(StringComparer.Ordinal).Count()) throw new InvalidDataException("cat-file batch omitted an object");
        return results;
    }

    private static string ResolveCommit(string repository, string revision)
    {
        if (string.IsNullOrWhiteSpace(revision) || revision.StartsWith('-')) throw new ArgumentException("commit arguments must be revision expressions, not options");
        var resolved = Encoding.ASCII.GetString(Git(repository, "rev-parse", "--verify", revision + "^{commit}").Output).Trim();
        if (resolved.Length is not (40 or 64) || !resolved.All(Uri.IsHexDigit)) throw new InvalidDataException("Git did not resolve a full commit object ID");
        return resolved;
    }

    private static GitResult Git(string repository, params string[] arguments) => RunGit(repository, arguments, null, false);

    private static GitResult Git(string repository, string[] arguments, byte[]? input) => RunGit(repository, arguments, input, false);
    private static GitResult GitOptional(string repository, params string[] arguments) => RunGit(repository, arguments, null, true);

    private static GitResult RunGit(string repository, string[] arguments, byte[]? input, bool allowFailure)
    {
        var start = new ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        start.ArgumentList.Add("-C"); start.ArgumentList.Add(repository);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("could not start Git");
        using var stdout = new MemoryStream(); using var stderr = new MemoryStream();
        var outTask = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        var errTask = process.StandardError.BaseStream.CopyToAsync(stderr);
        if (input is { Length: > 0 }) process.StandardInput.BaseStream.Write(input);
        process.StandardInput.Close();
        Task.WaitAll(outTask, errTask); process.WaitForExit();
        var result = new GitResult(process.ExitCode, stdout.ToArray(), stderr.ToArray());
        if (result.ExitCode != 0 && !allowFailure) throw new IOException($"Git {arguments[0]} failed: {SafeMessage(DecodeText(result.Error) ?? "non-text error")}");
        return result;
    }

    private static List<byte[]> SplitNulBytes(byte[] bytes)
    {
        var fields = new List<byte[]>(); var start = 0;
        for (var i = 0; i < bytes.Length; i++) if (bytes[i] == 0) { fields.Add(bytes.AsSpan(start, i - start).ToArray()); start = i + 1; }
        if (start != bytes.Length) throw new InvalidDataException("Git NUL-framed output is truncated");
        return fields;
    }

    private static List<string> SplitLines(byte[] bytes) => Encoding.ASCII.GetString(bytes).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    private static string DecodeDisplayPath(byte[] path) => Encoding.Latin1.GetString(path);
    private static string? DecodeFilesystemPath(string path)
    {
        try { return StrictUtf8.GetString(Encoding.Latin1.GetBytes(path)); }
        catch (DecoderFallbackException) { return null; }
    }
    private static string EscapePath(string path)
    {
        var builder = new StringBuilder(path.Length);
        foreach (var c in path) builder.Append(char.IsControl(c) || c == '\\' ? $"\\u{(int)c:X4}" : c.ToString());
        return builder.ToString();
    }
    private static string SafeMessage(string message) => Regex.Replace(message, @"[\r\n\0]+", " ").Trim();
    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
}
