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
/// </summary>
public sealed record EditTextNote(string Text, int? Segment, string? Word, string Note);

/// <summary>The Note edits and the analysis writing system they are written in; omitted uses the project's default.</summary>
public sealed record EditTextNotesIntent(IReadOnlyList<EditTextNote> Edits, string? AnalysisWritingSystem = null);

/// <summary>Reads a closed intent <c>{ "analysisWs"?, "edits": [ { "text", "segment" | "word", "note" } ] }</c>.</summary>
public static class EditTextNotesIntentParser
{
    private const string ConstructName = "EditTextNotes";

    public static EditTextNotesIntent Parse(JsonElement authored)
    {
        SoundSystemIntentParsing.Object(authored, ConstructName, "analysisWs", "edits");
        var analysisWs = authored.TryGetProperty("analysisWs", out _) ? SoundSystemIntentParsing.Text(authored, "analysisWs") : null;
        if (analysisWs is not null && string.IsNullOrWhiteSpace(analysisWs))
            throw Refuse("'analysisWs' must name a writing system.");
        var edits = SoundSystemIntentParsing.Array(authored, "edits").Select(Edit).ToArray();
        if (edits.Length == 0) throw Refuse("'edits' needs at least one edit.");
        return new EditTextNotesIntent(edits, analysisWs);
    }

    /// <summary>Reads tab-separated lines <c>Text, segment-number-or-word, Note</c>; a header line starting "Text" is skipped.</summary>
    /// <remarks>A locator that is a whole number is a segment; anything else is a word.</remarks>
    public static JsonElement FromTsv(string tsv, string? analysisWs = null)
    {
        var edits = new List<object>();
        var lineNumber = 0;
        foreach (var raw in tsv.Split('\n'))
        {
            lineNumber++;
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            var cells = line.Split('\t');
            if (lineNumber == 1 && cells[0] == "Text") continue;
            if (cells.Length != 3 || cells.Any(cell => cell.Length == 0))
                throw Refuse($"line {lineNumber} must have three nonblank tab-separated cells (Text, segment or word, Note).");
            var text = cells[0].Normalize(NormalizationForm.FormD);
            var note = cells[2].Normalize(NormalizationForm.FormD);
            edits.Add(int.TryParse(cells[1], NumberStyles.None, CultureInfo.InvariantCulture, out var segment)
                ? new { text, segment, note }
                : new { text, word = cells[1].Normalize(NormalizationForm.FormD), note });
        }
        return JsonSerializer.SerializeToElement(analysisWs is null ? new { edits } : (object)new { analysisWs, edits });
    }

    private static EditTextNote Edit(JsonElement edit)
    {
        SoundSystemIntentParsing.Object(edit, ConstructName + " edit", "text", "segment", "word", "note");
        var text = Nfd(SoundSystemIntentParsing.Text(edit, "text"), "text");
        var note = Nfd(SoundSystemIntentParsing.Text(edit, "note"), "note");
        var hasSegment = edit.TryGetProperty("segment", out var segment);
        var hasWord = edit.TryGetProperty("word", out _);
        if (hasSegment == hasWord) throw Refuse("each edit names exactly one of 'segment' or 'word'.");
        if (hasWord) return new EditTextNote(text, null, Nfd(SoundSystemIntentParsing.Text(edit, "word"), "word"), note);
        if (segment.ValueKind != JsonValueKind.Number || !segment.TryGetInt32(out var number) || number < 1)
            throw Refuse("'segment' must be a whole number from 1.");
        return new EditTextNote(text, number, null, note);
    }

    private static string Nfd(string text, string field)
    {
        if (string.IsNullOrWhiteSpace(text) || text != text.Trim() || !text.IsNormalized(NormalizationForm.FormD))
            throw Refuse($"'{field}' must be a nonblank NFD string without edge spaces.");
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
        var seen = new HashSet<Guid>();
        var operations = new List<OperationEnvelope>(intent.Edits.Count);
        foreach (var edit in intent.Edits)
        {
            if (!texts.TryGetValue(edit.Text, out var index)) texts[edit.Text] = index = new TextIndex(cache, edit.Text);
            var segment = edit.Segment is { } number ? index.Segment(number) : index.SegmentOfWord(edit.Word!);
            if (!seen.Add(segment.Guid))
                throw Refuse($"text '{edit.Text}' has two edits for the same segment ({edit.Segment?.ToString(CultureInfo.InvariantCulture) ?? $"word '{edit.Word}'"}); give each segment one Note.");
            var current = EditTextNotesHandler.Texts(segment, ws);
            var noteId = segment.NotesOS.Count > 0 ? CanonicalId.FromGuid(segment.NotesOS[0].Guid) : mint();
            operations.Add(new OperationEnvelope(mint(), EditTextNotesOperationKinds.Set,
                target: CanonicalId.FromGuid(segment.Guid),
                after: JsonSerializer.SerializeToElement(new { ws = tag, note = new { id = noteId.Value, text = edit.Note }, expected = current }),
                rationale: "Set the Note of an existing Text segment."));
        }
        return operations;
    }

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
