using SIL.LCModel;

namespace SIL.Motif.Host.Texts;

/// <summary>Occurrence counts from a fixed list of Text identities in a Baseline scratch project.</summary>
public sealed record TextOccurrenceSnapshot(
    IReadOnlyDictionary<string, int> OccurrencesByWord,
    IReadOnlyDictionary<string, int> InterlinearizedOccurrencesByWord,
    int TotalOccurrences,
    int InterlinearizedOccurrences);

/// <summary>Reads token occurrence counts through LibLCM's interlinear Text projection.</summary>
public static class TextOccurrenceReader
{
    /// <summary>Counts each distinct spelling at most once per word occurrence across the chosen Texts.</summary>
    public static TextOccurrenceSnapshot Read(LcmCache cache, IReadOnlyList<Guid> textIds)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(textIds);
        var repository = cache.ServiceLocator.GetInstance<ITextRepository>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var interlinearized = new Dictionary<string, int>(StringComparer.Ordinal);
        var total = 0;
        var analyzed = 0;
        var byWordform = new Dictionary<Guid, string[]>();
        foreach (var id in textIds.Distinct())
        {
            if (!repository.TryGetObject(id, out var text))
                throw new KeyNotFoundException($"No Text with GUID '{id:D}' exists in the Baseline.");
            foreach (var (wordform, isAnalysed) in InterlinearTextReader.ReadWordforms(text))
            {
                if (!byWordform.TryGetValue(wordform.Guid, out var forms))
                {
                    forms = wordform.Form.AvailableWritingSystemIds.Order()
                        .Select(ws => wordform.Form.get_String(ws)?.Text).OfType<string>()
                        .Select(form => form.Trim().Normalize(System.Text.NormalizationForm.FormD))
                        .Where(form => form.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
                    byWordform.Add(wordform.Guid, forms);
                }
                foreach (var form in forms)
                {
                    occurrences[form] = occurrences.GetValueOrDefault(form) + 1;
                    total++;
                    if (!isAnalysed) continue;
                    interlinearized[form] = interlinearized.GetValueOrDefault(form) + 1;
                    analyzed++;
                }
            }
        }
        return new TextOccurrenceSnapshot(occurrences, interlinearized, total, analyzed);
    }
}
