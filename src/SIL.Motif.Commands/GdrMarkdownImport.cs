using System.Text.RegularExpressions;
using SIL.Motif.Contract.Gdr;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Host.RichText;
using SIL.Motif.Runner.Operations;
using SIL.TextEditing;
using SIL.TextEditing.Markdown;

namespace SIL.Motif.Commands;

/// <summary>What reading a Guide section's Markdown found: the edits to stage, or the findings that stop them.</summary>
public sealed record GdrMarkdownImportResult(GdrMarkdownEdits? Edits, IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings, IReadOnlyList<string> ChangedFields)
{
    /// <summary>Whether the Markdown cannot be staged.</summary>
    public bool HasErrors => Errors.Count != 0;
}

/// <summary>
/// Reads the whole-record Markdown that <c>guide show --format fw-md</c> prints, or a bare Discussion body, against a
/// section as shown, into the field edits the window stages. It reads the same fields with the same reader and checker
/// as the window and stages nothing.
/// </summary>
public static class GdrMarkdownImport
{
    private static readonly Regex Opening = new(@"^::: \{field=(title|description|discussion)(?: ws=([^ }]+))?\}$",
        RegexOptions.CultureInvariant);

    private const string RecordFence = "---\nfwmd:";

    /// <summary>Reads <paramref name="markdown"/> as the whole record, or, with no front matter, as the Discussion alone.</summary>
    public static GdrMarkdownImportResult Read(GdrShowResponse shown, string? projectName, string markdown, GdrShowResponse? staged = null)
    {
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(markdown);
        var text = markdown.Replace("\r\n", "\n", StringComparison.Ordinal);
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];
        var view = new GdrMarkdownView(shown, projectName);
        var stagedView = staged is null ? null : new GdrMarkdownView(staged, projectName);
        var errors = new List<string>();
        var warnings = new List<string>();
        var values = new Dictionary<(string Name, string? Ws), string>();
        if (text.StartsWith(RecordFence, StringComparison.Ordinal))
        {
            if (!text.StartsWith(view.Header, StringComparison.Ordinal))
                return Fail("The record identity, parent and position are read-only; take the Markdown from 'guide show --format fw-md'.");
            if (!TryFields(text[view.Header.Length..], view, values, errors)) return Fail(errors.ToArray());
        }
        else values[("discussion", null)] = text.TrimEnd('\n');

        var edits = new List<TextRichStringPayload>();
        TextParagraphsPayload? paragraphs = null;
        var changed = new List<string>();
        foreach (var field in view.Fields)
        {
            if (!values.TryGetValue((field.Name, field.WritingSystem), out var value)) continue;
            var held = stagedView?.Fields.FirstOrDefault(item => item.Name == field.Name && item.WritingSystem == field.WritingSystem);
            if (held is not null && held.Body != field.Body && value != held.Body && (value == field.Body || DerivedFromBaseline(field.Body, held.Body, value)))
            {
                // The file is the Baseline text or an edit of it, so staging would drop the Draft's edit.
                errors.Add($"{field.Name}: the Draft already holds an edit of this field and the file would replace it. " +
                    $"Take the text from 'guide show {shown.Record.Abbreviation} --as-of <draft> --format fw-md', edit that, and run guide edit again.");
                continue;
            }
            if (value == field.Body) continue;
            var form = new FieldWorksMarkdownSourceForm(GdrMarkdownOptions.For(field.Response, projectName));
            var expanded = GuideInterlinear.Expand(value);
            var lifted = field.Inline ? ((string Markdown, IReadOnlyList<GdrQueryBlockMarkdown.Lifted> Blocks, IReadOnlyList<string> Warnings))
                (expanded, [], []) : GdrQueryBlockMarkdown.LiftMarkdown(expanded);
            var read = form.Read(lifted.Markdown, field.Inline);
            warnings.AddRange(lifted.Warnings.Select(item => $"{field.Name}: {item}"));
            errors.AddRange(read.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Error)
                .Select(item => $"{field.Name}: {item.Message}"));
            warnings.AddRange(read.Diagnostics.Where(item => item.Severity == DiagnosticSeverity.Warning)
                .Select(item => $"{field.Name}: {item.Message}"));
            if (read.HasErrors) continue;
            var original = GdrMarkdownView.ProjectParagraphs(field.Response);
            var readParagraphs = field.Inline ? read.Paragraphs : GdrQueryBlockMarkdown.RestoreParagraphs(read.Paragraphs, lifted.Blocks);
            if (field.Inline)
            {
                if (original.Length == readParagraphs.Count && original.Zip(readParagraphs).All(pair => form.AreEquivalent(pair.First, pair.Second)))
                    continue;
                var runs = Runs(readParagraphs[0]);
                if (field.Name == "title" && runs.Any(run => run.CharacterStyle is not null || run.ObjData is not null ||
                        !string.Equals(run.Ws, field.AlternativeWritingSystem, StringComparison.OrdinalIgnoreCase) ||
                        run.Props is { } props && (props.Ints.Count != 0 || props.Strings.Count != 0)))
                {
                    errors.Add("title: only plain text in its writing system is allowed.");
                    continue;
                }
                var alternative = (field.Name == "title" ? shown.TitleAlternatives : shown.DescriptionAlternatives)
                    .Single(item => item.WritingSystem == field.AlternativeWritingSystem);
                edits.Add(new TextRichStringPayload(field.Name == "title" ? "CmPossibility.Name" : "CmPossibility.Description",
                    field.AlternativeWritingSystem, runs, new TextRichStringExpected(alternative.Text, alternative.Digest)));
                changed.Add(field.WritingSystem is null ? field.Name : $"{field.Name} ({field.WritingSystem})");
                continue;
            }
            var edit = Paragraphs(shown, form, original, readParagraphs);
            if (edit is null) continue;
            paragraphs = edit;
            changed.Add(field.Name);
        }
        if (errors.Count != 0) return Fail(errors.ToArray());
        return new GdrMarkdownImportResult(
            new GdrMarkdownEdits(shown.Record.Id, edits, paragraphs,
                new GdrMarkdownIdentity(shown.Record.Abbreviation, shown.Record.ParentId, shown.Position)),
            errors, warnings, changed);

        GdrMarkdownImportResult Fail(params string[] findings) => new(null, findings, warnings, []);
    }

    // Derived from the Baseline: it keeps a Baseline paragraph and none the Draft added.
    private static bool DerivedFromBaseline(string baseline, string held, string value)
    {
        static HashSet<string> Split(string text) => text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(paragraph => paragraph.Trim()).Where(paragraph => paragraph.Length > 0).ToHashSet(StringComparer.Ordinal);
        HashSet<string> original = Split(baseline), staged = Split(held), given = Split(value);
        var stagedOnly = staged.Where(paragraph => !original.Contains(paragraph)).ToArray();
        if (stagedOnly.Length == 0 || stagedOnly.Any(given.Contains)) return false;
        return original.Count == 0 || original.Any(given.Contains);
    }

    private static bool TryFields(string body, GdrMarkdownView view, Dictionary<(string, string?), string> values, List<string> errors)
    {
        var known = view.Fields.Select(field => (field.Name, field.WritingSystem)).ToHashSet();
        var lines = body.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var match = Opening.Match(lines[i]);
            if (!match.Success) { errors.Add("Expected a field div."); return false; }
            var key = (match.Groups[1].Value, match.Groups[2].Success ? match.Groups[2].Value : null);
            if (!known.Contains(key) || values.ContainsKey(key))
            { errors.Add($"Unknown or repeated field: {key.Item1} {key.Item2}."); return false; }
            var lastBody = new List<string>();
            var depth = 0;
            var closed = false;
            while (++i < lines.Length)
            {
                var line = lines[i];
                if (line == ":::" && depth == 0) { closed = true; break; }
                if (line.StartsWith("::: ", StringComparison.Ordinal)) depth++;
                else if (line == ":::" && depth > 0) depth--;
                lastBody.Add(line);
            }
            if (!closed) { errors.Add($"The {key.Item1} div is not closed."); return false; }
            values[key] = string.Join("\n", lastBody);
        }
        if (values.Count != known.Count) { errors.Add("Every field must be present once."); return false; }
        return true;
    }

    // Unchanged paragraphs keep their keys; stored ones between them are reused in order.
    private static TextParagraphsPayload? Paragraphs(GdrShowResponse shown, FieldWorksMarkdownSourceForm form,
        IReadOnlyList<Paragraph> original, IReadOnlyList<Paragraph> read)
    {
        var pairs = Match(original.Count, read.Count, (o, n) => form.AreEquivalent(original[o], read[n]));
        var keyFor = new string?[read.Count];
        var unchanged = new bool[read.Count];
        foreach (var (o, n) in pairs) { keyFor[n] = original[o].Key as string; unchanged[n] = true; }
        var removed = new List<int>();
        var previousOld = -1;
        var previousNew = -1;
        foreach (var (o, n) in pairs.Append((original.Count, read.Count)))
        {
            var oldGap = Enumerable.Range(previousOld + 1, o - previousOld - 1).ToArray();
            var newGap = Enumerable.Range(previousNew + 1, n - previousNew - 1).ToArray();
            for (var i = 0; i < Math.Max(oldGap.Length, newGap.Length); i++)
            {
                if (i < oldGap.Length && i < newGap.Length)
                    keyFor[newGap[i]] = original[oldGap[i]].Key as string;
                else if (i < oldGap.Length) removed.Add(oldGap[i]);
            }
            previousOld = o;
            previousNew = n;
        }
        var changes = new List<TextParagraphChange>();
        for (var n = 0; n < read.Count; n++)
            if (!unchanged[n])
                changes.Add(new TextParagraphChange(n, keyFor[n], read[n].Style.Length > 0 ? read[n].Style : null,
                    Runs(read[n]), ParagraphProps(read[n])));
        if (changes.Count == 0 && removed.Count == 0) return null;
        var expected = new Dictionary<string, TextParagraphExpected>(StringComparer.Ordinal);
        foreach (var source in shown.Discussion)
        {
            if (source.Key is not { } key) continue;
            expected[key] = new TextParagraphExpected(string.Concat(source.Runs.Select(run => run.Text)), source.Style ?? "",
                TextRunsDigest.Of(source));
        }
        var removedKeys = removed.Select(o => original[o].Key as string).OfType<string>().ToArray();
        var wanted = changes.Select(change => change.Key).Concat(removedKeys).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return new TextParagraphsPayload(GdrDiscussionField, keyFor, changes, removedKeys,
            expected.Where(pair => wanted.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    private const string GdrDiscussionField = "CmPossibility.Discussion";

    /// <summary>Longest common subsequence of the two sequences under <paramref name="same"/>, as index pairs.</summary>
    internal static IReadOnlyList<(int Old, int New)> Match(int oldCount, int newCount, Func<int, int, bool> same)
    {
        var length = new int[oldCount + 1, newCount + 1];
        for (var o = oldCount - 1; o >= 0; o--)
            for (var n = newCount - 1; n >= 0; n--)
                length[o, n] = same(o, n) ? length[o + 1, n + 1] + 1 : Math.Max(length[o + 1, n], length[o, n + 1]);
        var pairs = new List<(int, int)>();
        for (int o = 0, n = 0; o < oldCount && n < newCount;)
        {
            if (same(o, n)) { pairs.Add((o, n)); o++; n++; }
            else if (length[o + 1, n] >= length[o, n + 1]) o++;
            else n++;
        }
        return pairs;
    }

    internal static IReadOnlyList<TextRun> Runs(Paragraph paragraph) => paragraph.Runs.Select(run =>
    {
        var style = run.Props.TryGetStr(TextPropertyNumbers.NamedStyle, out var named) && named.Length > 0 ? named : null;
        return new TextRun(run.Text, run.Ws, style, Props(run.Props), run.ObjData);
    }).ToArray();

    internal static TextProps ParagraphProps(Paragraph paragraph)
    {
        var props = paragraph.Props.Clone();
        if (paragraph.Rtl || props.IntProps.ContainsKey(128)) props.IntProps[128] = new IntPropValue(0, paragraph.Rtl ? 1 : 0);
        return Props(props);
    }

    private static TextProps Props(PropertyBag bag) => new(
        bag.IntProps.ToDictionary(pair => pair.Key, pair => (pair.Value.Variation, pair.Value.Value)),
        bag.StrProps.Where(pair => pair.Key != TextPropertyNumbers.NamedStyle).ToDictionary(pair => pair.Key, pair => pair.Value));
}
