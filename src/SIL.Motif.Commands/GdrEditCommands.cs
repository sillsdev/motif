using System.Text.Json;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Host.RichText;
using SIL.TextEditing;
using SIL.TextEditing.Markdown;

namespace SIL.Motif.Commands;

/// <summary>
/// Stages Grammar Guide edits in a Draft through the same operations the window stages, so Apply, Drift and the
/// Dry Run treat an agent's edit and a person's alike (ADR 0005 decision 5).
/// </summary>
public static class GdrEditCommands
{
    private const string DecisionsHeading = "### Decisions";

    /// <summary>
    /// Reads the Markdown against the Baseline's section and stages every field it changes in one Draft save. When the Draft
    /// already holds staged edits of the section, the Markdown must come from the previewed text, so no staged work is lost.
    /// </summary>
    public static CommandOutcome<GdrEditResponse> Edit(GdrEditRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var shown = GdrQuery.Show(new GdrShowRequest(request.ProjectPath, request.Id));
        if (!shown.Succeeded) return CommandOutcome<GdrEditResponse>.Refused(WithAddSectionHint(shown.Refusal!));
        var named = string.IsNullOrWhiteSpace(request.Draft) ? null : request.Draft.Trim();
        var current = GdrQuery.Show(new GdrShowRequest(request.ProjectPath, request.Id, AsOf: named ?? PendingChanges.DraftName));
        if (!current.Succeeded && (named is not null || current.Refusal!.Code != "draft.not-found"))
            return CommandOutcome<GdrEditResponse>.Refused(current.Refusal!);
        return Stage(request.ProjectPath, shown.Value!, request.Markdown, request.Draft, current.Succeeded ? current.Value : null);
    }

    /// <summary>Appends "A or B, I am choosing A, because ..." under the section's Decisions heading and stages the result.</summary>
    public static CommandOutcome<GdrEditResponse> Decide(GdrDecideRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (Entry(request) is not { } entry)
            return CommandOutcome<GdrEditResponse>.Refused(new Refusal(RefusalCodes.GdrDecisionInvalid, FailureReason.InvalidArgument,
                "Give the option chosen, the option set aside and the reason, each on one line."));
        var shown = GdrQuery.Show(new GdrShowRequest(request.ProjectPath, request.Id));
        if (!shown.Succeeded) return CommandOutcome<GdrEditResponse>.Refused(WithAddSectionHint(shown.Refusal!));
        if (BlockPart(request, shown.Value!, Path.GetFileNameWithoutExtension(request.ProjectPath)) is { } block)
            return CommandOutcome<GdrEditResponse>.Refused(new Refusal(RefusalCodes.GdrDecisionInvalid, FailureReason.InvalidArgument,
                block.Message + EscapeHint + " Nothing was staged.", block.Facts()));
        var named = string.IsNullOrWhiteSpace(request.Draft) ? null : request.Draft.Trim();
        var current = GdrQuery.Show(new GdrShowRequest(request.ProjectPath, request.Id, AsOf: named ?? PendingChanges.DraftName));
        if (!current.Succeeded && (named is not null || current.Refusal!.Code != "draft.not-found"))
            return CommandOutcome<GdrEditResponse>.Refused(current.Refusal!);
        var body = new GdrMarkdownView(current.Succeeded ? current.Value! : shown.Value!, Path.GetFileNameWithoutExtension(request.ProjectPath)).Fields
            .Single(field => field.Name == "discussion").Body;
        return Stage(request.ProjectPath, shown.Value!, WithDecision(body, entry), request.Draft, null);
    }

    private static readonly string[] RowKeys = ["section", "choosing", "over", "because", "rung", "tried"];

    /// <summary>
    /// Reads a JSONL file of judgement calls, one object per line with the string keys section, choosing, over and because, and optionally rung and tried (tried stands in for over or is appended to it as "(tried: X)", and rung is appended to because as "(rung: X)").
    /// Blank lines are skipped and each row keeps its line number. Any unreadable line fails the whole file, so nothing is partly read.
    /// </summary>
    public static CommandOutcome<IReadOnlyList<GdrDecisionRow>> ParseDecisions(string jsonl)
    {
        ArgumentNullException.ThrowIfNull(jsonl);
        var rows = new List<GdrDecisionRow>();
        var lines = jsonl.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim().TrimStart('\uFEFF');
            if (line.Length == 0) continue;
            var row = i + 1;
            string? problem = null;
            try
            {
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.ValueKind != JsonValueKind.Object) problem = "is not a JSON object";
                else
                {
                    var values = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var property in document.RootElement.EnumerateObject())
                    {
                        if (!RowKeys.Contains(property.Name)) { problem = $"has the unknown key \"{property.Name}\"; the keys are {string.Join(", ", RowKeys)}"; break; }
                        if (property.Value.ValueKind != JsonValueKind.String) { problem = $"has a \"{property.Name}\" that is not a string"; break; }
                        values[property.Name] = property.Value.GetString()!;
                    }
                    var required = new[] { "section", "choosing", "because" }.Concat(values.ContainsKey("tried") ? [] : ["over"]);
                    if (problem is null && required.FirstOrDefault(key => !values.ContainsKey(key)) is { } missing) problem = $"lacks the key \"{missing}\"";
                    if (problem is null)
                    {
                        // The Guide template has no rung or tried field: tried names what was set aside, and the rung ends the reason.
                        var over = values.TryGetValue("tried", out var tried)
                            ? values.TryGetValue("over", out var named) ? $"{named} (tried: {tried})" : tried
                            : values["over"];
                        var because = values.TryGetValue("rung", out var rung) ? $"{values["because"]} (rung: {rung})" : values["because"];
                        rows.Add(new GdrDecisionRow(row, values["section"], values["choosing"], over, because));
                    }
                }
            }
            catch (JsonException exception) { problem = "is not valid JSON: " + exception.Message; }
            if (problem is not null) return CommandOutcome<IReadOnlyList<GdrDecisionRow>>.Refused(RowRefusal(row, $"Line {row} {problem}."));
        }
        return rows.Count == 0
            ? CommandOutcome<IReadOnlyList<GdrDecisionRow>>.Refused(RowRefusal(0, "The file holds no decisions; give one JSON object per line."))
            : CommandOutcome<IReadOnlyList<GdrDecisionRow>>.Success(rows);
    }

    /// <summary>
    /// Adds every row under its section's Decisions heading in one command. Each row is read the way the Guide reads it, with the
    /// rows before it in place, before anything is staged; a bad row fails the batch, names its line and stages nothing. A dry run
    /// stops after that check.
    /// </summary>
    public static CommandOutcome<GdrDecideBatchResponse> DecideBatch(GdrDecideBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Rows.Count == 0)
            return CommandOutcome<GdrDecideBatchResponse>.Refused(RowRefusal(0, "The batch holds no decisions."));
        var project = Path.GetFileNameWithoutExtension(request.ProjectPath);
        var named = string.IsNullOrWhiteSpace(request.Draft) ? null : request.Draft.Trim();
        var sections = new Dictionary<string, (GdrShowResponse Shown, string Body, List<(int Row, string Entry)> Added)>(StringComparer.Ordinal);
        var byGiven = new Dictionary<string, string>(StringComparer.Ordinal);
        var decided = new List<GdrDecidedRow>();
        foreach (var row in request.Rows)
        {
            var one = new GdrDecideRequest(request.ProjectPath, row.Section, row.Choosing, row.Over, row.Because, request.Draft);
            if (Entry(one) is not { } entry)
                return CommandOutcome<GdrDecideBatchResponse>.Refused(RowRefusal(row.Row,
                    $"Line {row.Row}: give the option chosen, the option set aside and the reason, each on one line."));
            if (!byGiven.TryGetValue(row.Section, out var id))
            {
                var shown = GdrQuery.Show(new GdrShowRequest(request.ProjectPath, row.Section));
                if (!shown.Succeeded) return CommandOutcome<GdrDecideBatchResponse>.Refused(WithRow(WithAddSectionHint(shown.Refusal!), row.Row));
                id = shown.Value!.Record.Id;
                byGiven[row.Section] = id;
                if (!sections.ContainsKey(id))
                {
                    var current = GdrQuery.Show(new GdrShowRequest(request.ProjectPath, row.Section, AsOf: named ?? PendingChanges.DraftName));
                    if (!current.Succeeded && (named is not null || current.Refusal!.Code != "draft.not-found"))
                        return CommandOutcome<GdrDecideBatchResponse>.Refused(WithRow(current.Refusal!, row.Row));
                    var body = new GdrMarkdownView(current.Succeeded ? current.Value! : shown.Value!, project).Fields
                        .Single(field => field.Name == "discussion").Body;
                    sections[id] = (shown.Value!, body, []);
                }
            }
            var (baseline, _, added) = sections[id];
            if (BlockPart(one, baseline, project) is { } block)
                return CommandOutcome<GdrDecideBatchResponse>.Refused(RowRefusal(row.Row, $"Line {row.Row}: {block.Message}{EscapeHint}", block.Facts()));
            added.Add((row.Row, entry));
            decided.Add(new GdrDecidedRow(row.Row, baseline.Record.Abbreviation, entry));
        }
        // One read per section; a failure is halved to the first row that breaks it.
        var edits = new List<GdrMarkdownEdits>();
        foreach (var (baseline, body, added) in sections.Values)
        {
            GdrMarkdownImportResult ReadWith(int count) =>
                GdrMarkdownImport.Read(baseline, project, added.Take(count).Aggregate(body, (text, item) => WithDecision(text, item.Entry)));
            var read = ReadWith(added.Count);
            if (read.HasErrors)
            {
                int low = 1, high = added.Count;
                while (low < high)
                {
                    var middle = (low + high) / 2;
                    if (ReadWith(middle).HasErrors) high = middle; else low = middle + 1;
                }
                return CommandOutcome<GdrDecideBatchResponse>.Refused(RowRefusal(added[low - 1].Row,
                    $"Line {added[low - 1].Row}: the Guide cannot read this decision: {string.Join(" ", ReadWith(low).Errors)} Escape a literal @ [ * _ ` < or \\ with a backslash."));
            }
            edits.Add(read.Edits!);
        }
        var draftName = named ?? PendingChanges.DraftName;
        if (request.DryRun)
            return CommandOutcome<GdrDecideBatchResponse>.Success(new GdrDecideBatchResponse(draftName, false, decided, 0, []));
        var created = new List<string>();
        var result = ProposalCommands.StageGdrMarkdownBatch(request.ProjectPath, edits, named, created);
        if (!result.Succeeded)
            return CommandOutcome<GdrDecideBatchResponse>.Refused(new Refusal(result.Refusal!.Code, result.Refusal.Reason,
                result.Refusal.Message + " The batch is staged in one save, so nothing was staged.", result.Refusal.Facts));
        return CommandOutcome<GdrDecideBatchResponse>.Success(new GdrDecideBatchResponse(
            result.Value!.LastOrDefault()?.DraftName ?? draftName, true, decided, result.Value!.Sum(item => item.Operations.Count), created.Distinct().ToArray()));
    }

    private static Refusal RowRefusal(int row, string message, IReadOnlyDictionary<string, string>? located = null) => new(RefusalCodes.GdrDecisionsInvalid, FailureReason.InvalidArgument,
        message + " Nothing was staged.", new Dictionary<string, string>(located ?? new Dictionary<string, string>())
        {
            ["row"] = row.ToString(System.Globalization.CultureInfo.InvariantCulture),
        });

    private static Refusal WithRow(Refusal refusal, int row) => new(refusal.Code, refusal.Reason,
        $"Line {row}: {refusal.Message} Nothing was staged.",
        refusal.Facts.Concat([new KeyValuePair<string, string>("row", row.ToString(System.Globalization.CultureInfo.InvariantCulture))])
            .GroupBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Last().Value));

    /// <summary>The judgement call as one list item with its parts as given inline Markdown, or null when a part is blank or spans lines.</summary>
    internal static string? Entry(GdrDecideRequest request)
    {
        string[] parts = [request.Choosing, request.Over, request.Because];
        if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part.Contains('\n') || part.Contains('\r'))) return null;
        var (chosen, over, because) = Pieces(request);
        return $"- {chosen} or {over}, I am choosing {chosen}, because {because}";
    }

    private static (string Chosen, string Over, string Because) Pieces(GdrDecideRequest request)
    {
        var because = request.Because.Trim();
        if (!because.EndsWith('.') && !because.EndsWith('!') && !because.EndsWith('?')) because += ".";
        return (request.Choosing.Trim(), request.Over.Trim(), because);
    }

    /// <summary>Names the parts whose markers pair across the joined line, else null.</summary>
    private static string? CrossPartPairing(FieldWorksMarkdownSourceForm form, GdrDecideRequest request)
    {
        var (chosen, over, because) = Pieces(request);
        if (Agrees(form, chosen, over, because)) return null;
        // Probe the others as plain words: name a part that fails alone, else the pair that fails together.
        var alone = new List<string>();
        if (!Agrees(form, chosen, "x", "x")) alone.Add("--choosing");
        if (!Agrees(form, "x", over, "x")) alone.Add("--over");
        if (!Agrees(form, "x", "x", because)) alone.Add("--because");
        if (alone.Count == 0)
        {
            if (!Agrees(form, chosen, over, "x")) alone.AddRange(["--choosing", "--over"]);
            else if (!Agrees(form, chosen, "x", because)) alone.AddRange(["--choosing", "--because"]);
            else alone.AddRange(["--over", "--because"]);
        }
        return $"{string.Join(" and ", alone)} has an unbalanced marker that would pair with another part of the Decisions line.";
    }

    private static bool Agrees(FieldWorksMarkdownSourceForm form, string chosen, string over, string because)
    {
        var read = form.Read($"{chosen} or {over}, I am choosing {chosen}, because {because}", true);
        if (read.Diagnostics.Any(item => item.Severity == DiagnosticSeverity.Error)) return false;
        var joined = read.Paragraphs[0];
        var literal = form.Read("x", true).Paragraphs[0].Runs[0];
        var expected = new Paragraph { DefaultWs = joined.DefaultWs };
        foreach (var (fragment, isPart) in new[] { (chosen, true), (" or ", false), (over, true), (", I am choosing ", false), (chosen, true), (", because ", false), (because, true) })
        {
            if (isPart) foreach (var run in form.Read(fragment, true).Paragraphs[0].Runs) expected.Runs.Add(run);
            else expected.Runs.Add(literal.CloneWithText(fragment));
        }
        return MarkdownConverter.AreEquivalent(expected, joined);
    }

    /// <summary>The first problem with a part as the import reads it, else null.</summary>
    private static DecisionProblem? BlockPart(GdrDecideRequest request, GdrShowResponse shown, string projectName)
    {
        var form = new FieldWorksMarkdownSourceForm(GdrMarkdownOptions.For(shown, projectName));
        foreach (var (name, part) in new[] { ("choosing", request.Choosing), ("over", request.Over), ("because", request.Because) })
        {
            var text = part.Trim();
            var read = form.Read(text, true);
            var error = read.Diagnostics.FirstOrDefault(item => item.Severity == DiagnosticSeverity.Error);
            if (error is not null) return Located(name, text, error);
            if (read.Paragraphs.Count != 1)
                return new DecisionProblem(name, null, null, $"--{name} must be inline Markdown with no block (heading, list, quote or fence).");
        }
        if (CrossPartPairing(form, request) is { } pairing) return new DecisionProblem(null, null, null, pairing);
        return InLine(form, request);
    }

    /// <summary>Reads the whole list item and places a refusal in the part it falls in.</summary>
    private static DecisionProblem? InLine(FieldWorksMarkdownSourceForm form, GdrDecideRequest request)
    {
        var (chosen, over, because) = Pieces(request);
        var entry = $"- {chosen} or {over}, I am choosing {chosen}, because {because}";
        var error = form.Read(entry, false).Diagnostics.FirstOrDefault(item => item.Severity == DiagnosticSeverity.Error);
        if (error is null) return null;
        var at = error.Range.Start.Character;
        var spans = new[]
        {
            ("choosing", 2, chosen), ("over", 2 + chosen.Length + 4, over),
            ("choosing", 2 + chosen.Length + 4 + over.Length + 16, chosen), ("because", 2 + chosen.Length + 4 + over.Length + 16 + chosen.Length + 10, because),
        };
        foreach (var (field, start, part) in spans)
            if (at >= start && at <= start + part.Length) return Located(field, part, ShiftedBy(error, start));
        return new DecisionProblem(null, at, null, $"The Decisions line is not readable: {error.Message}");
    }

    private static LanguageDiagnostic ShiftedBy(LanguageDiagnostic error, int start) => new(
        new LanguageRange(error.Range.Start.Character - start, error.Range.End.Line == error.Range.Start.Line ? error.Range.End.Character - start : error.Range.Start.Character - start),
        error.Severity, error.Message, error.Code, error.Source, error.Fix);

    private static DecisionProblem Located(string field, string text, LanguageDiagnostic error)
    {
        var start = Math.Clamp(error.Range.Start.Character, 0, text.Length);
        var end = error.Range.End.Line == error.Range.Start.Line ? Math.Clamp(error.Range.End.Character, start, text.Length) : start;
        var observed = text[start..end];
        var fix = error.Fix?.Edits.FirstOrDefault()?.NewText;
        return new DecisionProblem(field, start, observed,
            $"--{field} is not inline Markdown: {error.Message} At character {start}: \"{observed}\"" +
            (fix is null ? "." : $", written {fix} to keep it as text."));
    }

    /// <summary>A part the Guide cannot stage: its field, offset and text, and why.</summary>
    private sealed record DecisionProblem(string? Field, int? Offset, string? Text, string Message)
    {
        public IReadOnlyDictionary<string, string> Facts()
        {
            var facts = new Dictionary<string, string>(StringComparer.Ordinal);
            if (Field is not null) facts["field"] = Field;
            if (Offset is { } at) facts["offset"] = at.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (Text is not null) facts["text"] = Text;
            return facts;
        }
    }

    private const string EscapeHint = " Escape a literal @ [ * _ ` < or \\ as \\@ \\[ \\* \\_ \\` \\< or \\\\ .";

    /// <summary>
    /// The Discussion body with <paramref name="entry"/> last under the Decisions heading, which it adds when absent.
    /// Lines inside fenced blocks, such as motif-query and motif-show, are never read as headings and no blank line is changed.
    /// </summary>
    internal static string WithDecision(string body, string entry)
    {
        var lines = body.Split('\n').ToList();
        var fenced = FencedLines(lines);
        var heading = Enumerable.Range(0, lines.Count).FirstOrDefault(i => !fenced[i] && lines[i].Trim() == DecisionsHeading, -1);
        if (heading < 0)
            return (body.Trim().Length == 0 ? string.Empty : body.TrimEnd('\n') + "\n\n") + DecisionsHeading + "\n\n" + entry;
        var end = Enumerable.Range(heading + 1, lines.Count - heading - 1).FirstOrDefault(i => !fenced[i] && lines[i].StartsWith('#'), -1);
        if (end < 0) end = lines.Count;
        var last = end - 1;
        while (last > heading && lines[last].Trim().Length == 0) last--;
        if (last == heading) lines.InsertRange(last + 1, ["", entry]);
        else if (!fenced[last] && lines[last].StartsWith("- ", StringComparison.Ordinal)) lines.Insert(last + 1, entry);
        else lines.InsertRange(last + 1, ["", entry]);
        return string.Join("\n", lines);
    }

    // A line is fenced when it opens, sits inside or closes a ``` or ~~~ block.
    private static bool[] FencedLines(List<string> lines)
    {
        var fenced = new bool[lines.Count];
        char fence = '\0';
        var length = 0;
        for (var i = 0; i < lines.Count; i++)
        {
            var text = lines[i].TrimStart();
            var marker = text.Length > 0 && text[0] is '`' or '~' ? text[0] : '\0';
            var run = marker == '\0' ? 0 : text.TakeWhile(c => c == marker).Count();
            if (fence == '\0')
            {
                if (run >= 3) { fence = marker; length = run; fenced[i] = true; }
                continue;
            }
            fenced[i] = true;
            if (marker == fence && run >= length && text.Skip(run).All(char.IsWhiteSpace)) fence = '\0';
        }
        return fenced;
    }

    private static Refusal WithAddSectionHint(Refusal refusal) => refusal.Code != RefusalCodes.GdrNotFound ? refusal :
        new Refusal(refusal.Code, refusal.Reason, refusal.Message +
            " Only sections in the Baseline can be edited. Add a new section with guide add (give its body with --from-file); a section added in a Draft can be edited only after that Draft is applied and a Baseline is captured with baseline capture.", refusal.Facts);

    private static CommandOutcome<GdrEditResponse> Stage(string projectPath, GdrShowResponse shown, string markdown, string? draft,
        GdrShowResponse? staged)
    {
        var read = GdrMarkdownImport.Read(shown, Path.GetFileNameWithoutExtension(projectPath), markdown, staged);
        if (read.HasErrors)
            return CommandOutcome<GdrEditResponse>.Refused(new Refusal(RefusalCodes.GdrMarkdownInvalid, FailureReason.InvalidArgument,
                "The Markdown cannot be read, so nothing was staged: " + string.Join(" ", read.Errors) +
                (read.Errors.Any(error => error.Contains("code block", StringComparison.OrdinalIgnoreCase))
                    ? " Only motif-query and motif-show blocks may be fenced; other code blocks are not kept in a Guide."
                    : "")));
        var draftName = string.IsNullOrWhiteSpace(draft) ? null : draft.Trim();
        var stylesCreated = new List<string>();
        var result = ProposalCommands.StageGdrMarkdown(projectPath, read.Edits!, draftName, stylesCreated);
        if (!result.Succeeded) return CommandOutcome<GdrEditResponse>.Refused(result.Refusal!);
        var response = result.Value!.LastOrDefault();
        return CommandOutcome<GdrEditResponse>.Success(new GdrEditResponse(response?.DraftName ?? draftName ?? PendingChanges.DraftName,
            shown.Record.Id, shown.Record.Abbreviation, read.ChangedFields, result.Value!.Sum(item => item.Operations.Count), read.Warnings) { StylesCreated = stylesCreated.ToArray() });
    }
}
