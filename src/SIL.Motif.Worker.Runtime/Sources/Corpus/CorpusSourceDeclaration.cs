using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SIL.Motif.Host.Corpus;
using SIL.Scripture;

namespace SIL.Motif.Worker.Sources;

internal enum CorpusSourceFormat
{
    PlainText,
    Jsonl,
    Usfm,
    Sikor,
}

internal sealed record CorpusSegmentation(string WritingSystemId, string ProfileSha256, string TokeniserName,
    string TokeniserVersion)
{
    public string Id => CorpusContent.Digest(
        new[] { WritingSystemId, ProfileSha256.ToLowerInvariant(), TokeniserName, TokeniserVersion });

    public IWordSpanTokeniser Bind(IWordTokeniser tokeniser)
    {
        if (string.IsNullOrWhiteSpace(WritingSystemId) || !CorpusContent.IsHash(ProfileSha256) ||
            tokeniser.Name != TokeniserName || tokeniser.Version != TokeniserVersion ||
            tokeniser is not IWordSpanTokeniser { Profile: { } profile } spans)
            throw new InvalidDataException("The Corpus Source requires its declared segmentation profile and span tokeniser.");
        if (profile.WritingSystemId is not null && profile.WritingSystemId != WritingSystemId ||
            !string.Equals(profile.Sha256, ProfileSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The span tokeniser uses another writing system or segmentation profile.");
        return spans;
    }
}

internal sealed record CorpusUsfm(string Edition, ScrVers Versification, string EncodingName = "utf-8",
    string? StylesheetPath = null);

internal sealed record CorpusSikor(string[] DevIds, string DevIdsSha256, byte[] WitnessKey);

internal sealed record CorpusSourceDeclaration(string SourceId, CorpusSourceFormat Format, string Locator,
    CorpusSegmentation Segmentation, string Provenance, string[]? Files = null, string JsonlIdField = "id",
    CorpusUsfm? Usfm = null, CorpusSikor? Sikor = null);

internal sealed record CorpusSourceBinding(CorpusSourceFormat Format, string WritingSystem, string Segmentation,
    string Provenance, string ProviderVersion, string? Extraction, string? DevSplitDigest, bool Private);

internal sealed record CorpusFileVersion(string Name, string RawHash, long Bytes);

internal sealed record CorpusDocumentVersion(string Id, string Container, int Position, string RawHash,
    string TextHash, string? Metadata, long ShardId = 0, long Tokens = 0);

internal sealed record CorpusSegment(int Number, string Text, long? ByteOffset = null, string? Verse = null);

internal sealed record CorpusInputDocument(CorpusDocumentVersion Version, IEnumerable<CorpusSegment> Segments);

internal static class CorpusContent
{
    public static readonly UTF8Encoding Utf8 = new(false, true);

    public static string Digest<T>(T value) => Hash(JsonSerializer.SerializeToUtf8Bytes(value));

    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static string Hash(string value) => Hash(Utf8.GetBytes(value));

    public static bool IsHash(string value) => value is { Length: 64 } && value.All(char.IsAsciiHexDigit);

    public static string PrivateHash(byte[] key, string kind, string source, string value) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(key, JsonSerializer.SerializeToUtf8Bytes(new[] { kind, source, value })));
}
