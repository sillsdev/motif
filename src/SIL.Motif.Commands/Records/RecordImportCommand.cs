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
    internal static readonly Func<string>[] Overlays =
    [
        () => ProjectRecordSchema.LexiconEntryOverlay,
        () => ProjectRecordSchema.NotebookRecordOverlay,
        () => ProjectRecordSchema.ListItemOverlay,
        () => ProjectRecordSchema.PhonologicalRuleOverlay,
    ];

    /// <summary>Runs the import described by <paramref name="request"/>.</summary>
    public static CommandOutcome<RecordImportResponse> Run(RecordImportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((request.FilePath is null) == (request.DirPath is null))
            return Refused(RefusalCodes.RecordImportFileOrDir, FailureReason.InvalidArgument, "Give --file or --dir, not both.");
        if (request.DirPath is not null) return RunDir(request);
        if (request.Page < 1)
            return Refused(RefusalCodes.RecordImportInvalidPage, FailureReason.InvalidArgument,
                "The page must be a whole number from 1.", ("page", request.Page.ToString()));
        if (request.Strict && request.DraftName is null)
            return Refused(RefusalCodes.RecordImportStrictNeedsDraft, FailureReason.InvalidArgument,
                "Strict applies to staging, so it needs a Draft name.");
        string text;
        try
        {
            text = File.ReadAllText(request.FilePath!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return Refused(RefusalCodes.RecordImportFileUnreadable, FailureReason.InvalidArgument,
                "The record file could not be read: " + exception.GetType().Name + ".",
                ("file", Path.GetFileName(request.FilePath!)));
        }
        var located = ProjectStoreCommand.LocateProject(request.ProjectPath);
        if (!located.Succeeded) return CommandOutcome<RecordImportResponse>.Refused(located.Refusal!);

        var read = ProjectStoreCommand.RunOperation(request.ProjectPath, () =>
            ProjectReadCache.ReadSaved(located.Value!, (_, cache) =>
            {
                Planned? planned;
                try
                {
                    planned = Plan(cache, text, validate: true);
                }
                catch (RecordImportScratchException exception)
                {
                    return CommandOutcome<Planned>.Refused(new Refusal(RefusalCodes.RecordImportPlanRefused, FailureReason.Refused,
                        exception.Message, new Dictionary<string, string> { ["refusalKind"] = "scratch-failed" }));
                }
                return planned is not null ? CommandOutcome<Planned>.Success(planned)
                    : CommandOutcome<Planned>.Refused(new Refusal(RefusalCodes.RecordImportPlanRefused, FailureReason.Refused,
                        "The file names no known record kind in its record: line, so it was not planned.",
                        new Dictionary<string, string> { ["refusalKind"] = "unknown-record-kind" }));
            }));
        if (!read.Succeeded) return CommandOutcome<RecordImportResponse>.Refused(read.Refusal!);
        var found = read.Value!;

        if (found.Plan.ChangeSet.IsRefusedWhole)
            return CommandOutcome<RecordImportResponse>.Refused(WholeFileRefusal(found));

        if (found.Lowering.WholeFile is { } shape)
            return CommandOutcome<RecordImportResponse>.Refused(WholeCreateRefusal(found, shape));

        var pages = PageCount(found.Lowering.Operations.Count, Refusals(found, found.Lowering).Count,
            found.Plan.ChangeSet.Changes.Count);
        if (request.Page > pages)
            return Refused(RefusalCodes.RecordImportInvalidPage, FailureReason.InvalidArgument,
                $"The file has {pages} page(s); page {request.Page} does not exist.",
                ("page", request.Page.ToString()), ("pageCount", pages.ToString()));

        if (request.DraftName is null)
            return CommandOutcome<RecordImportResponse>.Success(Summarize(found, found.Lowering, DryRun, request.Page, null, null));

        if (found.Plan.BaseVerdict == ProjectRecordBaseVerdict.New)
        {
            var creating = StageCreate(request, found);
            if (creating is not null) return creating;
        }

        if (request.Strict && Refusals(found, found.Lowering).Count > 0)
            return Refused(RefusalCodes.RecordImportStrictRefused, FailureReason.Refused,
                "Strict mode: the file has changes that were refused, so no Draft was made. Run it without a Draft name to list them.",
                ("refusals", Refusals(found, found.Lowering).Count.ToString()));

        if (found.Lowering.Operations.Count == 0)
            return Refused(RefusalCodes.RecordImportNothingToStage, FailureReason.Refused,
                "The file lowers to no operation, so no Draft was made.",
                ("changes", found.Plan.ChangeSet.Changes.Count.ToString()),
                ("refusals", Refusals(found, found.Lowering).Count.ToString()));

        var created = ProposalCommands.New(new(request.ProjectPath, MotifProductVersion.CurrentText, request.DraftName, "Record import"));
        if (!created.Succeeded) return CommandOutcome<RecordImportResponse>.Refused(created.Refusal!);
        BeforeStage?.Invoke();
        // The stage plans the file again against the project it reads and stages only if that is what was previewed.
        var staged = ProposalCommands.StageRecordImport(request.ProjectPath, request.DraftName,
            found.Plan.Export?.Base ?? "", cache =>
            {
                if (Plan(cache, text, validate: false) is not { } again || again.Digest != found.Digest)
                    throw new RecordImportChangedException();
                return found.Lowering;
            });
        if (!staged.Succeeded)
        {
            // A Draft this command made and could not fill is not left behind.
            var discarded = ProposalCommands.DiscardNamedDraft(
                new(request.ProjectPath, MotifProductVersion.CurrentText, request.DraftName, null));
            if (discarded.Succeeded) return CommandOutcome<RecordImportResponse>.Refused(staged.Refusal!);
            var facts = new Dictionary<string, string>(staged.Refusal!.Facts)
            {
                ["draftNotDiscarded"] = request.DraftName,
                ["discardRefusal"] = discarded.Refusal!.Code,
            };
            return CommandOutcome<RecordImportResponse>.Refused(new Refusal(staged.Refusal.Code, staged.Refusal.Reason,
                staged.Refusal.Message + " The empty Draft '" + request.DraftName + "' could not be discarded; discard it with discard-draft.",
                facts));
        }
        return CommandOutcome<RecordImportResponse>.Success(Summarize(found, staged.Value!.Lowering, DraftMode, request.Page,
            created.Value!.DraftName, created.Value.ProposalId));
    }

    /// <summary>A planned file, what would be staged, and a digest taken before any refusal.</summary>
    private sealed record Planned(ProjectRecordSchemaResult Schema, ProjectRecordPlan Plan, RecordLowering Lowering, string Digest);

    private static Planned? Plan(LcmCache cache, string text, bool validate, SchemaSet? schemas = null)
    {
        var name = KindName(text);
        if (name is null) return null;
        if ((schemas ?? new SchemaSet(cache)).For(name) is not { } resolved) return null;
        var plan = resolved.Importer.Plan(text, resolved.Catalog);
        var full = resolved.Lowerer.Lower(cache, plan);
        return new(resolved.Schema, plan, validate ? ValidateOnScratch(cache, plan, full) : full, Digest(full.Operations));
    }

    /// <summary>What one record kind needs to plan and lower a file.</summary>
    private sealed record KindTools(ProjectRecordSchemaResult Schema, ProjectRecordImporter Importer,
        CatalogWritableFields Catalog, RecordChangeSetLowerer Lowerer);

    /// <summary>The overlays' schemas, generated on first need and shared by a batch.</summary>
    private sealed class SchemaSet(LcmCache cache)
    {
        private readonly List<KindTools> _generated = [];

        public KindTools? For(string kindName)
        {
            foreach (var tools in _generated)
                if (tools.Schema.Schema.Kind(kindName) is not null) return tools;
            while (_generated.Count < Overlays.Length)
            {
                var schema = ProjectRecordSchema.Generate(cache, Overlays[_generated.Count]());
                var tools = new KindTools(schema, new ProjectRecordImporter(new ProjectRecordExporter(cache, schema)),
                    new CatalogWritableFields(schema), new RecordChangeSetLowerer(schema, new RecordWritableFields(schema)));
                _generated.Add(tools);
                if (schema.Schema.Kind(kindName) is not null) return tools;
            }
            return null;
        }
    }

    /// <summary>The whole unquoted value of the front matter's record: line.</summary>
    private static string? KindName(string text)
    {
        using var reader = new StringReader(text);
        if (reader.ReadLine()?.TrimEnd() != "---") return null;
        while (reader.ReadLine() is { } line && line.TrimEnd() != "---")
        {
            if (!line.StartsWith("record:", StringComparison.Ordinal)) continue;
            var value = line["record:".Length..].Trim();
            if (value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0]) value = value[1..^1];
            return value;
        }
        return null;
    }

    private static CommandOutcome<RecordImportResponse>? StageCreate(RecordImportRequest request, Planned found)
    {
        var guid = found.Plan.RecordGuid!.Value;
        var scan = ScanPending(request.ProjectPath, guid);
        if (!scan.Succeeded)
            return Refused(RefusalCodes.RecordImportGuidPendingUnchecked, FailureReason.Refused,
                "Other Drafts and Proposals could not be read to see whether one already creates " + guid.ToString("D") + ", so nothing was staged. Try again.",
                ("guid", CanonicalId.FromGuid(guid).Value), ("storeRefusal", scan.Refusal!.Code));
        if (scan.Value!.Name is { } pending)
            return Refused(RefusalCodes.RecordImportGuidPending, FailureReason.Refused,
                "Proposal " + pending + " already creates " + guid.ToString("D") + ". Apply or discard it first, or use another GUID.",
                ("guid", CanonicalId.FromGuid(guid).Value), ("proposal", pending));
        if (found.Lowering.Operations.Count > 0) return null;
        return Refused(RefusalCodes.RecordImportCreateKindNotCreatable, FailureReason.Refused,
            CreateNotLowered, ("reason", "not-yet-lowered"), ("recordKind", found.Plan.Kind ?? ""),
            ("guid", CanonicalId.FromGuid(guid).Value));
    }

    private static Refusal WholeCreateRefusal(Planned found, RecordLoweringRefusal shape)
    {
        var refusal = shape.ToRefusal();
        var facts = new Dictionary<string, string>(refusal.Facts) { ["baseVerdict"] = found.Plan.BaseVerdict.ToString().ToLowerInvariant() };
        if (found.Plan.Kind is { } kind) facts["recordKind"] = kind;
        return new Refusal(refusal.Code, refusal.Reason, Clip(refusal.Message, 600)!, facts);
    }

    internal const string CreateNotLowered =
        "The file plans a new record of a kind whose creation is not yet lowered to operations, so nothing was staged. Use the dry run to check the file.";

    /// <summary>Tests replace the pending scan to make the store unreadable.</summary>
    internal static Func<string, Guid, CommandOutcome<PendingScan>>? PendingScanOverride { get; set; }

    /// <summary>The first open Draft or Proposal that names the entity id.</summary>
    private static CommandOutcome<PendingScan> ScanPending(string projectPath, Guid guid)
    {
        if (PendingScanOverride is { } replaced) return replaced(projectPath, guid);
        var id = CanonicalId.FromGuid(guid).Value;
        return ProjectStoreCommand.ReadExisting(projectPath, CommandOutcome<PendingScan>.Success(new(null)), (database, _) =>
        {
            var repository = new SIL.Motif.Worker.Store.ProposalRepository(database);
            foreach (var record in repository.ListDrafts())
                if (record.ProposalJson?.Contains(id, StringComparison.Ordinal) == true)
                    return CommandOutcome<PendingScan>.Success(new(record.DraftName ?? record.ProposalId.Value));
            foreach (var record in repository.List(new(null, false)).Where(record => record.DraftName is null))
                if (record.ProposalJson?.Contains(id, StringComparison.Ordinal) == true)
                    return CommandOutcome<PendingScan>.Success(new(record.ProposalId.Value));
            return CommandOutcome<PendingScan>.Success(new(null));
        });
    }

    internal sealed record PendingScan(string? Name);

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
        var message = "The record file was refused whole (" + whole.Kind + "): " + Clip(whole.Reason, 300);
        return whole.Kind switch
        {
            RecordRefusalKind.CreateKindNotCreatable => new Refusal(RefusalCodes.RecordImportCreateKindNotCreatable, FailureReason.Refused, message, facts),
            RecordRefusalKind.CreatePartial => new Refusal(RefusalCodes.RecordImportCreatePartial, FailureReason.Refused, message, facts),
            RecordRefusalKind.RecordGone => new Refusal(RefusalCodes.RecordImportRecordGone, FailureReason.Refused, message, facts),
            RecordRefusalKind.GuidInvalid => new Refusal(RefusalCodes.RecordImportGuidInvalid, FailureReason.Refused, message, facts),
            RecordRefusalKind.GuidRetired => new Refusal(RefusalCodes.RecordImportGuidRetired, FailureReason.Refused, message, facts),
            RecordRefusalKind.CreateComputedField => new Refusal(RefusalCodes.RecordImportCreateComputedField, FailureReason.Refused, message, facts),
            _ => new Refusal(RefusalCodes.RecordImportPlanRefused, FailureReason.Refused, message, facts),
        };
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
        var changes = found.Plan.ChangeSet.Changes;
        var pageCount = PageCount(operations.Count, refusals.Count, changes.Count);
        var pageOperations = operations.Skip((page - 1) * size).Take(size).ToList();
        var pageRefusals = refusals.Skip((page - 1) * size).Take(size).ToList();
        var pageChanges = changes.Skip((page - 1) * size).Take(size).ToList();
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
                    Reason = Clip(row.Reason, length)! }).ToList(), draftName, proposalId,
                pageChanges.Select((change, offset) => ChangeRow(change, length,
                    lowering.RefusedChanges?.GetValueOrDefault((page - 1) * size + offset))).ToList());
            if (length <= 1 || ProjectionJson.ToUtf8Bytes(response).Length <= RecordImportPaging.SummaryBytes)
                return response;
        }
    }

    private static int PageCount(int operations, int refusals, int changes) =>
        Math.Max(1, (Math.Max(Math.Max(operations, refusals), changes) + RecordImportPaging.PageSize - 1) / RecordImportPaging.PageSize);

    private static RecordImportChange ChangeRow(RecordChange change, int length, string? refusalCode) =>
        new(change.Kind.ToString(), Clip(change.ObjectType, length), Clip(change.Field, length)!,
            change.ObjectGuid is { } guid ? CanonicalId.FromGuid(guid).Value : null,
            Clip(change.WritingSystem, length), Clip(change.Old, length), Clip(change.New, length),
            refusalCode is null ? "staged" : "refused", refusalCode);

    /// <summary>Returns a refusal for an agent path that leaves the allowed roots.</summary>
    /// <returns>The refusal, or <see langword="null"/> when the path may be read.</returns>
    public static Refusal? RefuseOutsideAgentRoots(string projectPath, string filePath, string workingDirectory, bool forWrite = false)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string real;
        string[] roots;
        try
        {
            real = RealPath(filePath);
            roots = [RealPath(Path.GetDirectoryName(Path.GetFullPath(projectPath))!), RealPath(workingDirectory)];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return Unreadable(filePath, forWrite);
        }
        foreach (var root in roots)
        {
            var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
            if (real.StartsWith(prefix, comparison)) return null;
        }
        return Unreadable(filePath, forWrite);
    }

    private static Refusal Unreadable(string filePath, bool forWrite) => new(
        forWrite ? RefusalCodes.RecordExportOutputUnwritable : RefusalCodes.RecordImportFileUnreadable,
        FailureReason.InvalidArgument,
        "An agent may " + (forWrite ? "write" : "read") + " a record file only under the project's folder or the working directory.",
        new Dictionary<string, string> { ["file"] = Path.GetFileName(filePath) });

    /// <summary>The full path with every link followed, so a link in an allowed folder cannot lead out.</summary>
    private static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full)!;
        var current = root;
        foreach (var part in full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is not null && info.ResolveLinkTarget(true) is { } target)
                current = Path.GetFullPath(target.FullName);
        }
        return current;
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
