using SIL.LCModel;

namespace SIL.Motif.Host.Texts;

/// <summary>
/// Counts each vernacular Text occurrence once under its first available spelling in declared vernacular order.
/// Alternate spellings remain keys and carry no occurrence weight unless another token is assigned to them.
/// </summary>
public sealed record TextOccurrenceSnapshot(
    IReadOnlyDictionary<string, int> OccurrencesByWord,
    IReadOnlyDictionary<string, int> InterlinearizedOccurrencesByWord,
    int TotalOccurrences,
    int InterlinearizedOccurrences,
    int DigitOccurrences = 0,
    int OtherWritingSystemOccurrences = 0);

/// <summary>Reads token occurrence counts through LibLCM's interlinear Text projection.</summary>
public static class TextOccurrenceReader
{
    /// <summary>
    /// Counts the vernacular words of the chosen Texts, each occurrence once in the totals however many
    /// vernacular spellings it records. Digit-only tokens and words in other writing systems are counted apart.
    /// </summary>
    public static TextOccurrenceSnapshot Read(LcmCache cache, IReadOnlyList<Guid> textIds)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(textIds);
        var repository = cache.ServiceLocator.GetInstance<ITextRepository>();
        var occurrences = new Dictionary<string, int>(StringComparer.Ordinal);
        var interlinearized = new Dictionary<string, int>(StringComparer.Ordinal);
        var total = 0;
        var analyzed = 0;
        var digits = 0;
        var other = 0;
        var byWordform = new Dictionary<Guid, (TextWordKind Kind, string[] Forms)>();
        foreach (var id in textIds.Distinct())
        {
            if (!repository.TryGetObject(id, out var text))
                throw new KeyNotFoundException($"No Text with GUID '{id:D}' exists in the Baseline.");
            foreach (var (wordform, isAnalysed) in InterlinearTextReader.ReadWordforms(text))
            {
                if (!byWordform.TryGetValue(wordform.Guid, out var classified))
                {
                    classified = TextWordForms.Classify(cache, wordform,
                        form => form.Normalize(System.Text.NormalizationForm.FormD));
                    byWordform.Add(wordform.Guid, classified);
                }
                if (classified.Kind == TextWordKind.Digits) { digits++; continue; }
                if (classified.Kind == TextWordKind.OtherWritingSystem) { other++; continue; }
                total++;
                if (isAnalysed) analyzed++;
                foreach (var form in classified.Forms)
                {
                    occurrences.TryAdd(form, 0);
                    if (isAnalysed) interlinearized.TryAdd(form, 0);
                }
                var primaryForm = classified.Forms[0];
                occurrences[primaryForm]++;
                if (isAnalysed) interlinearized[primaryForm]++;
            }
        }
        return new TextOccurrenceSnapshot(occurrences, interlinearized, total, analyzed, digits, other);
    }
}
