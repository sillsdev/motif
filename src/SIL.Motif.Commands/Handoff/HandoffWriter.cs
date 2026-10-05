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
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.Texts;

namespace SIL.Motif.Commands.Handoff;

/// <summary>
/// Writes the five core files ADR 0045 puts in an AI Handoff and, for a one-word Handoff, its trace file.
/// Every file lands in a sibling <c>.incoming-&lt;guid&gt;</c> directory first; only a validated listing is
/// moved to its destination.
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
    internal const string AssessmentFileName = "parse-results.json";
    internal const string PythonHelperFileName = "read_results.py";
    internal const string HandoffMarkdownFileName = "handoff.md";

    private const string StarterPromptResource = "SIL.Motif.Commands.Handoff.Assets.starter-prompt.md";

    private const string PythonHelperResource =
        "SIL.Motif.Commands.Handoff.Assets.read_results.py";

    /// <summary>The ref motif's own documents are linked at. Its release tag once one exists.</summary>
    internal const string MotifRef = "main";

    /// <summary>
    /// The PanGloss release tag its documents are linked at, which moves on PanGloss's release schedule
    /// and not motif's. A tag rather than a branch, so a Handoff's links keep describing the formats it
    /// was written in after PanGloss moves on. It must equal the tag in <c>pangloss-release.json</c>, whose
    /// runtime-specific asset the package bundles, so the documents describe the parser that wrote the files.
    /// </summary>
    internal const string PanGlossRef = "v0.6.2";

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
    /// <param name="includeAssessment">Whether <c>parse-results.json</c> is required (design decision 2: absent, not empty, when there is none).</param>
    /// <param name="includeTrace">Whether one trace file is required inside <c>traces/</c>.</param>
    /// <param name="populate">
    /// Writes every file into the incoming directory it is handed. Returning a <see cref="Refusal"/>
    /// aborts without moving anything; an exception it throws propagates after the incoming directory is
    /// still cleaned up.
    /// </param>
    /// <returns>The <see cref="Refusal"/> <paramref name="populate"/> returned, or <see langword="null"/> on success.</returns>
    public static Refusal? Publish(
        string destinationDirectory, bool includeAssessment, Func<string, Refusal?> populate, bool includeTrace = false)
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

            ValidateListing(incoming, includeAssessment, includeTrace);

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

    /// <summary>One Selection word's batch-pass statistics, the shape <c>parse-results.json</c> writes per line.</summary>
    internal readonly record struct AssessedWordStatistics(string Word, string Outcome, int? ElapsedMs, string? RawSignature, ParseWordEvidence? Morphology = null);

    /// <summary>
    /// Writes <c>parse-results.json</c>: every Assessment word, each record carrying its own <c>word</c> field.
    /// A one-word Handoff adds its raw trace file and summary to that word's record.
    /// </summary>
    internal static void WriteAssessmentJson(
        string incomingRoot, IReadOnlyList<AssessedWordStatistics> words, WordTraceResponse? trace = null)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (trace is not null && !words.Any(word => StringComparer.Ordinal.Equals(word.Word, trace.Word)))
            throw new InvalidOperationException("The traced word is not in the Handoff Assessment.");

        if (trace is not null)
        {
            WriteTraceJson(incomingRoot, trace);
        }

        var records = words
            .OrderBy(word => word.Word, StringComparer.Ordinal)
            .Select(word => (JsonNode)BuildAssessedWordRecord(
                word, trace is not null && StringComparer.Ordinal.Equals(word.Word, trace.Word) ? trace : null))
            .ToList();
        File.WriteAllText(Path.Combine(incomingRoot, AssessmentFileName), BuildLineDelimitedJsonArray(records));
    }

    internal static void WriteTraceJson(string incomingRoot, WordTraceResponse trace)
    {
        ArgumentNullException.ThrowIfNull(trace);
        if (string.IsNullOrWhiteSpace(trace.DiagnosticJson))
            throw new InvalidOperationException("The Handoff trace has no raw diagnostic to write.");
        var tracePath = Path.Combine(incomingRoot, TraceRelativePath(trace.Word)
            .Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(tracePath)!);
        File.WriteAllText(tracePath, trace.DiagnosticJson);
    }

    private static JsonObject BuildAssessedWordRecord(AssessedWordStatistics word, WordTraceResponse? trace)
    {
        var refusal = SIL.Motif.Commands.Queries.ParserRefusals.Of(word.Morphology, word.Outcome);
        var value = new JsonObject { ["word"] = word.Word.Normalize(System.Text.NormalizationForm.FormC),
            ["outcome"] = refusal is null ? word.Outcome : "refused" };
        if (refusal is not null)
            value["refusal"] = new JsonObject { ["code"] = refusal.Code, ["reason"] = refusal.Reason };
        if (word.Morphology is { } morphology) value["analysisCount"] = morphology.Analyses.Count;
        if (word.ElapsedMs is { } elapsed) value["elapsedMs"] = elapsed;
        if (!string.IsNullOrEmpty(word.RawSignature)) value["signature"] = word.RawSignature;
        if (trace is not null)
        {
            value["trace"] = new JsonObject
            {
                ["file"] = TraceRelativePath(trace.Word),
                ["summary"] = BuildTraceSummary(trace),
            };
        }
        return value;
    }

    private static JsonObject BuildTraceSummary(WordTraceResponse trace)
    {
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        Walk(trace.Reading.Root);
        return new JsonObject
        {
            ["outcome"] = trace.InvalidShape ? "invalid-shape" : trace.Parsed ? "parsed" : "no-analysis-recorded",
            ["parserSteps"] = trace.ParserSteps is { } steps ? JsonValue.Create(steps) : null,
            ["completion"] = trace.SearchCompletion switch
            {
                TraceSearchCompletion.Complete => "complete",
                TraceSearchCompletion.Incomplete => "incomplete",
                TraceSearchCompletion.InvalidShape => "not-run",
                _ => "unknown",
            },
            ["failureReasons"] = JsonSerializer.SerializeToNode(reasons.ToArray()),
            ["deepestRule"] = trace.DeepestRule,
        };

        void Walk(TraceStep step)
        {
            if (step.FailureReason is { Length: > 0 } reason) reasons.Add(reason);
            foreach (var child in step.Children) Walk(child);
        }
    }

    internal static string TraceRelativePath(string word)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(word);
        return "traces/" + Uri.EscapeDataString(word) + ".trace.json";
    }

    /// <summary>
    /// Writes <c>read_results.py</c> into the incoming directory: the embedded reader
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
    internal static string BuildHandoffMarkdown(
        bool hasAssessment, string sampleTextKey, string sampleWord, bool hasTrace = false,
        bool selectedTrace = false, WarningHandoffScope? warningScope = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sampleTextKey);

        var assessmentManifestLine = hasAssessment
            ? "- `parse-results.json` — whether PanGloss accepted each Selection word, and how long it took."
            : "- No Assessment was run for this Handoff, so `parse-results.json` is not included.";

        // Derived from the manifest's own condition: a literal count drifted from the list it introduced.
        var fileCount = hasTrace ? hasAssessment ? "six" : "five" : hasAssessment ? "five" : "four";
        var traceFileName = Uri.EscapeDataString(sampleWord) + ".trace.json";
        var traceManifestLine = hasTrace
            ? $"- `{traceFileName}` — the {(selectedTrace ? "selected" : "recorded")} trace diagnostic and Motif's host capture for `{sampleWord}`."
            : string.Empty;
        var traceEvidenceDescription = selectedTrace
            ? "This is the exact diagnostic selected from Try a Word, kept unchanged with the Baseline it records. " +
              "No Assessment or replacement trace was run for this Handoff."
            : "The matching `trace.summary` in `parse-results.json` gives recorded analysis attempts and failure reasons. " +
              "Completion is `complete` when the diagnostic records a finished search, `incomplete` when it records " +
              "a stopped trace, and `not-run` for invalid shape; `unknown` means no completion fact was recorded.";
        var traceSection = hasTrace
            ? $"""

                ## {traceFileName}

                The diagnostic for `{sampleWord}` preserves its recorded parser fields and Motif's host capture.
                {traceEvidenceDescription}

                ```
                python -c "import json; from pathlib import Path; name='{traceFileName}'; p=Path(name); p=p if p.is_file() else Path('traces')/name; print(json.dumps(json.load(open(p, encoding='utf-8'))))"
                ```
                """
            : string.Empty;

        var assessmentSection = hasAssessment
            ? $"""

                ## parse-results.json

                One record per Selection word, each carrying its own `word` field: the outcome PanGloss
                reported (`analysed`, `no-analysis`, `capped`, `timed-out`, or `skipped`) and how long the
                batch pass took. A one-word Handoff also links its raw trace file and summary from that
                word's record. Its completion follows the raw diagnostic: `complete`, `incomplete`, `not-run` for
                invalid shape, or `unknown` when no completion fact was recorded. See
                https://raw.githubusercontent.com/sillsdev/motif/{MotifRef}/docs/handoff/assessment-format.md.

                ```
                grep '"{sampleWord}"' parse-results.json
                ```
                ```
                python -c "import json; print(json.dumps(json.load(open('parse-results.json', encoding='utf-8'))))"
                ```
                ```
                python read_results.py --help
                ```
                """
            : $"""

                ## No Assessment

                Nobody ran an Assessment before this Handoff was written, which is a complete Handoff and
                not a broken one. There is no `parse-results.json`, and so no evidence for why any word did
                or did not parse; run an Assessment and hand off again to get one.
                """;

        var warningSection = warningScope is null ? string.Empty : WarningSection(warningScope);

        var markdown = $"""
            # Handoff

            The {fileCount} files listed below describe one FieldWorks project as of its captured Baseline. A
            one-word Handoff adds that word's raw diagnostic in the traces folder; the core files stay
            at the top level.

            Every JSON file here is valid JSON with **one record per line** — pretty-printed down to the
            record, compact within it — so a `grep` for a word returns that word's whole record on one
            line. The whole file loads with a plain `json.load`. A single grepped line carries the array's
            trailing comma, so strip it before `json.loads`, or hand the line to
            `read_results.py`, whose loaders take either form.

            `read_results.py`, in this same folder, has convenience routines for all of
            the above and a `--help` that teaches the file shapes; run it directly rather than reading its
            source copied in here.

            ## Files in this folder

            - `grammar.json` — the grammar PanGloss actually parsed with.
            - `texts.json` — every selected Text, each record carrying its own sanitized-title-and-GUID `key`.
            {assessmentManifestLine}
            {traceManifestLine}
            - `read_results.py` — reads `grammar.json`, `texts.json`, and `parse-results.json`; see its own `--help`.
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
            python read_results.py grammar rule <name>
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
            python read_results.py text {sampleTextKey}
            ```
            {assessmentSection}
            {traceSection}
            {warningSection}
            """;

        return NormalizeNewlines(markdown);
    }

    private static string WarningSection(WarningHandoffScope scope)
    {
        var reach = scope.State switch
        {
            WarningDisplayState.ExactUses when scope.HasUnfollowedConnections =>
                "The listed words use the item PanGloss named on routes Motif could follow",
            WarningDisplayState.ExactUses => "The words use the item PanGloss named",
            WarningDisplayState.MembershipCandidates =>
                "The listed words use members of the named resource; selection of the resource is not confirmed",
            WarningDisplayState.SpellingCandidates => "Spelling matches only; use is not confirmed",
            WarningDisplayState.NoneInSelection => "No words in this Selection use the named item",
            WarningDisplayState.NoFollowedRouteMatch =>
                "No words matched through routes Motif could follow; other named connections remain unchecked",
            WarningDisplayState.NoSubject => "PanGloss supplied no subject for this finding",
            WarningDisplayState.NamedUnsupportedRoute =>
                "PanGloss named an item, but Motif has no word route for its kind",
            WarningDisplayState.ProjectWide => "The named resource has no word attribution",
            WarningDisplayState.MissingObject => "The named item is missing from the FieldWorks project",
            WarningDisplayState.UnresolvedIdentity => "The named subject's identity or word reach is unavailable",
            _ => "Word evidence is unavailable",
        };
        var codeLine = string.IsNullOrWhiteSpace(scope.Code) ? string.Empty : $"Code: `{scope.Code}`.\n\n";
        var lowerBound = scope.HasUnfollowedConnections
            ? scope.State is WarningDisplayState.NoFollowedRouteMatch or
                WarningDisplayState.NamedUnsupportedRoute
                ? "Some named connections could not be followed; no word conclusion is made about those routes.\n\n"
                : "Some named connections could not be followed, so this word list is a lower bound.\n\n"
            : string.Empty;
        var quotedMessage = string.Join('\n', scope.Message.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n').Split('\n').Select(line => $"> {line}"));
        return $"""

            ## PanGloss warning

            {codeLine}Word reach: {reach}. {lowerBound}PanGloss reported:

            {quotedMessage}
            """;
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
    private static void ValidateListing(string root, bool includeAssessment, bool includeTrace)
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
                throw new InvalidOperationException("The Handoff folder is missing 'parse-results.json'.");
        }
        else if (File.Exists(assessmentPath))
        {
            throw new InvalidOperationException("A --no-assess Handoff must not write 'parse-results.json'.");
        }

        var traceRoot = Path.Combine(root, "traces");
        if (includeTrace)
        {
            if (!Directory.Exists(traceRoot) ||
                Directory.EnumerateFiles(traceRoot, "*.trace.json", SearchOption.AllDirectories).Count() != 1)
                throw new InvalidOperationException("A one-word Handoff must contain exactly one trace file.");
        }
        else if (Directory.Exists(traceRoot))
        {
            throw new InvalidOperationException("A Handoff without a one-word trace must not write a 'traces' folder.");
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
