using System.Globalization;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Host.Parser;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Worker.Corpus;

internal static class GrammarCoverageParserInputs
{
    internal const string FirstLetterLowercase = "fieldworks-first-letter-lowercase-1";
    internal const string Exact = "exact-1";

    public static LowercasePass ReadCasing(BaselineRecord baseline, string writingSystemId)
    {
        using var cache = BaselineReadCache.Open(baseline.FwDataPath);
        return LowercasePass.ForCache(cache.Cache, writingSystemId);
    }

    public static (string[] Words, Dictionary<string, string?> Origins) Expand(
        IReadOnlyList<string> selection, LowercasePass casing, string policy)
    {
        if (policy is not (Exact or FirstLetterLowercase or BatchInputProfile.WholeWordLowercase))
            throw new InvalidDataException("The Assessment casing policy is unavailable.");
        var words = new List<string>(selection);
        var origins = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var word in selection)
            if (!origins.TryAdd(GrammarCoverageInventory.Normalize(word), null))
                throw new InvalidDataException("The Assessment Selection has duplicate parser keys.");

        foreach (var word in selection)
        {
            var fallback = LowercaseOf(word, casing, policy);
            if (fallback is null ||
                !origins.TryAdd(GrammarCoverageInventory.Normalize(fallback), GrammarCoverageInventory.Normalize(word)))
                continue;
            words.Add(fallback);
        }
        return (words.ToArray(), origins);
    }

    public static string? LowercaseOf(string word, LowercasePass casing, string policy)
    {
        if (policy == Exact) return null;
        if (policy == BatchInputProfile.WholeWordLowercase) return casing.LowercaseOf(word);
        if (policy != FirstLetterLowercase)
            throw new InvalidDataException("The grammar coverage casing policy is unavailable.");

        var length = StringInfo.GetNextTextElementLength(word);
        return casing.LowercaseOf(word[..length]) is { } lower ? lower + word[length..] : null;
    }

    public static string WordsDigest(IReadOnlyList<string> words, string newline) =>
        AssessmentMaterial.Digest(words.Count == 0 ? "" : string.Join(newline, words) + newline);
}
