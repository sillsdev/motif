using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Snapshotting;

/// <summary>
/// Projects one Text's identity, name and the identities of its paragraphs, segments and occurrences, never the
/// other Texts. The members are named Read and Summary, not Snapshot, so the Baseline's whole-model digest does not
/// discover them as generated snapshotters.
/// </summary>
public static class TextOwningSnapshots
{
    /// <summary>The Text's full structure: <c>p:i</c>, <c>s:i.j</c> and <c>w:segmentId:k</c> bindings by identity.</summary>
    public static ObjectSnapshot Read(LcmCache cache, IText text) => new(
        CanonicalId.FromGuid(cache.LangProject.Guid),
        new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            [SnapshotFields.LangProjectTexts] = Structure(cache, text),
        });

    /// <summary>The compact effect: identity, name, counts and a digest of the structure.</summary>
    public static Dictionary<string, string> Summary(LcmCache cache, IText text)
    {
        var structure = Structure(cache, text);
        var summary = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in structure)
            if (key == "ref" || key.StartsWith("name:", StringComparison.Ordinal)) summary[key] = value;
        summary["paragraphs"] = structure.Keys.Count(key => key.StartsWith("p:", StringComparison.Ordinal))
            .ToString(CultureInfo.InvariantCulture);
        summary["segments"] = structure.Keys.Count(key => key.StartsWith("s:", StringComparison.Ordinal))
            .ToString(CultureInfo.InvariantCulture);
        summary["words"] = structure.Keys.Count(key => key.StartsWith("w:", StringComparison.Ordinal))
            .ToString(CultureInfo.InvariantCulture);
        var canonical = string.Join("\n", structure.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Key + "=" + pair.Value));
        summary["structure"] = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        return summary;
    }

    private static Dictionary<string, string> Structure(LcmCache cache, IText text)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal) { ["ref"] = CanonicalId.FromGuid(text.Guid).Value };
        foreach (var handle in text.Name.AvailableWritingSystemIds)
        {
            var title = text.Name.get_String(handle).Text;
            if (!string.IsNullOrEmpty(title))
                result["name:" + cache.WritingSystemFactory.GetStrFromWs(handle)] = title.Normalize(NormalizationForm.FormD);
        }
        var paragraphs = text.ContentsOA?.ParagraphsOS.OfType<IStTxtPara>().ToArray() ?? [];
        for (var i = 0; i < paragraphs.Length; i++)
        {
            result["p:" + i.ToString(CultureInfo.InvariantCulture)] = CanonicalId.FromGuid(paragraphs[i].Guid).Value;
            var segments = paragraphs[i].SegmentsOS.ToArray();
            for (var j = 0; j < segments.Length; j++)
            {
                var segmentId = CanonicalId.FromGuid(segments[j].Guid).Value;
                result["s:" + i.ToString(CultureInfo.InvariantCulture) + "." + j.ToString(CultureInfo.InvariantCulture)] = segmentId;
                var analyses = segments[j].AnalysesRS.ToArray();
                for (var k = 0; k < analyses.Length; k++)
                    result["w:" + segmentId + ":" + k.ToString(CultureInfo.InvariantCulture)] =
                        CanonicalId.FromGuid(analyses[k].Guid).Value;
            }
        }
        return result;
    }
}
