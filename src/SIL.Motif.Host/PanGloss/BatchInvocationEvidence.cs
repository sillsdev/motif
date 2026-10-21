using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using SIL.Motif.Contract.Assess;

namespace SIL.Motif.Host.PanGloss;

/// <summary>Observed input and artifact bytes for one completed batch invocation.</summary>
public sealed record BatchInvocationEvidence(
    string InvocationId,
    string SourcePath,
    string SourceBytesSha256,
    string ExecutableBytesSha256,
    string WordsPath,
    string WordsSha256,
    string TsvPath,
    string TsvSha256,
    string StandardErrorPath,
    string StandardErrorSha256,
    int? PerWordTimeoutMs,
    StepCap PerWordStepLimit,
    int Threads,
    bool CollectStatistics)
{
    internal sealed record TextSnapshot(string Text, string Sha256);
    internal sealed record BatchFileDigests(string? TsvSha256, string? AnalysesSha256);

    /// <summary>The version the executable that ran this invocation reported, or null when recorded before this existed.</summary>
    /// <remarks>
    /// With <see cref="ExecutableBytesSha256"/> this is the parser identity <c>compare</c> checks, so two
    /// Assessments made by different parsers are named as such.
    /// </remarks>
    public string? ExecutableVersion { get; init; }

    /// <summary>Where the executable that ran this invocation was; information only, never compared.</summary>
    public string? ExecutablePath { get; init; }

    public string? AnalysesPath { get; init; }
    public string? AnalysesSha256 { get; init; }

    /// <summary>
    /// Set when words the parser stopped at the per-word time limit were run again in one serial batch under a longer limit and
    /// their rows in the retained TSV and morphology files were replaced by the retried rows; null when none were.
    /// </summary>
    public StoppedWordRetry? StoppedWordRetry { get; init; }

    /// <summary>PanGloss stderr lines retained with the invocation, newline-joined or null when none were retained.</summary>
    /// <remarks>
    /// Joined rather than listed because a record's value equality is what lets one invocation be recorded once per
    /// kind and compared for agreement; a collection member would compare by reference and refuse the second write.
    /// </remarks>
    public string? GrammarWarnings { get; init; }

    /// <summary>The retained PanGloss stderr lines in order.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> GrammarWarningLines => string.IsNullOrEmpty(GrammarWarnings)
        ? []
        : GrammarWarnings.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>Hashes a file's bytes, independently of parser semantic identities.</summary>
    public static string DigestFile(string path)
    {
        using var stream = File.OpenRead(path);
        return "sha256:" + Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    internal static TextSnapshot ReadTextWithDigest(string path)
    {
        using var stream = new DigestingReadStream(File.OpenRead(path));
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096, leaveOpen: true);
        return new TextSnapshot(reader.ReadToEnd(), stream.Digest);
    }

    internal static bool TextMatchesDigest(string? text, string? digest)
    {
        if (text is null || digest is null || text.StartsWith('\uFEFF')) return false;
        return DigestText(text, includePreamble: false) == digest ||
            DigestText(text, includePreamble: true) == digest;
    }

    private static string DigestText(string text, bool includePreamble)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        if (includePreamble) hash.AppendData(Encoding.UTF8.Preamble);
        var encoder = Encoding.UTF8.GetEncoder();
        var bytes = new byte[4096];
        var offset = 0;
        do
        {
            encoder.Convert(text.AsSpan(offset), bytes, flush: true,
                out var charsUsed, out var bytesUsed, out var completed);
            if (bytesUsed > 0) hash.AppendData(bytes.AsSpan(0, bytesUsed));
            offset += charsUsed;
            if (!completed && charsUsed == 0 && bytesUsed == 0)
                throw new InvalidOperationException("UTF-8 encoding made no progress.");
            if (completed) break;
        } while (true);
        return "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private sealed class DigestingReadStream(Stream inner) : Stream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public string Digest => "sha256:" + Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            if (read > 0) _hash.AppendData(buffer.AsSpan(offset, read));
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer);
            if (read > 0) _hash.AppendData(buffer[..read]);
            return read;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            ReadAsyncCore(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count,
            CancellationToken cancellationToken) => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _hash.Dispose();
                inner.Dispose();
            }
            base.Dispose(disposing);
        }

        private async ValueTask<int> ReadAsyncCore(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0) _hash.AppendData(buffer.Span[..read]);
            return read;
        }
    }
}

/// <summary>
/// The record that some words were measured twice. The first pass stopped them at the per-word time limit; a
/// second invocation of the same parser on the same staged source ran only those words, serially, under
/// <see cref="LimitMs"/>. The retained TSV and morphology files hold the retried rows for those words, and the
/// first-pass files are kept beside them as <c>first-pass.tsv</c> and <c>first-pass-analyses.jsonl</c>.
/// </summary>
/// <remarks>
/// Only primitive members, so two recordings of one invocation still compare equal. A recovered word's row is
/// the same parse the first pass would have produced had it been given more time; a word that stopped again
/// keeps its stopped row, so it still blocks Apply.
/// </remarks>
/// <param name="LimitMs">The longer per-word limit the retry ran under.</param>
/// <param name="Stopped">Words the first pass stopped at the time limit.</param>
/// <param name="Retried">Words actually run again; the rest exceeded <see cref="MaxWords"/>. When <see cref="Failure"/> is set, the words the retry was attempted on.</param>
/// <param name="Recovered">Retried words that completed.</param>
/// <param name="StillStopped">Retried words that stopped again.</param>
/// <param name="Words">One tab-separated line per stopped word: index, word, first-pass ms, retry ms or "-", and recovered, still-stopped or not-retried.</param>
public sealed record StoppedWordRetry(
    int LimitMs, int Stopped, int Retried, int Recovered, int StillStopped,
    string RetryInvocationId, string FirstPassTsvSha256, string FirstPassAnalysesSha256,
    string RetryTsvSha256, string RetryAnalysesSha256, string Words)
{
    /// <summary>How many times the per-word limit the retry allows.</summary>
    public const int Multiplier = 10;

    /// <summary>The most words one invocation retries, so a grammar that is slow on thousands of words is not rerun at length.</summary>
    public const int MaxWords = 50;

    public int NotRetried => Stopped - Retried;

    /// <summary>Why the retry gave nothing, when it was attempted and failed; the stopped rows then stay as the first pass left them.</summary>
    public string? Failure { get; init; }
}
