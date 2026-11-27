using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

/// <summary>The folder holds more files or operations than one Proposal takes.</summary>
internal sealed class RecordImportBatchTooLargeException(int files, int operations) : Exception("The folder is too large.")
{
    public int Files { get; } = files;
    public int Operations { get; } = operations;
}

public static partial class RecordImportCommand
{
    private const string CreatedStatus = "created";
    private const string UpdatedStatus = "updated";
    private const string UnchangedStatus = "unchanged";
    private const string RefusedStatus = "refused";

    /// <summary>Tests replace the file reader to make a file unreadable.</summary>
    internal static Func<string, string>? FileReaderOverride { get; set; }

    /// <summary>One file of the folder as it was read.</summary>
    private sealed record DirInput(string Path, string FullPath, string? Text, string? Sha, string? ReadError);

    /// <summary>One file's row, with what the plan made of it.</summary>
    private sealed class DirFile(DirInput input)
    {
        public DirInput Input { get; } = input;
        public Planned? Planned { get; set; }
        public RecordLowering? Lowering { get; set; }
        public string Status { get; set; } = UnchangedStatus;
        public string Verdict { get; set; } = "not-checked";
        public string? Kind { get; set; }
        public Guid? Record { get; set; }
        public string? Base { get; set; }
        public string? Code { get; set; }
        public string? Reason { get; set; }
        public int Changes { get; set; }
        public int RefusedChanges { get; set; }
        /// <summary>Paths of the other files of the folder this file refers to or depends on.</summary>
        public SortedSet<string> DependsOn { get; } = new(StringComparer.Ordinal);
        public int Operations => Status is CreatedStatus or UpdatedStatus ? Lowering?.Operations.Count ?? 0 : 0;

        public void Refuse(Refusal refusal) => Refuse(refusal.Code, refusal.Message);

        public void Refuse(string code, string reason)
        {
            Status = RefusedStatus;
            Code = code;
            Reason = Clip(reason, 300);
        }
    }

    /// <summary>The plan of a whole folder: each file's row and the digest a stage compares.</summary>
    private sealed record BatchPlan(IReadOnlyList<DirFile> Files, string Digest)
    {
        public IEnumerable<DirFile> Staged => Files.Where(file => file.Status is CreatedStatus or UpdatedStatus);

        public RecordLowering Lowering => new(Staged.SelectMany(file => file.Lowering!.Operations).ToList(),
            Staged.SelectMany(file => file.Lowering!.Refusals).ToList(),
            Staged.SelectMany(file => file.Lowering!.PlanRefusals).ToList());

        public int Count(string status) => Files.Count(file => file.Status == status);
        public int RefusedChanges => Staged.Sum(file => file.RefusedChanges);
    }

    private sealed record DirListing(IReadOnlyList<(string Relative, string Full)> Files, int Ignored);

    private static CommandOutcome<RecordImportResponse> RunDir(RecordImportRequest request)
    {
        if (request.Page < 1)
            return Refused(RefusalCodes.RecordImportInvalidPage, FailureReason.InvalidArgument,
                "The page must be a whole number from 1.", ("page", request.Page.ToString()));
        if (request.Strict && request.DraftName is null)
            return Refused(RefusalCodes.RecordImportStrictNeedsDraft, FailureReason.InvalidArgument,
                "Strict applies to staging, so it needs a Draft name.");
        foreach (var (name, value) in new[] { ("source", request.Source), ("revision", request.Revision), ("licence", request.Licence) })
            if (value is not null && (value.Length > RecordImportPaging.MaxOptionLength || value.Any(char.IsControl)))
                return Refused(RefusalCodes.RecordImportOptionInvalid, FailureReason.InvalidArgument,
                    "--" + name + " must be text of at most " + RecordImportPaging.MaxOptionLength + " characters with no control characters.",
                    ("option", name));
        var listed = ListDir(request.DirPath!);
        if (!listed.Succeeded) return CommandOutcome<RecordImportResponse>.Refused(listed.Refusal!);
        var located = ProjectStoreCommand.LocateProject(request.ProjectPath);
        if (!located.Succeeded) return CommandOutcome<RecordImportResponse>.Refused(located.Refusal!);
        if (request.ReportPath is { } report && (File.Exists(report) || Directory.Exists(report) ||
            !Directory.Exists(Path.GetDirectoryName(Path.GetFullPath(report)))))
            return Refused(RefusalCodes.RecordExportOutputUnwritable, FailureReason.InvalidArgument,
                "The report file cannot be written at the path given, or a file is already there.",
                ("file", Path.GetFileName(report)));

        var inputs = listed.Value!.Files.Select(item => ReadInput(item.Relative, item.Full)).ToList();
        var excluded = new Dictionary<string, string>(StringComparer.Ordinal);
        CommandOutcome<BatchPlan> read;
        // A file whose operations fail unexpectedly is left out and the folder is planned again on a clean copy.
        while (true)
        {
            read = ProjectStoreCommand.RunOperation(request.ProjectPath, () =>
            ProjectReadCache.ReadSaved(located.Value!, (_, cache) =>
            {
                try
                {
                    return CommandOutcome<BatchPlan>.Success(PlanBatch(cache, inputs, validate: true, excluded));
                }
                catch (RecordImportScratchFileException exception)
                {
                    return CommandOutcome<BatchPlan>.Refused(new Refusal(RefusalCodes.RecordImportPlanRefused, FailureReason.Refused,
                        exception.Message, new Dictionary<string, string> { ["refusalKind"] = "scratch-failed", ["file"] = exception.Path }));
                }
                catch (RecordImportScratchException exception)
                {
                    return CommandOutcome<BatchPlan>.Refused(new Refusal(RefusalCodes.RecordImportPlanRefused, FailureReason.Refused,
                        exception.Message, new Dictionary<string, string> { ["refusalKind"] = "scratch-failed" }));
                }
                catch (RecordImportBatchTooLargeException exception)
                {
                    return CommandOutcome<BatchPlan>.Refused(TooLarge(exception.Files, exception.Operations));
                }
            }));
            if (!read.Succeeded && read.Refusal!.Facts.TryGetValue("file", out var culprit) &&
                excluded.TryAdd(culprit, "The operations of this file failed unexpectedly on a scratch copy, so it was left out and the rest of the folder went on."))
                continue;
            break;
        }
        if (!read.Succeeded) return CommandOutcome<RecordImportResponse>.Refused(read.Refusal!);
        var found = read.Value!;
        var ignored = listed.Value.Ignored;

        var pages = RowPageCount(found, request.RefusedOnly == true);
        if (request.Rows == true && request.Page > pages)
            return Refused(RefusalCodes.RecordImportInvalidPage, FailureReason.InvalidArgument,
                $"The folder has {pages} page(s) of rows; page {request.Page} does not exist.",
                ("page", request.Page.ToString()), ("pageCount", pages.ToString()));

        CommandOutcome<RecordImportResponse> outcome;
        if (request.DraftName is null)
            outcome = CommandOutcome<RecordImportResponse>.Success(Summarize(request, found, ignored, DryRun, null, null));
        else
        {
            var staged = StageDir(request, found, inputs, ignored);
            if (staged.Succeeded && request.ReportPath is not null) return Reported(found, staged);
            return staged;
        }
        return request.ReportPath is not null ? Reported(found, outcome) : outcome;
    }

    private static CommandOutcome<RecordImportResponse> StageDir(RecordImportRequest request, BatchPlan found,
        IReadOnlyList<DirInput> inputs, int ignored)
    {
        var draftName = request.DraftName!;
        var creates = found.Files.Where(file => file.Status == CreatedStatus && file.Record is not null).ToList();
        var pendingRefused = 0;
        string? firstPendingCode = null;
        if (creates.Count > 0)
        {
            var scan = ScanPendingMany(request.ProjectPath, creates.Select(file => file.Record!.Value).ToList());
            if (!scan.Succeeded)
                return Refused(RefusalCodes.RecordImportGuidPendingUnchecked, FailureReason.Refused,
                    "Other Drafts and Proposals could not be read to see whether one already creates a record of this folder, so nothing was staged. Try again.",
                    ("storeRefusal", scan.Refusal!.Code));
            foreach (var file in creates)
            {
                if (!scan.Value!.TryGetValue(file.Record!.Value, out var pending)) continue;
                file.Refuse(RefusalCodes.RecordImportGuidPending,
                    "Proposal " + pending + " already creates " + file.Record.Value.ToString("D") + ". Apply or discard it first, or use another GUID.");
                pendingRefused++;
                firstPendingCode ??= RefusalCodes.RecordImportGuidPending;
            }
        }
        var refusedFiles = found.Count(RefusedStatus);
        if (request.Strict && (refusedFiles > 0 || found.RefusedChanges > 0))
            return Refused(RefusalCodes.RecordImportStrictRefused, FailureReason.Refused,
                "Strict mode: the folder has files or changes that were refused, so no Draft was made. Run it without a Draft name to list them.",
                ("refusedFiles", refusedFiles.ToString()), ("refusedChanges", found.RefusedChanges.ToString()));
        var lowering = found.Lowering;
        if (lowering.Operations.Count == 0)
        {
            if (firstPendingCode is not null && pendingRefused == found.Files.Count(file => file.Status == RefusedStatus && file.Code == firstPendingCode))
                return Refused(RefusalCodes.RecordImportGuidPending, FailureReason.Refused,
                    pendingRefused + " new record(s) of the folder are already created by an open Draft or Proposal. Apply or discard it first, or use other GUIDs.",
                    ("refusedFiles", refusedFiles.ToString()), ("pendingFiles", pendingRefused.ToString()));
            return Refused(RefusalCodes.RecordImportNothingToStage, FailureReason.Refused,
                "The folder lowers to no operation, so no Draft was made.",
                ("files", found.Files.Count.ToString()), ("refusedFiles", refusedFiles.ToString()),
                ("unchanged", found.Count(UnchangedStatus).ToString()));
        }

        var created = ProposalCommands.New(new(request.ProjectPath, MotifProductVersion.CurrentText, draftName, "Record import"));
        if (!created.Succeeded) return CommandOutcome<RecordImportResponse>.Refused(created.Refusal!);
        BeforeStage?.Invoke();
        var summary = ManifestJson(request, found);
        var authored = JsonSerializer.SerializeToElement(new JsonObject { ["recordImport"] = JsonNode.Parse(summary.GetRawText()) });
        var outcome = ProposalCommands.StageRecordImport(request.ProjectPath, draftName, authored, cache =>
        {
            if (PlanBatch(cache, inputs, validate: false).Digest != found.Digest)
                throw new RecordImportChangedException();
            return lowering;
        }, _ => JsonSerializer.SerializeToElement(new JsonObject
        {
            ["recordImport"] = JsonNode.Parse(summary.GetRawText()),
            ["refusedChanges"] = found.RefusedChanges + found.Count(RefusedStatus),
        }));
        if (!outcome.Succeeded)
        {
            var discarded = ProposalCommands.DiscardNamedDraft(
                new(request.ProjectPath, MotifProductVersion.CurrentText, draftName, null));
            if (discarded.Succeeded) return CommandOutcome<RecordImportResponse>.Refused(outcome.Refusal!);
            var facts = new Dictionary<string, string>(outcome.Refusal!.Facts)
            {
                ["draftNotDiscarded"] = draftName,
                ["discardRefusal"] = discarded.Refusal!.Code,
            };
            return CommandOutcome<RecordImportResponse>.Refused(new Refusal(outcome.Refusal.Code, outcome.Refusal.Reason,
                outcome.Refusal.Message + " The empty Draft '" + draftName + "' could not be discarded; discard it with discard-draft.",
                facts));
        }
        return CommandOutcome<RecordImportResponse>.Success(Summarize(request, found, ignored, DraftMode,
            created.Value!.DraftName, created.Value.ProposalId));
    }

    /// <summary>What a Proposal keeps of the folder: counts, manifest digest and staged file texts.</summary>
    private static JsonElement ManifestJson(RecordImportRequest request, BatchPlan plan)
    {
        var entries = new JsonArray();
        foreach (var file in plan.Files)
        {
            var entry = new JsonObject
            {
                ["path"] = file.Input.Path, ["sha256"] = file.Input.Sha, ["status"] = file.Status,
            };
            if (file.Kind is not null) entry["kind"] = file.Kind;
            if (file.Record is { } record) entry["record"] = CanonicalId.FromGuid(record).Value;
            if (file.Base is not null) entry["base"] = file.Base;
            if (file.Code is not null) entry["code"] = file.Code;
            if (file.DependsOn.Count > 0) entry["dependsOn"] = new JsonArray(file.DependsOn.Select(path => (JsonNode?)JsonValue.Create(path)).ToArray());
            if (file.Status is CreatedStatus or UpdatedStatus)
            {
                entry["text"] = file.Input.Text;
                entry["operations"] = file.Operations;
            }
            entries.Add(entry);
        }
        var node = new JsonObject
        {
            ["files"] = plan.Files.Count, ["created"] = plan.Count(CreatedStatus), ["updated"] = plan.Count(UpdatedStatus),
            ["unchanged"] = plan.Count(UnchangedStatus), ["refused"] = plan.Count(RefusedStatus),
            ["manifest"] = Manifest(plan), ["entries"] = entries,
        };
        if (request.Source is not null) node["source"] = request.Source;
        if (request.Revision is not null) node["revision"] = request.Revision;
        if (request.Licence is not null) node["licence"] = request.Licence;
        return JsonSerializer.SerializeToElement(node);
    }

    private static string Manifest(BatchPlan plan)
    {
        var text = new StringBuilder();
        foreach (var file in plan.Files)
            text.Append(file.Input.Path).Append('\t').Append(file.Input.Sha).Append('\t').Append(file.Base).Append('\t')
                .Append(file.Record?.ToString("D")).Append('\t').Append(file.Status).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }

    // ------------------------------------------------------------------ planning

    private static BatchPlan PlanBatch(LcmCache cache, IReadOnlyList<DirInput> inputs, bool validate,
        IReadOnlyDictionary<string, string>? excluded = null)
    {
        var schemas = new SchemaSet(cache);
        var files = new List<DirFile>(inputs.Count);
        var digest = new StringBuilder();
        var operations = 0;
        foreach (var input in inputs)
        {
            var file = new DirFile(input);
            files.Add(file);
            digest.Append(input.Path).Append('|').Append(input.Sha).Append('|');
            if (input.Text is null)
            {
                file.Refuse(RefusalCodes.RecordImportFileUnreadable, "The record file could not be read: " + input.ReadError + ".");
                digest.Append("unreadable\n");
                continue;
            }
            file.Planned = Plan(cache, input.Text, validate: false, schemas);
            file.Lowering = file.Planned?.Lowering;
            digest.Append(file.Planned?.Digest).Append('\n');
            operations += file.Planned?.Lowering.Operations.Count ?? 0;
            if (operations > RecordImportPaging.MaxOperations)
                throw new RecordImportBatchTooLargeException(inputs.Count, operations);
        }
        var result = new BatchPlan(files, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(digest.ToString()))));
        if (!validate) return result;

        foreach (var file in files.Where(file => file.Input.Text is not null))
        {
            if (excluded is not null && excluded.TryGetValue(file.Input.Path, out var why)) file.Refuse(RefusalCodes.RecordImportPlanRefused, why);
            else Classify(file);
        }
        RefuseRecordTwice(files);
        RefuseDuplicateGuids(files);
        RefuseReferencesToNewRecords(files);
        var units = result.Staged.Where(file => file.Lowering!.Operations.Count > 0).ToList();
        RecordLowering[] validated;
        try { validated = ValidateOnScratch(cache, units.Select(file => (file.Planned!.Plan, file.Lowering!)).ToList()); }
        catch (RecordImportScratchException exception) when (exception.Unit >= 0)
        {
            throw new RecordImportScratchFileException(units[exception.Unit].Input.Path, exception);
        }
        for (var index = 0; index < units.Count; index++)
        {
            var file = units[index];
            file.Lowering = validated[index];
            if (validated[index].WholeFile is { } shape)
            {
                file.Refuse(shape.Code, shape.Reason);
                continue;
            }
            file.RefusedChanges = Refusals(file.Planned!, file.Lowering).Count;
            if (file.Lowering.Operations.Count == 0 && file.RefusedChanges > 0) RefuseWithFirst(file);
        }
        RefuseReferencesToNewRecords(files);
        RecordOperationEdges(files);
        return result;
    }

    /// <summary>Notes each file whose operations wait on another file's operations.</summary>
    private static void RecordOperationEdges(IReadOnlyList<DirFile> files)
    {
        var owner = new Dictionary<CanonicalId, DirFile>();
        foreach (var file in files.Where(file => file.Status is CreatedStatus or UpdatedStatus && file.Lowering is not null))
            foreach (var operation in file.Lowering!.Operations) owner[operation.OperationId] = file;
        foreach (var file in files.Where(file => file.Status is CreatedStatus or UpdatedStatus && file.Lowering is not null))
            foreach (var dependency in file.Lowering!.Operations.SelectMany(operation => operation.DependsOn))
                if (owner.TryGetValue(dependency.OperationId, out var other) && other != file) file.DependsOn.Add(other.Input.Path);
    }

    private static void Classify(DirFile file)
    {
        var planned = file.Planned;
        if (planned is null)
        {
            file.Refuse(RefusalCodes.RecordImportPlanRefused, "The file names no known record kind in its record: line, so it was not planned.");
            return;
        }
        var plan = planned.Plan;
        file.Kind = plan.Kind;
        file.Record = plan.RecordGuid;
        file.Base = plan.Export?.Base;
        file.Verdict = plan.BaseVerdict == ProjectRecordBaseVerdict.NotChecked ? "not-checked" : plan.BaseVerdict.ToString().ToLowerInvariant();
        file.Changes = plan.ChangeSet.Changes.Count;
        if (plan.ChangeSet.IsRefusedWhole)
        {
            var refusal = WholeFileRefusal(planned);
            file.Refuse(refusal.Code, refusal.Message);
            return;
        }
        if (planned.Lowering.WholeFile is { } shape)
        {
            var refusal = WholeCreateRefusal(planned, shape);
            file.Refuse(refusal.Code, refusal.Message);
            return;
        }
        var operations = planned.Lowering.Operations.Count;
        var refused = Refusals(planned, planned.Lowering);
        file.RefusedChanges = refused.Count;
        if (plan.BaseVerdict == ProjectRecordBaseVerdict.Unchanged || operations == 0 && refused.Count == 0 &&
            plan.BaseVerdict != ProjectRecordBaseVerdict.New)
        {
            file.Status = UnchangedStatus;
            return;
        }
        if (operations == 0 && plan.BaseVerdict == ProjectRecordBaseVerdict.New)
        {
            file.Refuse(RefusalCodes.RecordImportCreateKindNotCreatable, CreateNotLowered);
            return;
        }
        file.Status = plan.BaseVerdict == ProjectRecordBaseVerdict.New ? CreatedStatus : UpdatedStatus;
        if (operations == 0) RefuseWithFirst(file);
        else if (refused.Count > 0)
        {
            file.Code = refused[0].Code;
            file.Reason = Clip(refused[0].Reason, 300);
        }
    }

    private static void RefuseWithFirst(DirFile file)
    {
        var first = Refusals(file.Planned!, file.Lowering ?? file.Planned!.Lowering)[0];
        file.Refuse(first.Code, first.Reason);
    }

    private static string ListOf(IEnumerable<string> paths)
    {
        var list = paths.Take(4).ToList();
        return string.Join(", ", list.Take(3)) + (list.Count > 3 ? ", ..." : "");
    }

    private static void RefuseRecordTwice(IReadOnlyList<DirFile> files)
    {
        var groups = files.Where(file => file.Status is CreatedStatus or UpdatedStatus && file.Record is not null)
            .GroupBy(file => file.Record!.Value).Where(group => group.Count() > 1).ToList();
        foreach (var group in groups)
            foreach (var file in group)
                file.Refuse(new Refusal(RefusalCodes.RecordImportRecordTwice, FailureReason.Refused,
                    ListOf(group.Select(item => item.Input.Path)) + " all edit record " + group.Key.ToString("D") +
                    ". Put the edits in one file."));
    }

    private static void RefuseDuplicateGuids(IReadOnlyList<DirFile> files)
    {
        var owners = new Dictionary<Guid, List<DirFile>>();
        foreach (var file in files.Where(file => file.Status is CreatedStatus or UpdatedStatus))
            foreach (var guid in file.Lowering!.Operations.Where(operation => operation.EntityId is not null)
                .Select(operation => operation.EntityId!.Value.ToGuid()).Distinct())
            {
                if (!owners.TryGetValue(guid, out var list)) owners[guid] = list = [];
                list.Add(file);
            }
        foreach (var (guid, list) in owners.Where(pair => pair.Value.Count > 1).OrderBy(pair => pair.Key))
            foreach (var file in list.Where(file => file.Status is CreatedStatus or UpdatedStatus))
                file.Refuse(new Refusal(RefusalCodes.RecordImportGuidDuplicateInBatch, FailureReason.Refused,
                    "GUID " + guid.ToString("D") + " is used by more than one new object (" + ListOf(list.Select(item => item.Input.Path)) +
                    "). Every new object needs its own GUID."));
    }

    private static readonly Regex OwnedGuid = new("guid:\\s*\"([0-9a-fA-F-]{36})\"", RegexOptions.Compiled);
    private static readonly Regex LinkedGuid = new("/objects/([0-9a-fA-F-]{36})", RegexOptions.Compiled);

    private static void RefuseReferencesToNewRecords(IReadOnlyList<DirFile> files)
    {
        var creators = files.Where(file => file.Status == CreatedStatus && file.Input.Text is not null).ToList();
        if (creators.Count == 0) return;
        var owners = new Dictionary<string, DirFile>(StringComparer.Ordinal);
        var owned = new Dictionary<DirFile, HashSet<string>>();
        foreach (var creator in creators)
        {
            var set = OwnedGuid.Matches(creator.Input.Text!).Select(m => m.Groups[1].Value.ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
            owned[creator] = set;
            foreach (var guid in set) owners.TryAdd(guid, creator);
        }
        foreach (var file in files.Where(file => file.Planned is not null && file.Input.Text is not null))
        {
            var mine = owned.TryGetValue(file, out var set) ? set : [];
            var linked = LinkedGuid.Matches(file.Input.Text!).Select(m => m.Groups[1].Value.ToLowerInvariant()).Distinct();
            var hits = linked.Where(guid => !mine.Contains(guid) && owners.TryGetValue(guid, out var other) && other != file)
                .Select(guid => (Guid: guid, Creator: owners[guid])).ToList();
            foreach (var edge in hits) file.DependsOn.Add(edge.Creator.Input.Path);
            if (hits.Count == 0) continue;
            var hit = hits[0];
            var reason = "This file refers to " + hit.Guid + ", which " + hit.Creator.Input.Path +
                " creates in this folder. References between new records are not imported yet: apply " + hit.Creator.Input.Path + " first, then import this file.";
            var lowering = file.Lowering ?? file.Planned!.Lowering;
            if (file.Status == RefusedStatus || lowering.Operations.Count == 0)
            {
                file.Refuse(new Refusal(RefusalCodes.RecordImportReferenceToNewInBatch, FailureReason.Refused, reason));
                continue;
            }
            file.Code = RefusalCodes.RecordImportReferenceToNewInBatch;
            file.Reason = Clip(reason, 300);
        }
    }

    // ------------------------------------------------------------------ the folder

    private static CommandOutcome<DirListing> ListDir(string dir)
    {
        var files = new List<(string Relative, string Full)>();
        var ignored = 0;
        var total = 0;
        try
        {
            if (!Directory.Exists(dir)) return DirUnreadable("The folder does not exist.");
            var pending = new Stack<(string Full, string Relative)>();
            pending.Push((Path.GetFullPath(dir), ""));
            while (pending.Count > 0)
            {
                var (full, relative) = pending.Pop();
                foreach (var entry in new DirectoryInfo(full).EnumerateFileSystemInfos())
                {
                    if (entry.Name.StartsWith('.')) continue;
                    var isLink = entry.LinkTarget is not null || (entry.Attributes & FileAttributes.ReparsePoint) != 0;
                    var name = relative.Length == 0 ? entry.Name : relative + "/" + entry.Name;
                    if ((entry.Attributes & FileAttributes.Directory) != 0)
                    {
                        if (!isLink) pending.Push((entry.FullName, name));
                        continue;
                    }
                    if (isLink || !entry.Name.EndsWith(".fw.md", StringComparison.OrdinalIgnoreCase)) { ignored++; continue; }
                    if (++total <= RecordImportPaging.MaxFiles) files.Add((name, entry.FullName));
                }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return DirUnreadable(exception.GetType().Name);
        }
        if (total > RecordImportPaging.MaxFiles) return CommandOutcome<DirListing>.Refused(TooLarge(total, 0));
        if (files.Count == 0)
            return CommandOutcome<DirListing>.Refused(new Refusal(RefusalCodes.RecordImportDirEmpty, FailureReason.InvalidArgument,
                "The folder has no *.fw.md file.", new Dictionary<string, string> { ["ignored"] = ignored.ToString() }));
        files.Sort((left, right) => string.CompareOrdinal(left.Relative, right.Relative));
        return CommandOutcome<DirListing>.Success(new(files, ignored));
    }

    private static CommandOutcome<DirListing> DirUnreadable(string error) =>
        CommandOutcome<DirListing>.Refused(new Refusal(RefusalCodes.RecordImportDirUnreadable, FailureReason.InvalidArgument,
            "The folder could not be read: " + error + ".", new Dictionary<string, string>()));

    private static Refusal TooLarge(int files, int operations) => new(RefusalCodes.RecordImportBatchTooLarge, FailureReason.Refused,
        $"The folder has {files} files and {(operations > 0 ? operations.ToString() : "an unchecked number of")} operations. One Proposal takes at most " +
        $"{RecordImportPaging.MaxFiles} files and {RecordImportPaging.MaxOperations} operations; split the folder.",
        new Dictionary<string, string>
        {
            ["files"] = files.ToString(), ["operations"] = operations.ToString(),
            ["maxFiles"] = RecordImportPaging.MaxFiles.ToString(), ["maxOperations"] = RecordImportPaging.MaxOperations.ToString(),
        });

    private static DirInput ReadInput(string relative, string full)
    {
        try
        {
            var text = FileReaderOverride is { } replaced ? replaced(full) : File.ReadAllText(full);
            return new(relative, full, text, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant(), null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return new(relative, full, null, "", exception.GetType().Name);
        }
    }

    // ------------------------------------------------------------------ pending drafts and proposals

    private static readonly Regex IdToken = new("(?<![A-Za-z0-9_-])[A-Za-z0-9_-]{22}(?![A-Za-z0-9_-])", RegexOptions.CultureInvariant);

    /// <summary>For each record, the first open Draft or Proposal naming it, in one store pass.</summary>
    private static CommandOutcome<Dictionary<Guid, string>> ScanPendingMany(string projectPath, IReadOnlyList<Guid> guids)
    {
        var found = new Dictionary<Guid, string>();
        if (PendingScanOverride is { } replaced)
        {
            foreach (var guid in guids)
            {
                var scan = replaced(projectPath, guid);
                if (!scan.Succeeded) return CommandOutcome<Dictionary<Guid, string>>.Refused(scan.Refusal!);
                if (scan.Value!.Name is { } name) found[guid] = name;
            }
            return CommandOutcome<Dictionary<Guid, string>>.Success(found);
        }
        var wanted = guids.ToDictionary(guid => CanonicalId.FromGuid(guid).Value, guid => guid, StringComparer.Ordinal);
        return ProjectStoreCommand.ReadExisting(projectPath, CommandOutcome<Dictionary<Guid, string>>.Success(found), (database, _) =>
        {
            var repository = new SIL.Motif.Worker.Store.ProposalRepository(database);
            void Look(string? json, string name)
            {
                if (json is null) return;
                foreach (Match match in IdToken.Matches(json))
                    if (wanted.TryGetValue(match.Value, out var guid)) found.TryAdd(guid, name);
            }
            foreach (var record in repository.ListDrafts()) Look(record.ProposalJson, record.DraftName ?? record.ProposalId.Value);
            foreach (var record in repository.List(new(null, false)).Where(record => record.DraftName is null))
                Look(record.ProposalJson, record.ProposalId.Value);
            return CommandOutcome<Dictionary<Guid, string>>.Success(found);
        });
    }

    // ------------------------------------------------------------------ the response

    private static int RowPageCount(BatchPlan plan, bool refusedOnly)
    {
        var rows = refusedOnly ? plan.Count(RefusedStatus) : plan.Files.Count;
        return Math.Max(1, (rows + RecordImportPaging.RowsPerPage - 1) / RecordImportPaging.RowsPerPage);
    }

    private static RecordImportFileRow Row(DirFile file, int length) => new(Clip(file.Input.Path, Math.Max(length, 40))!, file.Kind,
        file.Record is { } record ? CanonicalId.FromGuid(record).Value : null, file.Status, file.Verdict,
        file.Changes, file.Operations, file.RefusedChanges, file.Code, Clip(file.Reason, length));

    private static RecordImportResponse Summarize(RecordImportRequest request, BatchPlan plan, int ignored, string mode,
        string? draftName, string? proposalId)
    {
        var refusedOnly = request.RefusedOnly == true;
        var pages = RowPageCount(plan, refusedOnly);
        var page = request.Rows == true ? request.Page : 1;
        var counted = plan.Staged.Sum(file => file.Operations);
        var refusals = plan.Count(RefusedStatus) + plan.RefusedChanges;
        var firstRefused = plan.Files.Where(file => file.Status == RefusedStatus).Take(RecordImportPaging.SummaryRefusedRows).ToList();
        var manifest = Manifest(plan);
        RecordImportResponse Build(IReadOnlyList<RecordImportFileRow> shown, int length, IReadOnlyList<RecordImportFileRow>? rows) =>
            new(mode, null, null, "not-checked", plan.Staged.Sum(file => file.Changes), counted, refusals, page, pages, [], [],
                draftName, proposalId, null,
                new RecordImportDirSummary(plan.Files.Count, plan.Count(CreatedStatus), plan.Count(UpdatedStatus),
                    plan.Count(UnchangedStatus), plan.Count(RefusedStatus), ignored, manifest, shown, request.Source, request.Revision,
                    request.Licence), rows);
        if (request.Rows == true)
        {
            var source = refusedOnly ? plan.Files.Where(file => file.Status == RefusedStatus) : plan.Files;
            var rows = source.Skip((page - 1) * RecordImportPaging.RowsPerPage).Take(RecordImportPaging.RowsPerPage)
                .Select(file => Row(file, 300)).ToList();
            return Build([], 0, rows);
        }
        // Rows and text are cut until the summary fits, so long names stay within the bound.
        for (var count = firstRefused.Count; count >= 0; count--)
            foreach (var length in new[] { RecordImportPaging.TextLength, 60, 40, 24 })
            {
                var response = Build(firstRefused.Take(count).Select(file => Row(file, length)).ToList(), length, null);
                if (ProjectionJson.ToUtf8Bytes(response).Length <= RecordImportPaging.SummaryBytes) return response;
            }
        return Build([], 0, null);
    }

    private static CommandOutcome<RecordImportResponse> Reported(BatchPlan plan, CommandOutcome<RecordImportResponse> outcome) =>
        CommandOutcome<RecordImportResponse>.Success(outcome.Value! with
        {
            ReportRows = plan.Files.Select(file => Row(file, 300)).ToList(),
        });
}
