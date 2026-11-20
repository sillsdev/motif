using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;

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
        var named = string.IsNullOrWhiteSpace(request.Draft) ? null : request.Draft.Trim();
        var current = GdrQuery.Show(new GdrShowRequest(request.ProjectPath, request.Id, AsOf: named ?? PendingChanges.DraftName));
        if (!current.Succeeded && (named is not null || current.Refusal!.Code != "draft.not-found"))
            return CommandOutcome<GdrEditResponse>.Refused(current.Refusal!);
        var body = new GdrMarkdownView(current.Succeeded ? current.Value! : shown.Value!, Path.GetFileNameWithoutExtension(request.ProjectPath)).Fields
            .Single(field => field.Name == "discussion").Body;
        return Stage(request.ProjectPath, shown.Value!, WithDecision(body, entry), request.Draft, null);
    }

    /// <summary>The judgement call as one list item with its Markdown characters escaped, or null when a part is blank or spans lines.</summary>
    internal static string? Entry(GdrDecideRequest request)
    {
        string[] parts = [request.Choosing, request.Over, request.Because];
        if (parts.Any(part => string.IsNullOrWhiteSpace(part) || part.Contains('\n') || part.Contains('\r'))) return null;
        var chosen = Escape(request.Choosing.Trim());
        var because = Escape(request.Because.Trim());
        if (!because.EndsWith('.') && !because.EndsWith('!') && !because.EndsWith('?')) because += ".";
        return $"- {chosen} or {Escape(request.Over.Trim())}, I am choosing {chosen}, because {because}";
    }

    private static string Escape(string text)
    {
        var escaped = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '[' or ']' or '`' or '<' or '>') escaped.Append('\\');
            escaped.Append(c);
        }
        return escaped.ToString();
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
            " Only existing sections can be edited; the CLI cannot create a section yet, so add the new section in FieldWorks first.", refusal.Facts);

    private static CommandOutcome<GdrEditResponse> Stage(string projectPath, GdrShowResponse shown, string markdown, string? draft,
        GdrShowResponse? staged)
    {
        var read = GdrMarkdownImport.Read(shown, Path.GetFileNameWithoutExtension(projectPath), markdown, staged);
        if (read.HasErrors)
            return CommandOutcome<GdrEditResponse>.Refused(new Refusal(RefusalCodes.GdrMarkdownInvalid, FailureReason.InvalidArgument,
                "The Markdown cannot be read, so nothing was staged: " + string.Join(" ", read.Errors) +
                (read.Errors.Any(error => error.Contains("code block", StringComparison.OrdinalIgnoreCase))
                    ? " Query blocks (motif-query and motif-show) are added in FieldWorks; a block the section already holds can stay as it is, but guide edit cannot add one."
                    : "")));
        var draftName = string.IsNullOrWhiteSpace(draft) ? null : draft.Trim();
        var result = ProposalCommands.StageGdrMarkdown(projectPath, read.Edits!, draftName);
        if (!result.Succeeded) return CommandOutcome<GdrEditResponse>.Refused(result.Refusal!);
        var response = result.Value!.LastOrDefault();
        return CommandOutcome<GdrEditResponse>.Success(new GdrEditResponse(response?.DraftName ?? draftName ?? PendingChanges.DraftName,
            shown.Record.Id, shown.Record.Abbreviation, read.ChangedFields, result.Value!.Sum(item => item.Operations.Count), read.Warnings));
    }
}
