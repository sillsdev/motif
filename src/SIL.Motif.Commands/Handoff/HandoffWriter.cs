using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Texts;

namespace SIL.Motif.Commands.Handoff;

/// <summary>
/// Writes the five flat files ADR 0045 puts in an AI Handoff — <c>grammar.json</c>, <c>texts.json</c>,
/// <c>assessment.json</c>, <c>parse_grammar_texts_assessment.py</c>, and <c>handoff.md</c> — and publishes
/// them atomically: every file lands in a sibling <c>.incoming-&lt;guid&gt;</c> directory first, the exact
/// listing is validated, and only then one <see cref="Directory.Move(string, string)"/> makes it appear at
/// its destination.
/// </summary>
/// <remarks>
/// <see cref="Publish"/> is the whole atomicity contract: its populate callback writes files freely into
/// the incoming directory, and a <see cref="Refusal"/> returned from it — or an exception thrown out of
/// it — both abort before the destination is ever touched. The incoming directory is always removed on
/// any path that does not end in a successful move.
/// </remarks>
public static class HandoffWriter
{
    internal const string GrammarFileName = "grammar.json";
    internal const string TextsFileName = "texts.json";
    internal const string AssessmentFileName = "assessment.json";
    internal const string PythonHelperFileName = "parse_grammar_texts_assessment.py";
    internal const string HandoffMarkdownFileName = "handoff.md";

    private const string StarterPromptResource = "SIL.Motif.Commands.Handoff.Assets.starter-prompt.md";

    private const string PythonHelperResource =
        "SIL.Motif.Commands.Handoff.Assets.parse_grammar_texts_assessment.py";

    /// <summary>The ref motif's own documents are linked at. Its release tag once one exists.</summary>
    internal const string MotifRef = "main";

    /// <summary>
    /// The PanGloss release tag its documents are linked at, which moves on PanGloss's release schedule
    /// and not motif's. A tag rather than a branch, so a Handoff's links keep describing the formats it
    /// was written in after PanGloss moves on; <c>v0.3.2</c> is the first release carrying
    /// <c>docs/formats/</c>.
    /// </summary>
    internal const string PanGlossRef = "v0.3.2";

    private static readonly string[] AlwaysRequiredTopLevelFiles =
        [GrammarFileName, TextsFileName, PythonHelperFileName, HandoffMarkdownFileName];

    private static readonly JsonSerializerOptions CompactOptions = new() { WriteIndented = false };

    /// <summary>
    /// Builds a Handoff folder in a fresh sibling incoming directory via <paramref name="populate"/>,
    /// validates the exact listing, and moves it to <paramref name="destinationDirectory"/> in one step.
    /// </summary>
    /// <param name="destinationDirectory">
    /// Where the folder must appear. Assumed already checked non-existent or empty by the caller; an
    /// existing empty directory here is removed immediately before the final move.
    /// </param>
    /// <param name="includeAssessment">Whether <c>assessment.json</c> is required (design decision 2: absent, not empty, when there is none).</param>
    /// <param name="populate">
    /// Writes every file into the incoming directory it is handed. Returning a <see cref="Refusal"/>
    /// aborts without moving anything; an exception it throws propagates after the incoming directory is
    /// still cleaned up.
    /// </param>
    /// <returns>The <see cref="Refusal"/> <paramref name="populate"/> returned, or <see langword="null"/> on success.</returns>
    public static Refusal? Publish(
        string destinationDirectory, bool includeAssessment, Func<string, Refusal?> populate)
    {
        ArgumentNullException.ThrowIfNull(destinationDirectory);
        ArgumentNullException.ThrowIfNull(populate);

        var full = Path.GetFullPath(destinationDirectory);
        var parent = Path.GetDirectoryName(full)
            ?? throw new ArgumentException("The destination must have a parent directory.", nameof(destinationDirectory));
        Directory.CreateDirectory(parent);

        var incoming = Path.Combine(parent, ".incoming-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(incoming);
        try
        {
            var refusal = populate(incoming);
            if (refusal is not null) return refusal;

            ValidateListing(incoming, includeAssessment);

            // Known empty by the caller's own pre-check; removed here so Directory.Move never sees it.
            if (Directory.Exists(full)) Directory.Delete(full);
            Directory.Move(incoming, full);
            return null;
        }
        finally
        {
            if (Directory.Exists(incoming)) DeleteDirectorySafely(incoming);
        }
    }

    /// <summary>
    /// Writes <c>texts.json</c>: every requested Text — every Text in the project when
    /// <paramref name="requestedTextIds"/> is empty — each carrying its own sanitized-title-and-GUID
    /// <c>key</c> field, one compact record per line (design decisions 1 and 3). Each line holds one whole
    /// record rather than a fragment of one, but every record except the last also carries the array's
    /// separating comma, so a line parses alone only once that comma is stripped.
    /// </summary>
    /// <param name="firstKey">The first written Text's own <c>key</c> field, for a working example elsewhere.</param>
    /// <returns>
    /// A refusal naming the first unresolved Text id, or <see langword="null"/> on success. An id that does
    /// not resolve is refused rather than silently dropped, so a Handoff never claims fewer Texts than the
    /// person actually chose.
    /// </returns>
    internal static Refusal? WriteTextsJson(
        LcmCache cache, IReadOnlyList<Guid> requestedTextIds, string incomingRoot, out string? firstKey)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(requestedTextIds);
        firstKey = null;

        var repository = cache.ServiceLocator.GetInstance<ITextRepository>();
        var chosen = new List<IText>();
        if (requestedTextIds.Count > 0)
        {
            foreach (var textId in requestedTextIds)
            {
                if (!repository.TryGetObject(textId, out var text))
                    return new Refusal(
                        "handoff.text-not-found", FailureReason.InvalidArgument,
                        $"Text '{textId:D}' is not present in the project.",
                        new Dictionary<string, string> { ["textId"] = textId.ToString("D") });
                chosen.Add(text);
            }
        }
        else
        {
            chosen.AddRange(repository.AllInstances());
        }

        var records = chosen.Select(text => InterlinearTextReader.Read(cache, text))
            .Select(projection =>
            {
                var written = FlexTextJsonWriter.Write(projection);
                var document = written["document"];
                written.Remove("document");
                return (JsonNode)new JsonObject
                {
                    ["key"] = InterlinearTextFileNaming.BuildKey(projection),
                    ["document"] = document,
                };
            })
            .ToList();
        firstKey = records.Count > 0 ? records[0]!["key"]!.GetValue<string>() : null;
        File.WriteAllText(Path.Combine(incomingRoot, TextsFileName), BuildLineDelimitedJsonArray(records));
        return null;
    }

    /// <summary>One Selection word's batch-pass statistics, the shape <c>assessment.json</c> writes per line.</summary>
    internal readonly record struct AssessedWordStatistics(string Word, string Outcome, int? ElapsedMs, string? RawSignature);

    /// <summary>
    /// Writes <c>assessment.json</c>: every Assessment word, each record carrying its own <c>word</c> field
    /// so a future trace can join the same record instead of a parallel structure (design decisions 2 and
    /// 12 — this call never writes a trace itself; see <see cref="SIL.Motif.Host.PanGloss.IPanGlossTracer"/>).
    /// </summary>
    internal static void WriteAssessmentJson(string incomingRoot, IReadOnlyList<AssessedWordStatistics> words)
    {
        ArgumentNullException.ThrowIfNull(words);

        var records = words
            .OrderBy(word => word.Word, StringComparer.Ordinal)
            .Select(word => (JsonNode)BuildAssessedWordRecord(word))
            .ToList();
        File.WriteAllText(Path.Combine(incomingRoot, AssessmentFileName), BuildLineDelimitedJsonArray(records));
    }

    private static JsonObject BuildAssessedWordRecord(AssessedWordStatistics word)
    {
        var value = new JsonObject { ["word"] = word.Word, ["outcome"] = word.Outcome };
        if (word.ElapsedMs is { } elapsed) value["elapsedMs"] = elapsed;
        if (!string.IsNullOrEmpty(word.RawSignature)) value["signature"] = word.RawSignature;
        return value;
    }

    /// <summary>
    /// Writes <c>parse_grammar_texts_assessment.py</c> into the incoming directory: the embedded reader
    /// script every Handoff carries, unconditionally, since it reads <c>grammar.json</c> and <c>texts.json</c>
    /// whether or not this run also collected an Assessment.
    /// </summary>
    internal static void WritePythonHelper(string incomingRoot) =>
        File.WriteAllText(
            Path.Combine(incomingRoot, PythonHelperFileName),
            SubstituteDocumentRefs(ReadEmbeddedText(PythonHelperResource)));

    private static string SubstituteDocumentRefs(string template) =>
        template
            .Replace("{{MOTIF_REF}}", MotifRef, StringComparison.Ordinal)
            .Replace("{{PANGLOSS_REF}}", PanGlossRef, StringComparison.Ordinal);

    /// <summary>
    /// Builds <c>handoff.md</c>'s content: orientation, a manifest of the files this run actually wrote, and
    /// per file, what it is, one <c>grep</c> example, and one Python call. Capped at 100
    /// lines by <c>HandoffMarkdownTests.HandoffMarkdownNeverExceedsTheHundredLineCap</c>.
    /// </summary>
    internal static string BuildHandoffMarkdown(bool hasAssessment, string sampleTextKey, string sampleWord)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sampleTextKey);

        var assessmentManifestLine = hasAssessment
            ? "- `assessment.json` — whether PanGloss accepted each Selection word, and how long it took."
            : "- No Assessment was run for this Handoff, so `assessment.json` is not included.";

        // Derived from the manifest's own condition: a literal count drifted from the list it introduced.
        var fileCount = hasAssessment ? "five" : "four";

        var assessmentSection = hasAssessment
            ? $"""

                ## assessment.json

                One record per Selection word, each carrying its own `word` field: the outcome PanGloss
                reported (`analysed`, `no-analysis`, `capped`, `timed-out`, or `skipped`) and how long the
                batch pass took. A traced word, when one has been chosen for tracing, carries its
                derivation in the same record. See
                https://raw.githubusercontent.com/sillsdev/motif/{MotifRef}/docs/handoff/assessment-format.md.

                ```
                grep '"{sampleWord}"' assessment.json
                ```
                ```
                python -c "import json; d=json.load(open('assessment.json')); print([r for r in d if r['word']=='{sampleWord}'][0])"
                ```
                ```
                python parse_grammar_texts_assessment.py word {sampleWord}
                ```
                """
            : $"""

                ## No Assessment

                Nobody ran an Assessment before this Handoff was written, which is a complete Handoff and
                not a broken one. There is no `assessment.json`, and so no evidence for why any word did
                or did not parse; run an Assessment and hand off again to get one.
                """;

        var markdown = $"""
            # Handoff

            The {fileCount} files listed below describe one FieldWorks project as of its last save: the
            grammar PanGloss parsed with, the interlinear Texts that were selected, what happened when
            each Selection word was parsed, the script that reads all of that, and this file. There are
            no other files and no subfolders.

            Every JSON file here is valid JSON with **one record per line** — pretty-printed down to the
            record, compact within it — so a `grep` for a word returns that word's whole record on one
            line. The whole file loads with a plain `json.load`. A single grepped line carries the array's
            trailing comma, so strip it before `json.loads`, or hand the line to
            `parse_grammar_texts_assessment.py`, whose loaders take either form.

            `parse_grammar_texts_assessment.py`, in this same folder, has convenience routines for all of
            the above and a `--help` that teaches the file shapes; run it directly rather than reading its
            source copied in here.

            ## Files in this folder

            - `grammar.json` — the grammar PanGloss actually parsed with.
            - `texts.json` — every selected Text, each record carrying its own sanitized-title-and-GUID `key`.
            {assessmentManifestLine}
            - `parse_grammar_texts_assessment.py` — reads the three files above; see its own `--help`.
            - `handoff.md` — this file.

            ## grammar.json

            Rules, parts of speech, phonemes, and lexicon entries, exactly as PanGloss read them. See
            https://raw.githubusercontent.com/sillsdev/PanGloss/{PanGlossRef}/docs/formats/grammar-format.md.

            ```
            grep '"guid"' grammar.json
            ```
            ```
            python -c "import json; print(len(json.load(open('grammar.json'))))"
            ```
            ```
            python parse_grammar_texts_assessment.py grammar rule <name>
            ```

            ## texts.json

            One record per selected Text. `{sampleTextKey}` is one such record's `key` in this Handoff.

            ```
            grep '"{sampleTextKey}"' texts.json
            ```
            ```
            python -c "import json; print([t['key'] for t in json.load(open('texts.json'))])"
            ```
            ```
            python parse_grammar_texts_assessment.py text {sampleTextKey}
            ```
            {assessmentSection}
            """;

        return NormalizeNewlines(markdown);
    }

    /// <summary>
    /// Builds the text a person pastes into the chat alongside the dragged files (design decision 5): the
    /// <c>starter-prompt.md</c> asset with this run's language and project name substituted in.
    /// </summary>
    internal static string BuildPastedHeader(string languageName, string projectName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(languageName);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectName);

        return SubstituteDocumentRefs(ReadEmbeddedText(StarterPromptResource)
            .Replace("{{LANGUAGE_NAME}}", languageName, StringComparison.Ordinal)
            .Replace("{{PROJECT_NAME}}", projectName, StringComparison.Ordinal));
    }

    /// <summary>Every file the published folder contains, as Handoff-relative, forward-slashed paths.</summary>
    internal static IReadOnlyList<string> ListFiles(string root)
    {
        var full = Path.GetFullPath(root);
        return Directory.EnumerateFiles(full, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(full, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    // The folder's own consistency guard: never move an incomplete listing to the caller's destination.
    private static void ValidateListing(string root, bool includeAssessment)
    {
        foreach (var name in AlwaysRequiredTopLevelFiles)
        {
            if (!File.Exists(Path.Combine(root, name)))
                throw new InvalidOperationException($"The Handoff folder is missing '{name}'.");
        }

        var assessmentPath = Path.Combine(root, AssessmentFileName);
        if (includeAssessment)
        {
            if (!File.Exists(assessmentPath))
                throw new InvalidOperationException("The Handoff folder is missing 'assessment.json'.");
        }
        else if (File.Exists(assessmentPath))
        {
            throw new InvalidOperationException("A --no-assess Handoff must not write 'assessment.json'.");
        }
    }

    // One record per line; all but the last carry the array's comma, so a line parses alone once stripped.
    private static string BuildLineDelimitedJsonArray(IReadOnlyList<JsonNode> records)
    {
        var text = new StringBuilder();
        text.Append('[').Append('\n');
        for (var index = 0; index < records.Count; index++)
        {
            text.Append(records[index].ToJsonString(CompactOptions));
            if (index < records.Count - 1) text.Append(',');
            text.Append('\n');
        }
        text.Append(']').Append('\n');
        return text.ToString();
    }

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string ReadEmbeddedText(string resourceName)
    {
        using var stream = typeof(HandoffWriter).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static void DeleteDirectorySafely(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
