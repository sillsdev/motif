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
        foreach (var id in textIds.Distinct())
        {
            if (!repository.TryGetObject(id, out var text))
                throw new KeyNotFoundException($"No Text with GUID '{id:D}' exists in the Baseline.");
            var projection = InterlinearTextReader.Read(cache, text);
            foreach (var word in projection.Paragraphs.SelectMany(paragraph => paragraph.Phrases).SelectMany(phrase => phrase.Words))
            {
                if (word.WordformGuid is null) continue;
                var forms = word.Items.Where(item => item.Type == "txt" && item.Value.Length > 0)
                    .Select(item => item.Value.Trim().Normalize(System.Text.NormalizationForm.FormD))
                    .Where(value => value.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
                foreach (var form in forms)
                {
                    occurrences[form] = occurrences.GetValueOrDefault(form) + 1;
                    total++;
                    if (word.AnalysisStatus == InterlinearAnalysisStatus.Unanalysed) continue;
                    interlinearized[form] = interlinearized.GetValueOrDefault(form) + 1;
                    analyzed++;
                }
            }
        }
        return new TextOccurrenceSnapshot(occurrences, interlinearized, total, analyzed);
    }
}
