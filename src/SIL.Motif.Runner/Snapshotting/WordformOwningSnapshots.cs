using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Model.Snapshot;

namespace SIL.Motif.Runner.Snapshotting;

/// <summary>
/// Projects one wordform's membership and authored forms under the project root, never the inventory. The members are
/// named Read, not Snapshot, so the Baseline's whole-model digest does not discover them as generated snapshotters.
/// </summary>
public static class WordformOwningSnapshots
{
    /// <summary>The wordform's identity as <c>ref</c> and each non-empty form as <c>form:&lt;ws&gt;</c>, in NFD.</summary>
    public static ObjectSnapshot Read(LcmCache cache, IWfiWordform wordform)
    {
        ArgumentNullException.ThrowIfNull(wordform);
        var alternatives = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ref"] = CanonicalId.FromGuid(wordform.Guid).Value,
        };
        foreach (var handle in wordform.Form.AvailableWritingSystemIds)
        {
            var text = wordform.Form.get_String(handle).Text;
            if (string.IsNullOrEmpty(text)) continue;
            alternatives["form:" + cache.WritingSystemFactory.GetStrFromWs(handle)] = text.Normalize(NormalizationForm.FormD);
        }
        return new ObjectSnapshot(CanonicalId.FromGuid(cache.LangProject.Guid),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
            {
                [SnapshotFields.LangProjectWordforms] = alternatives,
            });
    }

    /// <summary>The same projection for an identity that may not exist yet; an absent wordform has no fields.</summary>
    public static ObjectSnapshot Read(LcmCache cache, CanonicalId wordformId) =>
        cache.ServiceLocator.GetInstance<ICmObjectRepository>().TryGetObject(wordformId.ToGuid(), out var found) &&
        found is IWfiWordform wordform
            ? Read(cache, wordform)
            : new ObjectSnapshot(CanonicalId.FromGuid(cache.LangProject.Guid),
                new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal));
}
