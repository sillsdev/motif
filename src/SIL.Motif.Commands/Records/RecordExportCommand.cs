using System.Text;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.TextEditing.Project;

namespace SIL.Motif.Commands.Records;

/// <summary>
/// The <c>record export</c> command: writes the saved project's records as the record files <c>import</c> reads, and
/// counts every value a file leaves out. It never changes the project. The CLI and the MCP tool both call this and return
/// its <see cref="RecordExportResponse"/>.
/// </summary>
public static class RecordExportCommand
{
    private const string DefaultKind = "lexicon/entry";
    private const int ReasonLength = 200;

    /// <summary>Runs the export described by <paramref name="request"/>.</summary>
    public static CommandOutcome<RecordExportResponse> Run(RecordExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Guid? record = null;
        if (request.RecordId is not null)
        {
            if (!Guid.TryParse(request.RecordId, out var parsed))
                return Refused(RefusalCodes.RecordExportInvalidRequest, FailureReason.InvalidArgument,
                    "The record id must be a GUID.");
            record = parsed;
        }
        if (record is not null == request.All)
            return Refused(RefusalCodes.RecordExportInvalidRequest, FailureReason.InvalidArgument,
                "Name one record with --record <guid>, or every record of the kind with --all, not both and not neither.");
        if (string.IsNullOrWhiteSpace(request.OutPath))
            return Refused(RefusalCodes.RecordExportInvalidRequest, FailureReason.InvalidArgument, "An output path is required.");

        var located = ProjectStoreCommand.LocateProject(request.ProjectPath);
        if (!located.Succeeded) return CommandOutcome<RecordExportResponse>.Refused(located.Refusal!);
        return ProjectStoreCommand.RunOperation(request.ProjectPath, () =>
            ProjectReadCache.ReadSaved(located.Value!, (_, cache) => Export(cache, request, record)));
    }

    private static CommandOutcome<RecordExportResponse> Export(LcmCache cache, RecordExportRequest request, Guid? record)
    {
        var kindName = request.Kind ?? DefaultKind;
        ProjectRecordSchemaResult? schema = null;
        foreach (var overlay in RecordImportCommand.Overlays)
        {
            var candidate = ProjectRecordSchema.Generate(cache, overlay());
            var found = request.Kind is not null || record is null
                ? candidate.Schema.Kind(kindName)
                : KindOf(cache, candidate, record.Value);
            if (found is null) continue;
            schema = candidate;
            kindName = found.Name;
            break;
        }
        if (schema is null)
            return Refused(RefusalCodes.RecordExportNotFound, FailureReason.NotFound,
                record is null || request.Kind is not null ? "No record kind is named '" + kindName + "'." : "The project has no record of that id that a record kind covers.",
                ("kind", kindName));

        var exporter = new ProjectRecordExporter(cache, schema);
        IReadOnlyList<ProjectRecordExport> exports;
        try
        {
            exports = record is { } guid
                ? [exporter.Export(kindName, guid)]
                : exporter.ExportAll(kindName);
        }
        catch (ArgumentException exception)
        {
            return Refused(RefusalCodes.RecordExportNotFound, FailureReason.NotFound, Clip(exception.Message), ("kind", kindName));
        }
        catch (NotSupportedException exception)
        {
            return Refused(RefusalCodes.RecordExportInvalidRequest, FailureReason.InvalidArgument, Clip(exception.Message), ("kind", kindName));
        }

        var written = exports.Where(export => export.Text is not null).ToList();
        var targets = record is null
            ? written.Select(export => (Export: export, Path: Path.Combine(request.OutPath, export.Guid.ToString("D") + ".md"))).ToList()
            : written.Select(export => (Export: export, Path: request.OutPath)).ToList();
        if (targets.FirstOrDefault(target => File.Exists(target.Path)) is { Path: { } existing })
            return Refused(RefusalCodes.RecordExportOutputUnwritable, FailureReason.InvalidArgument,
                "A file is already at the output path, so nothing was written.", ("file", System.IO.Path.GetFileName(existing)));
        try
        {
            if (record is null) Directory.CreateDirectory(request.OutPath);
            else if (System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(request.OutPath)) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
            WriteAll(targets.Select(target => (target.Path, target.Export.Text!)).ToList());
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException)
        {
            return Refused(RefusalCodes.RecordExportOutputUnwritable, FailureReason.InvalidArgument,
                "The record file could not be written: " + exception.GetType().Name + ".",
                ("file", System.IO.Path.GetFileName(request.OutPath)));
        }

        var groups = exports.SelectMany(export => export.Unexported)
            .GroupBy(value => (value.Class, value.Field, Reason: Clip(value.Reason)))
            .OrderBy(group => group.Key.Class, StringComparer.Ordinal).ThenBy(group => group.Key.Field, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Reason, StringComparer.Ordinal)
            .Select(group => new RecordExportUnexported(group.Key.Class, group.Key.Field, group.Key.Reason, group.Count()))
            .ToList();
        return CommandOutcome<RecordExportResponse>.Success(new(kindName, exports.Count, written.Count, exports.Count - written.Count,
            groups.Sum(group => group.Count), groups, request.OutPath,
            "Edit the file, then run import --file to plan it. If the project changes first, export again and redo the edits."));
    }

    /// <summary>Writes temporary files, then renames them all, so a failure leaves none.</summary>
    private static void WriteAll(IReadOnlyList<(string Path, string Text)> files)
    {
        var temps = new List<(string Temp, string Path)>();
        var moved = new List<string>();
        try
        {
            foreach (var (path, text) in files)
            {
                var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
                temps.Add((temp, path));
                File.WriteAllBytes(temp, new UTF8Encoding(false).GetBytes(text));
            }
            foreach (var (temp, path) in temps)
            {
                File.Move(temp, path, overwrite: false);
                moved.Add(path);
            }
        }
        catch
        {
            foreach (var (temp, _) in temps) File.Delete(temp);
            foreach (var path in moved) File.Delete(path);
            throw;
        }
    }

    /// <summary>The first schema kind rooted at the object's class, or null if none.</summary>
    private static SIL.TextEditing.Markdown.Records.RecordKind? KindOf(LcmCache cache, ProjectRecordSchemaResult schema, Guid record) =>
        cache.ServiceLocator.ObjectRepository.TryGetObject(record, out var obj)
            ? schema.Schema.Kinds.FirstOrDefault(kind => kind.Roots.Any(root => root.Name == obj.ClassName))
            : null;

    private static string Clip(string text)
    {
        var line = text.ReplaceLineEndings(" ");
        return line.Length <= ReasonLength ? line : line[..(ReasonLength - 1)] + "…";
    }

    private static CommandOutcome<RecordExportResponse> Refused(string code, FailureReason reason, string message,
        params (string Name, string Value)[] facts) =>
        CommandOutcome<RecordExportResponse>.Refused(new Refusal(code, reason, message,
            facts.ToDictionary(fact => fact.Name, fact => fact.Value)));
}
