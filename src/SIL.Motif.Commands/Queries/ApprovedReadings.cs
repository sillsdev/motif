using System.Globalization;
using System.Text;
using System.Text.Json;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Query.Corpus;
using SIL.Motif.Contract.Query.Readings;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// Reads the file <c>--approved</c> names, the oracle format: JSONL, one <c>{"type": "&lt;word&gt;", "readings": [{"gloss": "...", "class": "correct"}, ...]}</c> per
/// line. A reading is approved when its <c>class</c> is <c>correct</c>; every other class (wrong, junk, missing) and every other key is ignored.
/// A word with no correct reading is not an approved word and is left out of the comparison.
/// </summary>
internal static class ApprovedReadingsFile
{
    /// <summary>What the file holds: each word with an approved reading and its approved gloss strings, and how many words had none.</summary>
    public sealed record Content(IReadOnlyDictionary<string, IReadOnlyList<string>> Words, int WordsWithoutApprovedReading);

    /// <summary>Reads the file; words are keyed as the corpus spells them.</summary>
    public static Content Read(string path, bool foldCase)
    {
        string[] lines;
        try { lines = File.ReadAllLines(path, Encoding.UTF8); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        { throw Refuse($"The approved-readings file '{path}' cannot be read: {exception.Message}", 0); }
        var words = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var without = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(lines[index])) continue;
            var number = index + 1;
            JsonDocument document;
            try { document = JsonDocument.Parse(lines[index]); }
            catch (JsonException exception) { throw Refuse($"Line {number} of '{path}' is not JSON: {exception.Message}", number); }
            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out var typeValue) || typeValue.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(typeValue.GetString()) || !root.TryGetProperty("readings", out var readingsValue) || readingsValue.ValueKind != JsonValueKind.Array)
                    throw Refuse($"Line {number} of '{path}' is not {{\"type\": \"<word>\", \"readings\": [{{\"gloss\": \"<reading>\", \"class\": \"correct\"}}, ...]}}.", number);
                var word = typeValue.GetString()!.Trim().Normalize(NormalizationForm.FormC);
                if (foldCase) word = word.ToLowerInvariant();
                var approved = new List<string>();
                foreach (var item in readingsValue.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("class", out var classValue) || classValue.ValueKind != JsonValueKind.String)
                        throw Refuse($"Line {number} of '{path}' has a reading that is not {{\"gloss\": \"<reading>\", \"class\": \"<class>\"}}: {item.GetRawText()}", number);
                    if (!string.Equals(classValue.GetString(), "correct", StringComparison.Ordinal)) continue;
                    if (!item.TryGetProperty("gloss", out var glossValue) || glossValue.ValueKind != JsonValueKind.String)
                        throw Refuse($"Line {number} of '{path}' has a correct reading without a string gloss: {item.GetRawText()}", number);
                    var gloss = glossValue.GetString()!.Trim().Normalize(NormalizationForm.FormC);
                    if (gloss.Length > 0 && !approved.Contains(gloss)) approved.Add(gloss);
                }
                if (!seen.Add(word)) throw Refuse($"Line {number} of '{path}' repeats the word '{word}'.", number);
                if (approved.Count == 0) without++;
                else words.Add(word, approved);
            }
        }
        if (words.Count == 0) throw Refuse($"The approved-readings file '{path}' holds no word with a reading whose class is correct.", 0);
        return new Content(words, without);
    }

    private static QueryRefusalException Refuse(string message, int line) =>
        new(new Refusal(RefusalCodes.QueryApprovedInvalid, FailureReason.InvalidArgument, message,
            line > 0 ? new Dictionary<string, string>(StringComparer.Ordinal) { ["line"] = line.ToString(CultureInfo.InvariantCulture) } : null));
}

/// <summary>Compares approved readings with the readings of the corpus parse in one pass over the approved words.</summary>
internal static class ApprovedReadingsComparison
{
    /// <summary>A reading as the oracle file writes its gloss: each morph as <c>text/gloss</c> (text alone when the project gives no gloss), joined by <c> + </c>.</summary>
    internal static string Render(WordReading reading) =>
        string.Join(" + ", reading.Morphs.Select(MorphSeqSupport.Label)).Normalize(NormalizationForm.FormC);

    /// <summary>Compares each approved word with its readings in <paramref name="readings"/>.</summary>
    public static ApprovedReport Compare(ApprovedReadingsFile.Content content, CorpusWordList corpus, IReadingSource readings, bool complete)
    {
        var approved = content.Words;
        var rows = new List<ApprovedWord>();
        foreach (var (word, expected) in approved)
        {
            var inCorpus = corpus.TryGet(word, out var entry);
            var produced = inCorpus ? readings.Get(word) : null;
            var distinct = new HashSet<string>((produced?.Readings ?? []).Select(Render), StringComparer.Ordinal);
            var kept = new List<string>();
            var missing = new List<string>();
            foreach (var reading in expected)
                (distinct.Contains(reading) ? kept : missing).Add(reading);
            var extra = distinct.Where(reading => !expected.Contains(reading)).Order(StringComparer.Ordinal).ToArray();
            var state = inCorpus ? produced?.State ?? ReadingStates.Failed : "absent";
            var incomplete = ReadingStates.MayBeIncomplete(state);
            rows.Add(new ApprovedWord(word, inCorpus ? entry!.Tokens : 0, inCorpus, state, kept, missing, extra,
                incomplete ? null : missing.Count == 0, incomplete ? null : extra.Length == 0));
        }
        rows = [.. rows.OrderByDescending(row => row.Tokens).ThenBy(row => row.Word, StringComparer.Ordinal)];
        var tokens = rows.Sum(row => row.Tokens);
        var allKept = rows.Where(row => row.AllApprovedKept == true).Sum(row => row.Tokens);
        var clean = rows.Where(row => row.AllApprovedKept == true && row.NoExtras == true).Sum(row => row.Tokens);
        var incompleteRows = rows.Where(row => row.AllApprovedKept is null).ToArray();
        var inCorpusWords = rows.Count(row => row.InCorpus);
        double? Share(long part) => tokens == 0 ? null : (double)part / tokens;
        return new ApprovedReport(complete, new ApprovedTotals(rows.Count, inCorpusWords, rows.Count - inCorpusWords, content.WordsWithoutApprovedReading, approved.Values.Sum(list => list.Count),
            rows.Sum(row => row.Kept.Count), rows.Where(row => row.AllApprovedKept is not null).Sum(row => row.Missing.Count), rows.Where(row => row.AllApprovedKept is not null).Sum(row => row.Extra.Count),
            incompleteRows.Length, incompleteRows.Sum(row => row.Tokens), tokens, allKept, clean,
            Share(clean), Share(allKept)), rows);
    }
}
