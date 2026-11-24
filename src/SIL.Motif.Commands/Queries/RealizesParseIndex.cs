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

/// <summary>One morph of an indexed analysis, named by its form and its analysis (MSA) ids as <c>pangloss batch</c> reports them.</summary>
internal readonly record struct IndexedMorph(string Form, string Msa);

/// <summary>One word's answer in the index.</summary>
internal sealed record IndexedWord(IndexedStatus Status, IReadOnlyList<IReadOnlyList<IndexedMorph>> Analyses);

/// <summary>
/// Every word type of a corpus parsed once by <c>pangloss batch --analyses</c>: for each word, the morphs of each analysis and
/// whether the parse finished. It carries no surface text; the surface piece of a morpheme comes from tracing the few words
/// that carry it.
/// </summary>
internal sealed class RealizesParseIndex
{
    private const string Header = "motif-realizes-parse-index/v1";

    public RealizesParseIndex(IReadOnlyDictionary<string, IndexedWord> words) => Words = words;

    /// <summary>The answer for each word type.</summary>
    public IReadOnlyDictionary<string, IndexedWord> Words { get; }

    /// <summary>Reads the <c>fieldworks-parse-analysis/v1</c> lines <c>pangloss batch --analyses</c> wrote; a word with no line is <see cref="IndexedStatus.Failed"/>.</summary>
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
                var analyses = new List<IReadOnlyList<IndexedMorph>>();
                if (row.TryGetProperty("analyses", out var listed) && listed.ValueKind == JsonValueKind.Array)
                    foreach (var analysis in listed.EnumerateArray())
                        analyses.Add([.. analysis.GetProperty("morphs").EnumerateArray()
                            .Select(morph => new IndexedMorph(Text(morph, "form"), Text(morph, "msa")))]);
                read[word] = new IndexedWord(status, analyses);
            }
            catch (Exception exception) when (exception is JsonException or KeyNotFoundException or InvalidOperationException)
            {
                throw new RealizesIndexException($"line {number} of the analyses pangloss batch wrote is malformed ({exception.Message}).");
            }
        }
        var all = new Dictionary<string, IndexedWord>(StringComparer.Ordinal);
        foreach (var word in words) all[word] = read.GetValueOrDefault(word) ?? new IndexedWord(IndexedStatus.Failed, []);
        return new RealizesParseIndex(all);
    }

    private static bool Flag(JsonElement row, string name) => row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string Text(JsonElement morph, string name) =>
        morph.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    /// <summary>Writes the index as gzip text: one line per word, its status, then its analyses.</summary>
    public void Write(Stream destination)
    {
        using var zip = new GZipStream(destination, CompressionLevel.Fastest, leaveOpen: true);
        using var writer = new StreamWriter(zip, new UTF8Encoding(false)) { NewLine = "\n" };
        writer.WriteLine(Header);
        foreach (var (word, entry) in Words)
            writer.WriteLine(word + "\t" + (int)entry.Status + "\t" + string.Join("|",
                entry.Analyses.Select(analysis => string.Join(",", analysis.Select(morph => morph.Form + ":" + morph.Msa)))));
    }

    /// <summary>Reads an index written by <see cref="Write"/>; null when the stream is not one.</summary>
    public static RealizesParseIndex? Read(Stream source)
    {
        using var zip = new GZipStream(source, CompressionMode.Decompress);
        using var reader = new StreamReader(zip, Encoding.UTF8);
        if (reader.ReadLine() != Header) return null;
        var words = new Dictionary<string, IndexedWord>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('\t');
            if (parts.Length != 3 || !int.TryParse(parts[1], out var status)) return null;
            var analyses = new List<IReadOnlyList<IndexedMorph>>();
            if (parts[2].Length > 0)
                foreach (var analysis in parts[2].Split('|'))
                {
                    var morphs = new List<IndexedMorph>();
                    foreach (var morph in analysis.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var at = morph.IndexOf(':');
                        if (at < 0) return null;
                        morphs.Add(new IndexedMorph(morph[..at], morph[(at + 1)..]));
                    }
                    analyses.Add(morphs);
                }
            words[parts[0]] = new IndexedWord((IndexedStatus)status, analyses);
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
                     "--threads", Math.Clamp(Environment.ProcessorCount / 2, 1, 4).ToString(), "--analyses", analyses,
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
        return $"{grammar[..16]}-{corpus[..16]}-{Bare(parserIdentity)[..16]}.gz";
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
