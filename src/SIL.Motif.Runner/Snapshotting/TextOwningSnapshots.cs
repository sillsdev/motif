using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Snapshotting;

/// <summary>
/// Projects one Text's baseline, identities, occurrences and annotations without reading other Texts.
/// Read materializes the full structure; Summary hashes its fields as a stream for a compact effect.
/// </summary>
public static class TextOwningSnapshots
{
    /// <summary>The Text's full structure, including baseline, punctuation, translations and Note identities.</summary>
    public static ObjectSnapshot Read(LcmCache cache, IText text) => new(
        CanonicalId.FromGuid(cache.LangProject.Guid),
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            [SnapshotFields.LangProjectTexts] = Structure(cache, text)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
        });

    /// <summary>
    /// The compact effect: identity, name, counts and a length-framed SHA-256 digest in reading order.
    /// Writing system alternatives are ordered by tag, so cache-local handle order cannot affect the digest.
    /// Punctuation is identified by its form, because ParagraphParser creates its storage identities itself.
    /// </summary>
    public static Dictionary<string, string> Summary(LcmCache cache, IText text)
    {
        var summary = new Dictionary<string, string>(StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "text-structure-v2");
        var paragraphs = 0;
        var segments = 0;
        var words = 0;
        foreach (var (key, value) in Structure(cache, text))
        {
            if (key == "ref" || key.StartsWith("name:", StringComparison.Ordinal)) summary[key] = value;
            if (key.StartsWith("p:", StringComparison.Ordinal)) paragraphs++;
            if (key.StartsWith("s:", StringComparison.Ordinal)) segments++;
            if (key.StartsWith("w:", StringComparison.Ordinal)) words++;
            Append(hash, key);
            Append(hash, value);
        }
        summary["paragraphs"] = paragraphs.ToString(CultureInfo.InvariantCulture);
        summary["segments"] = segments.ToString(CultureInfo.InvariantCulture);
        summary["words"] = words.ToString(CultureInfo.InvariantCulture);
        summary["structure"] = "sha256:" + Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        return summary;
    }

    private static void Append(IncrementalHash hash, string value)
    {
        var count = Encoding.UTF8.GetByteCount(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, count);
        hash.AppendData(length);
        byte[]? rented = null;
        Span<byte> bytes = count <= 512 ? stackalloc byte[count] : (rented = ArrayPool<byte>.Shared.Rent(count)).AsSpan(0, count);
        try
        {
            Encoding.UTF8.GetBytes(value, bytes);
            hash.AppendData(bytes);
        }
        finally { if (rented is not null) ArrayPool<byte>.Shared.Return(rented); }
    }

    private static IEnumerable<(string Key, string Value)> Structure(LcmCache cache, IText text)
    {
        yield return ("ref", CanonicalId.FromGuid(text.Guid).Value);
        foreach (var handle in text.Name.AvailableWritingSystemIds.OrderBy(cache.WritingSystemFactory.GetStrFromWs, StringComparer.Ordinal))
        {
            var title = text.Name.get_String(handle).Text;
            if (!string.IsNullOrEmpty(title))
                yield return ("name:" + cache.WritingSystemFactory.GetStrFromWs(handle), title.Normalize(NormalizationForm.FormD));
        }
        var paragraphs = text.ContentsOA?.ParagraphsOS.OfType<IStTxtPara>() ?? [];
        var i = 0;
        foreach (var paragraph in paragraphs)
        {
            var paragraphKey = (i++).ToString(CultureInfo.InvariantCulture);
            yield return ("p:" + paragraphKey, CanonicalId.FromGuid(paragraph.Guid).Value);
            yield return ("text:" + paragraphKey, paragraph.Contents.Text ?? string.Empty);
            var j = 0;
            foreach (var segment in paragraph.SegmentsOS)
            {
                var segmentId = CanonicalId.FromGuid(segment.Guid).Value;
                yield return ("s:" + paragraphKey + "." + (j++).ToString(CultureInfo.InvariantCulture), segmentId);
                yield return ("offset:" + segmentId, segment.BeginOffset.ToString(CultureInfo.InvariantCulture));
                foreach (var pair in Alternatives(cache, segment.FreeTranslation, "free:" + segmentId)) yield return pair;
                foreach (var pair in Alternatives(cache, segment.LiteralTranslation, "literal:" + segmentId)) yield return pair;
                var note = 0;
                foreach (var item in segment.NotesOS)
                {
                    var key = "note:" + segmentId + ":" + (note++).ToString(CultureInfo.InvariantCulture);
                    yield return (key, CanonicalId.FromGuid(item.Guid).Value);
                    foreach (var pair in Alternatives(cache, item.Content, key)) yield return pair;
                }
                var k = 0;
                foreach (var analysis in segment.AnalysesRS)
                {
                    var key = segmentId + ":" + (k++).ToString(CultureInfo.InvariantCulture);
                    yield return analysis is IPunctuationForm punctuation
                        ? ("punct:" + key, punctuation.Form.Text ?? string.Empty)
                        : ("w:" + key, CanonicalId.FromGuid(analysis.Guid).Value);
                }
            }
        }
    }

    private static IEnumerable<(string Key, string Value)> Alternatives(LcmCache cache, IMultiString field, string prefix)
    {
        foreach (var ws in field.AvailableWritingSystemIds.OrderBy(cache.WritingSystemFactory.GetStrFromWs, StringComparer.Ordinal))
            yield return (prefix + ":" + cache.WritingSystemFactory.GetStrFromWs(ws), field.get_String(ws).Text ?? string.Empty);
    }
}
