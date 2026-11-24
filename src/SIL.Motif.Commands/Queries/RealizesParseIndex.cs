using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Host;
using SIL.Motif.Host.PanGloss;

namespace SIL.Motif.Commands.Queries;

/// <summary>The parse index could not be built: <c>pangloss batch</c> failed, or what it wrote is malformed.</summary>
internal sealed class RealizesIndexException(string message) : Exception(message);

/// <summary>How the grammar answered one word of the index.</summary>
internal enum IndexedStatus
{
    /// <summary>The parse finished; the word has the analyses listed, possibly none.</summary>
    Ok,

    /// <summary>The parse stopped at the work limit, so analyses may be missing.</summary>
    Capped,

    /// <summary>The parse stopped at the word timeout, so analyses may be missing.</summary>
    TimedOut,

    /// <summary>The grammar's characters cannot segment the word.</summary>
    Unsegmentable,

    /// <summary>The batch wrote no answer for the word.</summary>
    Failed,
}

/// <summary>One run of surface text a morph occupies: where it starts in the analysis's surface (UTF-16 index) and its text, as PanGloss gives it.</summary>
internal readonly record struct IndexedRun(int Start, string Text);

/// <summary>
/// One morph of an indexed analysis, named by its form and analysis (MSA) ids as <c>pangloss batch</c> reports them, and where it sits on the
/// surface. <paramref name="Runs"/> is null when PanGloss could place no sound span, and <paramref name="Reason"/> then names why.
/// </summary>
internal sealed record IndexedMorph(string Form, string Msa, IReadOnlyList<IndexedRun>? Runs = null, string? Reason = null);

/// <summary>
/// One reading of a word. <paramref name="Surface"/> is the NFD lower-case surface its spans index; <paramref name="Problem"/> is null when
/// the morph and unattributed runs tile that surface and the surface is the word, else the typed reason the spans cannot be trusted.
/// </summary>
internal sealed record IndexedAnalysis(IReadOnlyList<IndexedMorph> Morphs, string Surface = "", string? Problem = null);

/// <summary>One word's answer in the index.</summary>
internal sealed record IndexedWord(IndexedStatus Status, IReadOnlyList<IndexedAnalysis> Analyses);

/// <summary>
/// Every word type of a corpus parsed once by <c>pangloss batch --analyses --morph-spans</c>: for each word, the morphs of each analysis
/// with the surface text each occupies, and whether the parse finished. A morpheme's piece is read from here; nothing is traced.
/// </summary>
internal sealed class RealizesParseIndex
{
    /// <summary>The format tag; a file with another header is not read, so an index of an older format is rebuilt, never misread.</summary>
    internal const string Header = "motif-realizes-parse-index/v2";

    /// <summary>The cache file suffix for this format, so older formats are never opened under the same key.</summary>
    internal const string FileTag = "v2";

    public RealizesParseIndex(IReadOnlyDictionary<string, IndexedWord> words) => Words = words;

    /// <summary>The answer for each word type.</summary>
    public IReadOnlyDictionary<string, IndexedWord> Words { get; }

    /// <summary>Reads the <c>fieldworks-parse-analysis/v1</c> lines <c>pangloss batch --analyses --morph-spans</c> wrote; a word with no line is <see cref="IndexedStatus.Failed"/>.</summary>
    public static RealizesParseIndex FromBatch(string analysesPath, IEnumerable<string> words)
    {
        var read = new Dictionary<string, IndexedWord>(StringComparer.Ordinal);
        var number = 0;
        foreach (var line in File.ReadLines(analysesPath))
        {
            number++;
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                using var document = JsonDocument.Parse(line);
                var row = document.RootElement;
                var word = row.GetProperty("word").GetString() ?? throw new KeyNotFoundException("word");
                var status = Flag(row, "invalidShape") ? IndexedStatus.Unsegmentable : Flag(row, "timedOut") ? IndexedStatus.TimedOut
                    : Flag(row, "capped") ? IndexedStatus.Capped : IndexedStatus.Ok;
                var analyses = new List<IndexedAnalysis>();
                if (row.TryGetProperty("analyses", out var listed) && listed.ValueKind == JsonValueKind.Array)
                    foreach (var analysis in listed.EnumerateArray()) analyses.Add(ReadAnalysis(word, analysis));
                read[word] = new IndexedWord(status, analyses);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or ArgumentException)
            {
                throw new RealizesIndexException($"line {number} of the analyses pangloss batch wrote is malformed ({exception.Message}).");
            }
        }
        var all = new Dictionary<string, IndexedWord>(StringComparer.Ordinal);
        foreach (var word in words) all[word] = read.GetValueOrDefault(word) ?? new IndexedWord(IndexedStatus.Failed, []);
        return new RealizesParseIndex(all);
    }

    private const string SpansNull = "morph_spans_null";
    private const string SpansAbsent = "morph_spans_absent";
    internal const string SurfaceDiffers = "surface_differs_from_word";
    internal const string TilingBroken = "spans_do_not_tile_surface";

    private static IndexedAnalysis ReadAnalysis(string word, JsonElement analysis)
    {
        var morphs = analysis.GetProperty("morphs").EnumerateArray().ToList();
        if (!analysis.TryGetProperty("morphSpans", out var spans) || spans.ValueKind != JsonValueKind.Object)
        {
            var why = spans.ValueKind == JsonValueKind.Null ? SpansNull : SpansAbsent;
            return new IndexedAnalysis([.. morphs.Select(morph => new IndexedMorph(Text(morph, "form"), Text(morph, "msa"), null, why))]);
        }
        var surface = spans.GetProperty("surface").GetString() ?? "";
        var bytes = Encoding.UTF8.GetBytes(surface);
        int CharIndex(int offset) => Encoding.UTF8.GetString(bytes, 0, offset).Length;
        var covered = new List<(int Start, int End, string Text)>();
        void Cover(JsonElement run)
        {
            var offsets = run.GetProperty("offsets");
            var text = run.GetProperty("text").GetString() ?? "";
            covered.Add((offsets[0].GetInt32(), offsets[1].GetInt32(), text));
        }
        var runsOf = new Dictionary<int, (List<IndexedRun>? Runs, string? Reason)>();
        foreach (var morpheme in spans.GetProperty("morphemes").EnumerateArray())
        {
            var item = morpheme.GetProperty("item").GetInt32();
            var list = morpheme.GetProperty("spans");
            if (list.ValueKind != JsonValueKind.Array)
            {
                var reason = morpheme.TryGetProperty("reason", out var named) && named.ValueKind == JsonValueKind.String ? named.GetString()! : SpansNull;
                runsOf[item] = (null, reason);
                continue;
            }
            var runs = new List<IndexedRun>();
            foreach (var run in list.EnumerateArray())
            {
                Cover(run);
                runs.Add(new IndexedRun(CharIndex(run.GetProperty("offsets")[0].GetInt32()), run.GetProperty("text").GetString() ?? ""));
            }
            runsOf[item] = (runs, null);
        }
        var unattributedNull = false;
        if (spans.TryGetProperty("unattributed", out var unattributed) && unattributed.ValueKind == JsonValueKind.Array)
            foreach (var run in unattributed.EnumerateArray())
                if (run.GetProperty("offsets").ValueKind == JsonValueKind.Array) Cover(run); else unattributedNull = true;
        var problem = Tiles(covered, bytes, surface, unattributedNull) ? null : TilingBroken;
        if (problem is null && !string.Equals(surface, word.Normalize(NormalizationForm.FormD).ToLowerInvariant(), StringComparison.Ordinal))
            problem = SurfaceDiffers;
        return new IndexedAnalysis([.. morphs.Select((morph, at) =>
        {
            var (runs, reason) = runsOf.GetValueOrDefault(at, (null, "morph_not_in_spans"));
            return new IndexedMorph(Text(morph, "form"), Text(morph, "msa"), runs, runs is null ? reason : null);
        })], surface, problem);
    }

    // The runs, in order, must leave no gap or overlap from the first byte to the last and spell the surface.
    private static bool Tiles(List<(int Start, int End, string Text)> covered, byte[] bytes, string surface, bool unattributedNull)
    {
        if (unattributedNull) return false;
        var at = 0;
        var spelled = new StringBuilder();
        foreach (var (start, end, text) in covered.OrderBy(run => run.Start).ThenBy(run => run.End))
        {
            if (start != at || end < start || end > bytes.Length) return false;
            if (!string.Equals(text, Encoding.UTF8.GetString(bytes, start, end - start), StringComparison.Ordinal)) return false;
            spelled.Append(text);
            at = end;
        }
        return at == bytes.Length && spelled.ToString() == surface;
    }

    private static bool Flag(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string Text(JsonElement morph, string name) =>
        morph.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    /// <summary>Writes the index as gzip text: the header, then one JSON line per word.</summary>
    public void Write(Stream destination)
    {
        using var zip = new GZipStream(destination, CompressionLevel.Fastest, leaveOpen: true);
        using var writer = new StreamWriter(zip, new UTF8Encoding(false)) { NewLine = "\n" };
        writer.WriteLine(Header);
        foreach (var (word, entry) in Words)
        {
            using var buffer = new MemoryStream();
            using (var json = new Utf8JsonWriter(buffer))
            {
                json.WriteStartObject();
                json.WriteString("w", word);
                json.WriteNumber("s", (int)entry.Status);
                json.WriteStartArray("a");
                foreach (var analysis in entry.Analyses)
                {
                    json.WriteStartObject();
                    json.WriteString("u", analysis.Surface);
                    if (analysis.Problem is not null) json.WriteString("p", analysis.Problem);
                    json.WriteStartArray("m");
                    foreach (var morph in analysis.Morphs)
                    {
                        json.WriteStartObject();
                        json.WriteString("f", morph.Form);
                        json.WriteString("i", morph.Msa);
                        if (morph.Runs is null) json.WriteString("x", morph.Reason ?? SpansNull);
                        else
                        {
                            json.WriteStartArray("r");
                            foreach (var run in morph.Runs)
                            {
                                json.WriteStartArray();
                                json.WriteNumberValue(run.Start);
                                json.WriteStringValue(run.Text);
                                json.WriteEndArray();
                            }
                            json.WriteEndArray();
                        }
                        json.WriteEndObject();
                    }
                    json.WriteEndArray();
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            writer.WriteLine(Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length));
        }
    }

    /// <summary>Reads an index written by <see cref="Write"/>; null when the stream is not one of this format.</summary>
    public static RealizesParseIndex? Read(Stream source)
    {
        using var zip = new GZipStream(source, CompressionMode.Decompress);
        using var reader = new StreamReader(zip, Encoding.UTF8);
        if (reader.ReadLine() != Header) return null;
        var words = new Dictionary<string, IndexedWord>(StringComparer.Ordinal);
        try
        {
            while (reader.ReadLine() is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var row = document.RootElement;
                var analyses = new List<IndexedAnalysis>();
                foreach (var analysis in row.GetProperty("a").EnumerateArray())
                {
                    var morphs = new List<IndexedMorph>();
                    foreach (var morph in analysis.GetProperty("m").EnumerateArray())
                    {
                        List<IndexedRun>? runs = null;
                        if (morph.TryGetProperty("r", out var listed))
                            runs = [.. listed.EnumerateArray().Select(run => new IndexedRun(run[0].GetInt32(), run[1].GetString() ?? ""))];
                        morphs.Add(new IndexedMorph(morph.GetProperty("f").GetString() ?? "", morph.GetProperty("i").GetString() ?? "", runs,
                            runs is null && morph.TryGetProperty("x", out var reason) ? reason.GetString() : null));
                    }
                    analyses.Add(new IndexedAnalysis(morphs, analysis.GetProperty("u").GetString() ?? "",
                        analysis.TryGetProperty("p", out var problem) ? problem.GetString() : null));
                }
                var status = row.GetProperty("s").GetInt32();
                if (!Enum.IsDefined((IndexedStatus)status)) return null;
                words[row.GetProperty("w").GetString() ?? throw new KeyNotFoundException("w")] = new IndexedWord((IndexedStatus)status, analyses);
            }
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or FormatException)
        {
            return null;
        }
        return new RealizesParseIndex(words);
    }
}

/// <summary>Runs <c>pangloss batch --analyses</c> over a word list; a test replays a recording instead.</summary>
internal interface IRealizesIndexBuilder
{
    /// <summary>What a cached index depends on beyond the grammar and the words: the parser build.</summary>
    string Identity { get; }

    /// <summary>Parses <paramref name="words"/> with the grammar and returns the path of the <c>--analyses</c> file; the caller deletes the folder around it.</summary>
    string Build(string grammarPath, IReadOnlyList<string> words, CancellationToken cancellationToken);
}

/// <summary>The real builder: one <c>pangloss batch</c> child process with a few threads.</summary>
internal sealed class PanGlossIndexBuilder(string executable) : IRealizesIndexBuilder
{
    private const int WordTimeoutMs = 30000;

    public string Identity { get; } = BatchInvocationEvidence.DigestFile(executable);

    public string Build(string grammarPath, IReadOnlyList<string> words, CancellationToken cancellationToken)
    {
        RequireMorphSpans(Describe(cancellationToken));
        var folder = Directory.CreateTempSubdirectory("motif-realizes-index").FullName;
        var wordsPath = Path.Combine(folder, "words.txt");
        var analyses = Path.Combine(folder, "analyses.jsonl");
        File.WriteAllLines(wordsPath, words);
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in new[]
                 {
                     "batch", grammarPath, wordsPath, Path.Combine(folder, "out.tsv"), "--word-timeout-ms", WordTimeoutMs.ToString(),
                     "--threads", Math.Clamp(Environment.ProcessorCount / 2, 1, 4).ToString(), "--analyses", analyses, "--morph-spans",
                 })
            start.ArgumentList.Add(argument);
        var kept = false;
        try
        {
            Process process;
            try { process = Process.Start(start) ?? throw new RealizesIndexException("pangloss batch did not start."); }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
            {
                throw new RealizesIndexException($"pangloss batch did not start: {exception.Message}");
            }
            using (process)
            {
                var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
                process.StandardOutput.ReadToEndAsync(CancellationToken.None);
                try { process.WaitForExitAsync(cancellationToken).GetAwaiter().GetResult(); }
                catch (OperationCanceledException)
                {
                    try { process.Kill(true); } catch (InvalidOperationException) { }
                    throw;
                }
                if (process.ExitCode != 0 || !File.Exists(analyses))
                    throw new RealizesIndexException($"pangloss batch exited {process.ExitCode} and wrote no analyses. " + Tail(errors.GetAwaiter().GetResult()));
            }
            kept = true;
            return analyses;
        }
        finally
        {
            if (!kept) TryDelete(folder);
        }
    }

    /// <summary>The flag a parser's <c>batch</c> must list for the index to carry surface spans; PanGloss v0.34.0 added it.</summary>
    internal const string MorphSpansFlag = "--morph-spans";

    /// <summary>Throws the typed refusal when the <c>--describe</c> document does not list <c>batch --morph-spans</c>.</summary>
    internal static void RequireMorphSpans(string? describeJson)
    {
        var listed = false;
        try
        {
            if (describeJson is not null)
            {
                using var document = JsonDocument.Parse(describeJson);
                if (document.RootElement.TryGetProperty("commands", out var commands) && commands.ValueKind == JsonValueKind.Array)
                    foreach (var command in commands.EnumerateArray())
                        if (command.TryGetProperty("name", out var name) && name.GetString() == "batch" && command.TryGetProperty("flags", out var flags) &&
                            flags.ValueKind == JsonValueKind.Array)
                            listed = flags.EnumerateArray().Any(flag => flag.TryGetProperty("name", out var flagName) && flagName.GetString() == MorphSpansFlag);
            }
        }
        catch (JsonException) { }
        if (!listed)
            throw new RealizesIndexException("needs-pangloss-v0.34.0: this PanGloss does not list `batch --morph-spans`, which realizes needs to read each morpheme's " +
                "surface piece from the corpus parse. Use the PanGloss release Motif pins (v0.34.0 or later).");
    }

    private string? Describe(CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(executable, "--describe") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        try
        {
            using var process = Process.Start(start);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            process.StandardError.ReadToEndAsync(CancellationToken.None);
            if (!process.WaitForExit(15000))
            {
                try { process.Kill(true); } catch (InvalidOperationException) { }
                return null;
            }
            cancellationToken.ThrowIfCancellationRequested();
            return process.ExitCode == 0 ? output.GetAwaiter().GetResult() : null;
        }
        catch (System.ComponentModel.Win32Exception) { return null; }
    }

    private static string Tail(string text) => text.Length <= 400 ? text.Trim() : "..." + text[^400..].Trim();

    private static void TryDelete(string folder)
    {
        try { Directory.Delete(folder, true); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }
}

/// <summary>
/// The parse index of a corpus, kept under the user's Motif folder and keyed by the grammar's bytes, the corpus's word types and
/// the parser build, so it is rebuilt exactly when one of them changes.
/// </summary>
internal static class RealizesParseIndexCache
{
    /// <summary>Where the indexes are kept.</summary>
    public static string DefaultRoot => Path.Combine(UserDataHome.Path, "SIL", "Motif", "cache", "realizes-parse-index");

    /// <summary>The file name for one grammar, word list and parser build.</summary>
    public static string KeyFor(string grammarPath, IEnumerable<string> words, string parserIdentity)
    {
        var grammar = Bare(BatchInvocationEvidence.DigestFile(grammarPath));
        var corpus = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", words.OrderBy(word => word, StringComparer.Ordinal)))));
        return $"{grammar[..16]}-{corpus[..16]}-{Bare(parserIdentity)[..16]}.{RealizesParseIndex.FileTag}.gz";
    }

    /// <summary>How many parse indexes stay under the cache folder; the newest by modified time.</summary>
    internal const int KeepIndexes = 8;

    private static void Prune(string root, string written)
    {
        try
        {
            foreach (var old in new DirectoryInfo(root).EnumerateFiles("*.gz").Where(file => file.FullName != written)
                         .OrderByDescending(file => file.LastWriteTimeUtc).Skip(KeepIndexes - 1))
                old.Delete();
            foreach (var orphan in new DirectoryInfo(root).EnumerateFiles("*.tmp").Where(file => file.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1)))
                orphan.Delete();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static string Bare(string digest) => digest[(digest.IndexOf(':') + 1)..];

    /// <summary>The cached index, or one built now; <paramref name="notice"/> hears before a slow build starts.</summary>
    public static (RealizesParseIndex Index, bool Built, double Seconds) GetOrBuild(string root, string grammarPath, IReadOnlyList<string> words,
        IRealizesIndexBuilder builder, Action<string> notice, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var path = Path.Combine(root, KeyFor(grammarPath, words, builder.Identity));
        if (File.Exists(path))
        {
            string? why = null;
            try
            {
                using var stream = File.OpenRead(path);
                if (RealizesParseIndex.Read(stream) is { } cached) return (cached, false, Math.Round(clock.Elapsed.TotalSeconds, 1));
                why = "it is not a parse index";
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or FormatException)
            {
                why = exception.Message;
            }
            notice($"discarding the cached parse index {Path.GetFileName(path)} ({why}); rebuilding it");
            try { File.Delete(path); } catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        notice($"building parse index for {words.Count} word types (one-off per grammar, corpus and PanGloss build; a large grammar takes about a minute and a half)");
        var analyses = builder.Build(grammarPath, words, cancellationToken);
        try
        {
            var index = RealizesParseIndex.FromBatch(analyses, words);
            Directory.CreateDirectory(root);
            var temporary = path + "." + Environment.ProcessId + ".tmp";
            using (var stream = File.Create(temporary)) index.Write(stream);
            File.Move(temporary, path, true);
            Prune(root, path);
            return (index, true, Math.Round(clock.Elapsed.TotalSeconds, 1));
        }
        finally
        {
            try { Directory.Delete(Path.GetDirectoryName(analyses)!, true); } catch (IOException) { }
        }
    }
}
