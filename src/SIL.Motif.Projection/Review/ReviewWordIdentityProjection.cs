using System.Security.Cryptography;
using System.Text;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;

namespace SIL.Motif.Projection.Review;

/// <summary>A project spelling and its exact source identities, when both are available.</summary>
public sealed record ReviewProjectWordIdentity(
    string Word, string WritingSystemTag, string? WritingSystemId, string WordformId, string? WordKey);

/// <summary>Reads project wordforms without inventing writing-system identities.</summary>
public static class ReviewWordIdentityProjection
{
    /// <summary>Captures every populated project form with its exact project writing-system ID.</summary>
    public static IReadOnlyList<ReviewProjectWordIdentity> Read(LcmCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        var words = new List<ReviewProjectWordIdentity>();
        foreach (var wordform in cache.ServiceLocator.GetInstance<IWfiWordformRepository>().AllInstances())
        foreach (var handle in wordform.Form.AvailableWritingSystemIds.Order())
        {
            var word = wordform.Form.get_String(handle)?.Text?.Normalize(NormalizationForm.FormD);
            if (string.IsNullOrEmpty(word)) continue;
            var tag = cache.WritingSystemFactory.GetStrFromWs(handle);
            var wordformId = CanonicalId.FromGuid(wordform.Guid).Value;
            var writingSystemId = string.IsNullOrWhiteSpace(tag) ? null : tag;
            var resolved = writingSystemId is not null;
            var wordKey = resolved ? Key(writingSystemId!, wordform.Guid.ToString("D")) : null;
            words.Add(new(word, tag, writingSystemId, wordformId, wordKey));
        }
        return Array.AsReadOnly(words.OrderBy(item => item.WordformId, StringComparer.Ordinal)
            .ThenBy(item => item.WritingSystemTag, StringComparer.Ordinal).ToArray());
    }

    private static string Key(string writingSystem, string wordform) => "sha256:" + Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(writingSystem + "\0" + wordform)));
}
