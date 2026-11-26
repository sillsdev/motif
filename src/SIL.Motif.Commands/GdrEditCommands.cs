using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Host.RichText;
using SIL.Motif.Runner.Operations;
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
                block + " Write a literal marker such as [ or * as \\[ or \\* ."));
        var named = string.IsNullOrWhiteSpace(request.Draft) ? null : request.Draft.Trim();
        var current = GdrQuery.Show(new GdrShowRequest(request.ProjectPath, request.Id, AsOf: named ?? PendingChanges.DraftName));
        if (!current.Succeeded && (named is not null || current.Refusal!.Code != "draft.not-found"))
            return CommandOutcome<GdrEditResponse>.Refused(current.Refusal!);
        var body = new GdrMarkdownView(current.Succeeded ? current.Value! : shown.Value!, Path.GetFileNameWithoutExtension(request.ProjectPath)).Fields
            .Single(field => field.Name == "discussion").Body;
        return Stage(request.ProjectPath, shown.Value!, WithDecision(body, entry), request.Draft, null);
    }

    /// <summary>The judgement call as one list item with its parts as given inline Markdown, or null when a part is blank or spans lines.</summary>
    internal static string? Entry(GdrDecideRequest request) => Parts(request) is { } parts ? "- " + parts.Sentence : null;

    private sealed record DecisionParts(string Chosen, string Over, string Because)
    {
        public string Sentence => $"{Chosen} or {Over}, I am choosing {Chosen}, because {Because}";
    }

    private static DecisionParts? Parts(GdrDecideRequest request)
    {
        string[] parts = [request.Choosing, request.Over, request.Because];
        if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part.Contains('\n') || part.Contains('\r'))) return null;
        var because = request.Because.Trim();
        if (!because.EndsWith('.') && !because.EndsWith('!') && !because.EndsWith('?')) because += ".";
        return new DecisionParts(request.Choosing.Trim(), request.Over.Trim(), because);
    }

    /// <summary>Why the first part is not inline Markdown (it would be a block), else null.</summary>
    private static string? BlockPart(GdrDecideRequest request, GdrShowResponse shown, string projectName)
    {
        var form = new FieldWorksMarkdownSourceForm(GdrMarkdownOptions.For(shown, projectName));
        foreach (var (name, part) in new[] { ("--choosing", request.Choosing), ("--over", request.Over), ("--because", request.Because) })
        {
            var read = form.Read(part.Trim(), true);
            var error = read.Diagnostics.FirstOrDefault(item => item.Severity == DiagnosticSeverity.Error);
            if (error is not null) return $"{name} is not inline Markdown: {error.Message}";
            if (read.Paragraphs.Count != 1) return $"{name} must be inline Markdown with no block (heading, list, quote or fence).";
        }
        return PairsAcrossParts(request, form);
    }

    /// <summary>Why a part has a marker left open, else null.</summary>
    private const string Open = "A part has a marker (* _ ` < [ { or \\) that is not closed within that part, so on the joined line it would pair with another part.";

    private static string? PairsAcrossParts(GdrDecideRequest request, FieldWorksMarkdownSourceForm form)
    {
        if (Parts(request) is not { } parts) return null;
        var alone = new Dictionary<string, IReadOnlyList<TextRun>>();
        foreach (var text in new[] { parts.Chosen, parts.Over, parts.Because })
            alone[text] = GdrMarkdownImport.Runs(form.Read(text, true).Paragraphs[0]);
        var plainWs = GdrMarkdownImport.Runs(form.Read("x", true).Paragraphs[0])[0].Ws;
        TextRun Plain(string text) => new(text, plainWs);
        var expected = alone[parts.Chosen].Append(Plain(" or ")).Concat(alone[parts.Over]).Append(Plain(", I am choosing "))
            .Concat(alone[parts.Chosen]).Append(Plain(", because ")).Concat(alone[parts.Because]);
        var joinedRead = form.Read(parts.Sentence, true);
        if (joinedRead.HasErrors || joinedRead.Paragraphs.Count != 1) return Open;
        var joined = GdrMarkdownImport.Runs(joinedRead.Paragraphs[0]);
        if (Shape(expected).SequenceEqual(Shape(joined))) return null;
        return Open;
    }

    // Adjacent runs that differ in nothing but their text are one run, however the reader split them.
    private static IEnumerable<string> Shape(IEnumerable<TextRun> runs)
    {
        string? key = null;
        var text = new System.Text.StringBuilder();
        foreach (var run in runs)
        {
            var next = string.Join("|", run.Ws, run.CharacterStyle, run.ObjData,
                string.Join(",", (run.Props ?? TextProps.None).Ints.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")),
                string.Join(",", (run.Props ?? TextProps.None).Strings.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}")));
            if (key is not null && key != next) { yield return key + "\n" + text; text.Clear(); }
            key = next;
            text.Append(run.Text);
        }
        if (key is not null) yield return key + "\n" + text;
    }

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
