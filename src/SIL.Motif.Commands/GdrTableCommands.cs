using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Gdr;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;
using SIL.TextEditing;

namespace SIL.Motif.Commands;

/// <summary>
/// Stages the saved tables of Grammar Guide query blocks (ADR 0005). A refresh is the only thing that writes a table: it runs each
/// block's query through the read-only evaluator, and stages the caption and the first ten rows right after the block in the Draft, to be
/// applied like any edit. Viewing, opening and checking never call it.
/// </summary>
public static class GdrTableCommands
{
    /// <summary>
    /// Refreshes the saved table of each <c>motif query corpus forms</c> block of the section (or of every section) from a live run.
    /// A block whose query is refused, has no built parse index or is another kind of line keeps the table it has, and is named in the answer.
    /// </summary>
    public static CommandOutcome<GdrRefreshTablesResponse> Refresh(GdrRefreshTablesRequest request, string? parserPath = null,
        CancellationToken cancellationToken = default, DateOnly? today = null, ProjectSession? session = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var own = session is null ? new ProjectSession() : null;
        session ??= own;
        var ids = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Id))
        {
            var list = GdrQuery.List(new GdrListRequest(request.ProjectPath));
            if (!list.Succeeded) return CommandOutcome<GdrRefreshTablesResponse>.Refused(list.Refusal!);
            ids.AddRange(list.Value!.Records.Select(record => record.Id));
        }
        else ids.Add(request.Id.Trim());
        var named = string.IsNullOrWhiteSpace(request.Draft) ? null : request.Draft.Trim();
        var projectName = Path.GetFileNameWithoutExtension(request.ProjectPath);
        var date = today ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var sections = new List<GdrRefreshedSection>();
        var warnings = new List<string>();
        var created = new List<string>();
        var operations = 0;
        string? draftName = null;
        foreach (var id in ids)
        {
            var shown = GdrQuery.Show(new GdrShowRequest(request.ProjectPath, id));
            if (!shown.Succeeded) return CommandOutcome<GdrRefreshTablesResponse>.Refused(shown.Refusal!);
            var current = GdrQuery.Show(new GdrShowRequest(request.ProjectPath, id, AsOf: named ?? PendingChanges.DraftName));
            if (!current.Succeeded && (named is not null || current.Refusal!.Code != "draft.not-found"))
                return CommandOutcome<GdrRefreshTablesResponse>.Refused(current.Refusal!);
            var basis = current.Succeeded ? current.Value! : shown.Value!;
            var paragraphs = GdrMarkdownView.ProjectParagraphs(basis);
            var sites = GdrSavedTables.Sites(paragraphs);
            if (sites.Count == 0) continue;
            var definitions = GuideQueryBlocks.Parse(string.Join("\n", paragraphs.Select(p => string.Concat(p.Runs.Select(run => run.Text))))).Queries;
            var tables = new Dictionary<string, IReadOnlyList<Paragraph>?>(StringComparer.Ordinal);
            var done = new List<GdrRefreshedTable>();
            foreach (var site in sites.Where(site => request.QueryId is null || site.QueryId == request.QueryId))
            {
                var query = definitions.First(definition => definition.Id == site.QueryId);
                done.Add(RefreshOne(query, site, paragraphs, basis, request.ProjectPath, parserPath, cancellationToken, date, tables, session));
            }
            sections.Add(new GdrRefreshedSection(shown.Value!.Record.Id, shown.Value.Record.Abbreviation, done));
            if (tables.Count == 0) continue;
            var edits = GdrMarkdownImport.FromParagraphs(shown.Value, projectName, GdrSavedTables.Replace(paragraphs, tables));
            if (edits is null) continue;
            var staged = ProposalCommands.StageGdrMarkdown(request.ProjectPath, edits, named, created);
            if (!staged.Succeeded) return CommandOutcome<GdrRefreshTablesResponse>.Refused(staged.Refusal!);
            draftName = staged.Value!.LastOrDefault()?.DraftName ?? draftName ?? named ?? PendingChanges.DraftName;
            operations += staged.Value!.Sum(item => item.Operations.Count);
        }
        if (request.QueryId is not null && sections.All(section => section.Tables.Count == 0))
            warnings.Add($"No query block has the id '{request.QueryId}'.");
        return CommandOutcome<GdrRefreshTablesResponse>.Success(
            new GdrRefreshTablesResponse(draftName, sections, operations, warnings) { StylesCreated = created.ToArray() });
    }

    private static GdrRefreshedTable RefreshOne(GuideQueryDefinition query, GdrSavedTableSite site, IReadOnlyList<Paragraph> paragraphs,
        GdrShowResponse basis, string projectPath, string? parserPath, CancellationToken cancellationToken, DateOnly date,
        Dictionary<string, IReadOnlyList<Paragraph>?> tables, ProjectSession? session)
    {
        GdrRefreshedTable Skipped(string why) => new(query.Id, "skipped", why, 0, 0);
        if (!QueryRunLine.TryParseRun(query.Run, out var line, out _) || line.Corpus is null)
            return Skipped("only a 'motif query corpus forms' run line has a table to save.");
        var live = LiveTable(query, projectPath, parserPath, cancellationToken, session);
        if (live.Status != GuideEvidenceStatus.Ready || live.Table is not { } table) return Skipped(live.Message);
        var rows = table.Rows.Take(GdrSavedTables.MaxRows).ToArray();
        var total = table.Rows.Count + table.Truncated;
        var caption = GdrSavedTables.Caption(date, GdrSavedTables.ShortBaselineId(live.BaselineId), !table.Complete, total);
        var built = GdrSavedTables.Build(caption, table.Columns, rows, basis.DefaultAnalysis, string.IsNullOrEmpty(basis.DefaultVernacular) ? null : basis.DefaultVernacular, IsFormColumn);
        if (site.HasTable && SameBody(paragraphs, site, built))
            return new GdrRefreshedTable(query.Id, "unchanged", "the saved table already matches a live run.", rows.Length, total);
        tables[query.Id] = built;
        return new GdrRefreshedTable(query.Id, "saved", table.Headline, rows.Length, total);
    }

    // Same rows: a new date or Baseline alone is no refresh, since saving moves the Baseline.
    private static bool SameBody(IReadOnlyList<Paragraph> paragraphs, GdrSavedTableSite site, IReadOnlyList<Paragraph> built)
    {
        if (site.Caption is not { } caption || site.TableCount != built.Count) return false;
        for (var i = 1; i < built.Count; i++)
        {
            var held = paragraphs[site.TableFirst + i];
            if (held.Style != built[i].Style || Text(held) != Text(built[i])) return false;
        }
        var total = GdrSavedTables.TryParseCaption(Text(built[0]), out var fresh) ? fresh : null;
        return total is not null && total.Rows == caption.Rows && total.AtLeast == caption.AtLeast;
    }

    private static string Text(Paragraph paragraph) => string.Concat(paragraph.Runs.Select(run => run.Text));

    /// <summary>Whether a result column holds word forms, which a saved table writes in the vernacular writing system.</summary>
    internal static bool IsFormColumn(string column) => column == "form" || column.EndsWith(".form", StringComparison.Ordinal) || column == "example";

    /// <summary>One block's query as it runs now, shaped with the block's own columns and sort and no limit.</summary>
    internal static GuideEvidenceResult LiveTable(GuideQueryDefinition query, string projectPath, string? parserPath, CancellationToken cancellationToken,
        ProjectSession? session = null) =>
        GuideEvidenceRunner.Evaluate(query.Run, GuideEvidenceRunner.BaseDirectoryOf(projectPath), parserPath, cancellationToken, projectPath, session: session)
            .Shaped(query.Columns, query.Sort, null);
}
