using System.Text;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Retirement;

namespace SIL.Motif.Host.Parsimony;

/// <summary>
/// Proves, from the two Assessments of one Trial, that replacing listed forms with a rule left the readings of the
/// affected words unchanged. Unlike <see cref="RecipeVerification"/>, which follows the frozen human readings, this
/// compares every reading the parser produced, so a reading the rule newly allows is counted as new.
/// </summary>
public static class ReadingIdentityProof
{
    /// <summary>Compares the readings of <paramref name="affectedWords"/> before and after, translating retired forms first.</summary>
    /// <param name="before">The Trial's before run.</param>
    /// <param name="after">The Trial's after run.</param>
    /// <param name="affectedWords">The words whose readings must stay the same; the Trial may hold others.</param>
    /// <param name="mappings">
    /// The retired form to survivor mapping of each role. A before reading's morph at one of these roles is compared
    /// as the survivor, because after the change the rule produces the surface the retired form did.
    /// </param>
    public static ReadingIdentityResult Prove(RecipeVerificationRun before, RecipeVerificationRun after,
        IEnumerable<string> affectedWords, IReadOnlyCollection<RetirementExpectationMapping> mappings)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(affectedWords);
        ArgumentNullException.ThrowIfNull(mappings);

        var words = affectedWords.Select(Key).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var unavailable = new List<string>();
        if (words.Length == 0) unavailable.Add("There are no affected words to compare.");
        var translation = mappings.ToDictionary(
            item => (Key(item.RetiredForm), Key(item.Msa), item.InflType is null ? null : Key(item.InflType)),
            item => item.ReplacementForm);
        var beforeByWord = Index(before, "before", unavailable);
        var afterByWord = Index(after, "after", unavailable);

        var differences = new List<ReadingIdentityDifference>();
        var incomplete = new List<string>();
        int identical = 0, readingsBefore = 0, lost = 0, gained = 0;
        foreach (var word in words)
        {
            if (!Complete(beforeByWord, word, out var beforeWord) || !Complete(afterByWord, word, out var afterWord))
            {
                incomplete.Add(word);
                continue;
            }
            var beforeReadings = beforeWord.Analyses.Select(item => Render(item, translation)).ToHashSet(StringComparer.Ordinal);
            var afterReadings = afterWord.Analyses.Select(item => Render(item, null)).ToHashSet(StringComparer.Ordinal);
            var lostHere = beforeReadings.Except(afterReadings).Order(StringComparer.Ordinal).ToArray();
            var newHere = afterReadings.Except(beforeReadings).Order(StringComparer.Ordinal).ToArray();
            readingsBefore += beforeReadings.Count;
            lost += lostHere.Length;
            gained += newHere.Length;
            if (lostHere.Length == 0 && newHere.Length == 0) identical++;
            else differences.Add(new ReadingIdentityDifference(beforeWord.Word, lostHere, newHere));
        }
        if (incomplete.Count > 0)
            unavailable.Add($"{incomplete.Count} affected words have no completed search on both sides.");

        var status = differences.Count > 0 ? ReadingIdentityStatus.Differs
            : incomplete.Count > 0 || words.Length == 0 ? ReadingIdentityStatus.Incomplete
            : ReadingIdentityStatus.Identical;
        return new ReadingIdentityResult(status, words.Length, identical, readingsBefore, lost, gained,
            differences, incomplete, unavailable);
    }

    private static string Key(string text) => text.Normalize(NormalizationForm.FormD);

    private static Dictionary<string, ParseWordEvidence> Index(
        RecipeVerificationRun run, string side, List<string> unavailable)
    {
        var index = new Dictionary<string, ParseWordEvidence>(StringComparer.Ordinal);
        foreach (var word in run.Assessment.Words)
        {
            if (word.Morphology is null) continue;
            if (!index.TryAdd(Key(word.Morphology.Word), word.Morphology))
            {
                unavailable.Add($"The {side} Assessment holds a word more than once.");
                index[Key(word.Morphology.Word)] = word.Morphology with { InvalidShape = true };
            }
        }
        return index;
    }

    private static bool Complete(Dictionary<string, ParseWordEvidence> index, string word, out ParseWordEvidence evidence) =>
        index.TryGetValue(word, out evidence!) &&
        !evidence.Capped && !evidence.TimedOut && !evidence.InvalidShape && evidence.Unavailable.Count == 0;

    private static string Render(ParseAnalysis analysis,
        IReadOnlyDictionary<(string Form, string Msa, string? InflType), string>? translation) =>
        string.Join(" + ", analysis.Morphs.Select(morph =>
        {
            var form = morph.Form;
            if (translation is not null && form is not null && morph.Msa is not null &&
                translation.TryGetValue((Key(form), Key(morph.Msa), morph.InflType is null ? null : Key(morph.InflType)),
                    out var survivor))
                form = survivor;
            return $"{form}/{morph.Msa}/{morph.InflType}/{morph.GuessedString}";
        }));
}
