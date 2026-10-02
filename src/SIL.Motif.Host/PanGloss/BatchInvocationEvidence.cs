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

    public string? AnalysesPath { get; init; }
    public string? AnalysesSha256 { get; init; }

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
