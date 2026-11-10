using System.Globalization;
using System.Text;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Runner.Composers;

/// <summary>
/// One Note to set: the Text (name or id), the segment (1-based, counted across the Text) or a word it contains, and the Note.
/// <c>At</c> labels the edit in refusals, such as "line 7" of a TSV file.
/// </summary>
public sealed record EditTextNote(string Text, int? Segment, string? Word, string Note, string? At = null);

/// <summary>The Note edits and the analysis writing system they are written in; omitted uses the project's default.</summary>
public sealed record EditTextNotesIntent(IReadOnlyList<EditTextNote> Edits, string? AnalysisWritingSystem = null);

/// <summary>Reads a closed intent <c>{ "analysisWs"?, "edits": [ { "text", "segment" | "word", "note", "at"? } ] }</c>.</summary>
public static class EditTextNotesIntentParser
{
    private const string ConstructName = "EditTextNotes";

    public static EditTextNotesIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "analysisWs", "edits");
        var analysisWs = authored.TryGetProperty("analysisWs", out _) ? SoundSystemIntentParsing.Text(authored, "analysisWs") : null;
        if (analysisWs is not null && string.IsNullOrWhiteSpace(analysisWs))
            throw Refuse("'analysisWs' must name a writing system.");
        var edits = SoundSystemIntentParsing.Array(authored, "edits").Select((edit, index) => Edit(edit, index + 1)).ToArray();
        if (edits.Length == 0) throw Refuse("'edits' needs at least one edit.");
        return new EditTextNotesIntent(edits, analysisWs);
    }

    /// <summary>The exact first line a TSV file may start with; matched case-insensitively and then skipped.</summary>
    public const string TsvHeader = "Text\tLocator\tNote";

    /// <summary>Reads tab-separated lines <c>Text, segment-number-or-word, Note</c>, after an optional <see cref="TsvHeader"/> line.</summary>
    /// <remarks>
    /// A locator of digits that fits a whole number is a segment; anything else is a word. There is no quoting, so a cell
    /// holds no tab or line break. Every row problem is collected and reported once, with its line number.
    /// </remarks>
    public static JsonElement FromTsv(string tsv, string? analysisWs = null)
    {
        var edits = new List<object>();
        var problems = new List<string>();
        var lineNumber = 0;
        foreach (var raw in tsv.Split('\n'))
        {
            lineNumber++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (lineNumber == 1 && string.Equals(line, TsvHeader, StringComparison.OrdinalIgnoreCase)) continue;
            var cells = line.Split('\t');
            if (cells.Length != 3 || cells.Any(cell => cell.Length == 0))
            {
                problems.Add($"line {lineNumber}: needs three nonblank tab-separated cells (Text, segment or word, Note).");
                continue;
            }
            var at = $"line {lineNumber}";
            var text = cells[0].Normalize(NormalizationForm.FormD);
            var note = cells[2].Normalize(NormalizationForm.FormD);
            edits.Add(int.TryParse(cells[1], NumberStyles.None, CultureInfo.InvariantCulture, out var segment)
                ? new { text, segment, note, at }
                : new { text, word = cells[1].Normalize(NormalizationForm.FormD), note, at });
        }
        if (problems.Count > 0) throw Refuse(Summarize(problems));
        return JsonSerializer.SerializeToElement(analysisWs is null ? new { edits } : (object)new { analysisWs, edits });
    }

    internal static string Summarize(IReadOnlyList<string> problems, int shown = 20) =>
        $"{problems.Count} problem(s):\n  " + string.Join("\n  ", problems.Take(shown)) +
        (problems.Count > shown ? $"\n  ... and {problems.Count - shown} more." : string.Empty);

    private static EditTextNote Edit(JsonElement edit, int index)
    {
        SoundSystemIntentParsing.Object(edit, ConstructName + $" edit {index}", "text", "segment", "word", "note", "at");
        var at = edit.TryGetProperty("at", out _) ? SoundSystemIntentParsing.Text(edit, "at") : null;
        var label = $"edit {index}" + (at is null ? string.Empty : $" ({at})");
        var text = Nfd(SoundSystemIntentParsing.Text(edit, "text"), "text", label);
        var note = Nfd(SoundSystemIntentParsing.Text(edit, "note"), "note", label);
        var hasSegment = edit.TryGetProperty("segment", out var segment);
        var hasWord = edit.TryGetProperty("word", out _);
        if (hasSegment == hasWord) throw Refuse($"{label} names exactly one of 'segment' or 'word'.");
        if (hasWord) return new EditTextNote(text, null, Nfd(SoundSystemIntentParsing.Text(edit, "word"), "word", label), note, at);
        if (segment.ValueKind != JsonValueKind.Number || !segment.TryGetInt32(out var number) || number < 1)
            throw Refuse($"{label}: 'segment' must be a whole number from 1.");
        return new EditTextNote(text, number, null, note, at);
    }

    private static string Nfd(string text, string field, string label)
    {
        if (string.IsNullOrWhiteSpace(text) || text != text.Trim() || !text.IsNormalized(NormalizationForm.FormD))
            throw Refuse($"{label}: '{field}' must be a nonblank NFD string without edge spaces.");
        return text;
    }

    private static ContractParseException Refuse(string message) => new($"'{ConstructName}': {message}");
}

/// <summary>
/// Stages one <c>texts/segment/setNotes</c> per segment named, replacing that segment's Notes with the one Note given.
/// Each operation remembers the Notes it started from, so a segment that changed meanwhile fails at Apply.
/// </summary>
public static class EditTextNotesComposer
{
    private const string ConstructName = "EditTextNotes";

    public static IReadOnlyList<OperationEnvelope> Build(LcmCache cache, EditTextNotesIntent intent, Func<CanonicalId>? mintId = null)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(intent);
        var mint = mintId ?? (() => CanonicalId.Mint());
        var tag = intent.AnalysisWritingSystem ?? cache.WritingSystemFactory.GetStrFromWs(cache.DefaultAnalWs);
        var ws = cache.WritingSystemFactory.GetWsFromStr(tag);
        if (ws == 0 || !cache.ServiceLocator.WritingSystems.AnalysisWritingSystems.Any(system => system.Id == tag))
            throw Refuse($"'{tag}' is not an analysis writing system of this project.");
        var texts = new Dictionary<string, TextIndex>(StringComparer.Ordinal);
        var failedTexts = new Dictionary<string, string>(StringComparer.Ordinal);
        var seen = new HashSet<Guid>();
        var operations = new List<OperationEnvelope>(intent.Edits.Count);
        var problems = new List<string>();
        for (var number = 1; number <= intent.Edits.Count; number++)
        {
            var edit = intent.Edits[number - 1];
            var label = $"edit {number}" + (edit.At is null ? string.Empty : $" ({edit.At})");
            try
            {
                if (failedTexts.TryGetValue(edit.Text, out var textProblem)) throw Refuse(textProblem);
                if (!texts.TryGetValue(edit.Text, out var index))
                {
                    try { texts[edit.Text] = index = new TextIndex(cache, edit.Text); }
                    catch (ContractParseException failure) { failedTexts[edit.Text] = Strip(failure.Message); throw; }
                }
                var segment = edit.Segment is { } n ? index.Segment(n) : index.SegmentOfWord(edit.Word!);
                if (!seen.Add(segment.Guid))
                    throw Refuse($"text '{edit.Text}' has two edits for the same segment; give each segment one Note.");
                var current = EditTextNotesHandler.Contents(cache, segment);
                var noteId = segment.NotesOS.Count > 0 ? CanonicalId.FromGuid(segment.NotesOS[0].Guid) : mint();
                operations.Add(new OperationEnvelope(mint(), EditTextNotesOperationKinds.Set,
                    target: CanonicalId.FromGuid(segment.Guid),
                    after: JsonSerializer.SerializeToElement(new { ws = tag, note = new { id = noteId.Value, text = edit.Note }, expected = current }),
                    extensions: JsonSerializer.SerializeToElement(new { trialWords = index.WordsOf(segment) }),
                    rationale: "Set the Note of an existing Text segment."));
            }
            catch (ContractParseException failure)
            {
                problems.Add($"{label}: {Strip(failure.Message)}");
            }
        }
        if (problems.Count > 0) throw Refuse(EditTextNotesIntentParser.Summarize(problems));
        return operations;
    }

    private static string Strip(string message) => message.StartsWith($"'{ConstructName}': ", StringComparison.Ordinal)
        ? message[($"'{ConstructName}': ").Length..] : message;

    private sealed class TextIndex
    {
        private readonly string _name;
        private readonly List<ISegment> _segments;
        private Dictionary<string, List<int>>? _words;

        public TextIndex(LcmCache cache, string nameOrId)
        {
            _name = nameOrId;
            var text = Find(cache, nameOrId);
            _segments = text.ContentsOA?.ParagraphsOS.OfType<IStTxtPara>().SelectMany(paragraph => paragraph.SegmentsOS).ToList() ?? [];
        }

        public string[] WordsOf(ISegment segment) => segment.AnalysesRS.Where(a => a.Wordform is not null)
            .SelectMany(a => a.Wordform.Form.AvailableWritingSystemIds.Select(ws => a.Wordform.Form.get_String(ws).Text))
            .Where(form => !string.IsNullOrEmpty(form)).Select(form => form!.Normalize(NormalizationForm.FormD))
            .Distinct(StringComparer.Ordinal).ToArray();

        public ISegment Segment(int number) => number <= _segments.Count ? _segments[number - 1]
            : throw Refuse($"text '{_name}' has {_segments.Count} segments; there is no segment {number}.");

        public ISegment SegmentOfWord(string word)
        {
            _words ??= BuildWords();
            if (!_words.TryGetValue(word, out var numbers)) throw Refuse($"text '{_name}' has no word '{word}'.");
            return numbers.Count == 1 ? _segments[numbers[0] - 1]
                : throw Refuse($"word '{word}' is in {numbers.Count} segments of text '{_name}' ({string.Join(", ", numbers.Take(8))}); name the segment number instead.");
        }

        private Dictionary<string, List<int>> BuildWords()
        {
            var words = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (var index = 0; index < _segments.Count; index++)
                foreach (var analysis in _segments[index].AnalysesRS)
                {
                    if (analysis.Wordform is not { } wordform) continue;
                    foreach (var ws in wordform.Form.AvailableWritingSystemIds)
                    {
                        var form = wordform.Form.get_String(ws).Text?.Normalize(NormalizationForm.FormD);
                        if (string.IsNullOrEmpty(form)) continue;
                        if (!words.TryGetValue(form, out var numbers)) words[form] = numbers = [];
                        if (numbers.Count == 0 || numbers[^1] != index + 1) numbers.Add(index + 1);
                    }
                }
            return words;
        }

        private static IText Find(LcmCache cache, string nameOrId)
        {
            var repository = cache.ServiceLocator.GetInstance<ITextRepository>();
            if (CanonicalId.TryParse(nameOrId, out var id) && repository.TryGetObject(id.ToGuid(), out var byId)) return byId;
            var matches = repository.AllInstances().Where(text => text.Name.AvailableWritingSystemIds.Any(ws =>
                text.Name.get_String(ws).Text?.Normalize(NormalizationForm.FormD) == nameOrId)).ToArray();
            return matches.Length switch
            {
                1 => matches[0],
                0 => throw Refuse($"there is no text named '{nameOrId}'."),
                _ => throw Refuse($"{matches.Length} texts are named '{nameOrId}'; use the text's id."),
            };
        }
    }

    private static ContractParseException Refuse(string message) => new($"'{ConstructName}': {message}");
}
