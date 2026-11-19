using System.Text.RegularExpressions;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.TextEditing.Markdown.Records;
using SIL.TextEditing.Project;

namespace SIL.Motif.Commands.Records;

/// <summary>
/// The <c>import --file</c> command: plans an edited record file against the saved project, lowers the plan to
/// operations, and either reports what it found (a dry run, which writes nothing) or stages the operations in a new
/// Draft. It never applies: only a person can Apply. The CLI and the MCP tool both call this and return
/// its <see cref="RecordImportResponse"/>.
/// </summary>
public static partial class RecordImportCommand
{
    private const string DryRun = "dryRun";
    private const string DraftMode = "draft";

    /// <summary>The shipped overlays in the order a file's kind is looked for; each yields its own schema.</summary>
    private static readonly Func<string>[] Overlays =
    [
        () => ProjectRecordSchema.LexiconEntryOverlay,
        () => ProjectRecordSchema.NotebookRecordOverlay,
        () => ProjectRecordSchema.ListItemOverlay,
    ];

    /// <summary>Runs the import described by <paramref name="request"/>.</summary>
    public static CommandOutcome<RecordImportResponse> Run(RecordImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Page < 1)
            return Refused(RefusalCodes.RecordImportInvalidPage, FailureReason.InvalidArgument,
                "The page must be a whole number from 1.", ("page", request.Page.ToString()));
        string text;
        try
        {
            text = File.ReadAllText(request.FilePath);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return Refused(RefusalCodes.RecordImportFileUnreadable, FailureReason.InvalidArgument,
                "The record file could not be read: " + exception.GetType().Name + ".",
                ("file", Path.GetFileName(request.FilePath)));
        }
        var located = ProjectStoreCommand.LocateProject(request.ProjectPath);
        if (!located.Succeeded) return CommandOutcome<RecordImportResponse>.Refused(located.Refusal!);

        var read = ProjectStoreCommand.RunOperation(request.ProjectPath, () =>
            ProjectReadCache.ReadSaved(located.Value!, (_, cache) =>
            {
                return CommandOutcome<Planned>.Success(Plan(cache, text));
            }));
        if (!read.Succeeded) return CommandOutcome<RecordImportResponse>.Refused(read.Refusal!);
        var found = read.Value!;

        if (found.Plan.ChangeSet.IsRefusedWhole)
            return CommandOutcome<RecordImportResponse>.Refused(WholeFileRefusal(found));

        if (request.DraftName is null)
            return CommandOutcome<RecordImportResponse>.Success(Summarize(found, found.Lowering, DryRun, request.Page, null, null));

        if (found.Lowering.Operations.Count == 0)
            return Refused(RefusalCodes.RecordImportNothingToStage, FailureReason.Refused,
                "The file lowers to no operation, so no Draft was made.",
                ("changes", found.Plan.ChangeSet.Changes.Count.ToString()),
                ("refusals", Refusals(found, found.Lowering).Count.ToString()));

        var created = ProposalCommands.New(new(request.ProjectPath, MotifProductVersion.CurrentText, request.DraftName, "Record import"));
        if (!created.Succeeded) return CommandOutcome<RecordImportResponse>.Refused(created.Refusal!);
        var staged = ProposalCommands.StageRecordImport(request.ProjectPath, request.DraftName, found.Schema, found.Plan);
        if (!staged.Succeeded)
        {
            // A Draft this command made and could not fill is not left behind.
            ProposalCommands.DiscardNamedDraft(new(request.ProjectPath, MotifProductVersion.CurrentText, request.DraftName, null));
            return CommandOutcome<RecordImportResponse>.Refused(staged.Refusal!);
        }
        return CommandOutcome<RecordImportResponse>.Success(Summarize(found, staged.Value!.Lowering, DraftMode, request.Page,
            created.Value!.DraftName, created.Value.ProposalId));
    }

    private sealed record Planned(ProjectRecordSchemaResult Schema, ProjectRecordPlan Plan, RecordLowering Lowering);

    private static Planned Plan(LcmCache cache, string text)
    {
        var name = KindName(text);
        ProjectRecordSchemaResult? schema = null;
        foreach (var overlay in Overlays)
        {
            var candidate = ProjectRecordSchema.Generate(cache, overlay());
            schema ??= candidate;
            if (name is not null && candidate.Schema.Kind(name) is not null)
            {
                schema = candidate;
                break;
            }
        }
        var plan = ProjectRecordImporter.Plan(cache, schema!, text, new CatalogWritableFields());
        var lowering = new RecordChangeSetLowerer(schema!, new RecordWritableFields(schema!)).Lower(cache, plan);
        return new(schema!, plan, lowering);
    }

    [GeneratedRegex(@"^kind:[ \t]*[""']?([A-Za-z0-9_.\-]+)", RegexOptions.Multiline)]
    private static partial Regex KindLine();

    private static string? KindName(string text)
    {
        var match = KindLine().Match(text);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static Refusal WholeFileRefusal(Planned found)
    {
        var whole = found.Plan.ChangeSet.Refusals.First(item => item.WholeFile);
        var facts = new Dictionary<string, string>
        {
            ["refusalKind"] = whole.Kind.ToString(),
            ["baseVerdict"] = found.Plan.BaseVerdict.ToString().ToLowerInvariant(),
        };
        if (found.Plan.Kind is { } kind) facts["recordKind"] = kind;
        if (whole.Field.Length > 0) facts["field"] = whole.Field;
        return new Refusal(RefusalCodes.RecordImportPlanRefused, FailureReason.Refused,
            "The record file was refused whole (" + whole.Kind + "): " + Clip(whole.Reason, 300), facts);
    }

    private static List<RecordImportRefusal> Refusals(Planned found, RecordLowering lowering)
    {
        var rows = new List<RecordImportRefusal>();
        foreach (var refusal in found.Plan.ChangeSet.Refusals)
            rows.Add(new(RefusalCodes.RecordImportChangeRefused, null, Clip(refusal.Field),
                refusal.ObjectGuid is { } guid ? CanonicalId.FromGuid(guid).Value : null,
                Clip(refusal.Kind + ": " + refusal.Reason)));
        foreach (var refusal in lowering.Refusals)
            rows.Add(new(refusal.Code, Clip(refusal.Class), Clip(refusal.Field),
                refusal.ObjectGuid is { } guid ? CanonicalId.FromGuid(guid).Value : null, Clip(refusal.Reason)));
        return rows;
    }

    private static RecordImportResponse Summarize(Planned found, RecordLowering lowering, string mode, int page,
        string? draftName, string? proposalId)
    {
        var refusals = Refusals(found, lowering);
        var operations = lowering.Operations;
        var size = RecordImportPaging.PageSize;
        var pageCount = Math.Max(1, (Math.Max(operations.Count, refusals.Count) + size - 1) / size);
        var pageOperations = operations.Skip((page - 1) * size).Take(size).ToList();
        var pageRefusals = refusals.Skip((page - 1) * size).Take(size).ToList();
        // Text is shortened until the response fits, so long or non-ASCII names stay within 2 KB.
        for (var length = RecordImportPaging.TextLength; ; length /= 2)
        {
            var response = new RecordImportResponse(mode, found.Plan.Kind,
                found.Plan.RecordGuid is { } record ? CanonicalId.FromGuid(record).Value : null,
                found.Plan.BaseVerdict.ToString().ToLowerInvariant(), found.Plan.ChangeSet.Changes.Count, operations.Count,
                refusals.Count, page, pageCount,
                pageOperations.Select(operation => new RecordImportOperation(operation.OperationId.Value,
                    Clip(operation.Kind, length)!, operation.Target?.Value)).ToList(),
                pageRefusals.Select(row => row with { Class = Clip(row.Class, length), Field = Clip(row.Field, length)!,
                    Reason = Clip(row.Reason, length)! }).ToList(), draftName, proposalId);
            if (length <= 4 || ProjectionJson.ToUtf8Bytes(response).Length <= RecordImportPaging.SummaryBytes)
                return response;
        }
    }

    private static string Clip(string? text) => Clip(text ?? "", RecordImportPaging.TextLength) ?? "";

    private static string? Clip(string? text, int length)
    {
        if (text is null) return null;
        var line = text.ReplaceLineEndings(" ");
        return line.Length <= length ? line : line[..(length - 1)] + "\u2026";
    }

    private static CommandOutcome<RecordImportResponse> Refused(string code, FailureReason reason, string message,
        params (string Name, string Value)[] facts) =>
        CommandOutcome<RecordImportResponse>.Refused(new Refusal(code, reason, message,
            facts.ToDictionary(fact => fact.Name, fact => fact.Value)));
}
