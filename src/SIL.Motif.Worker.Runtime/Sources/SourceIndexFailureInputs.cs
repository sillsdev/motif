using System.Text.Json;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Host.Store;
using SIL.Motif.Scripture;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Worker.Sources;

internal sealed record SourceIndexFailureInputs(ProgressFileStamp[] ProjectFiles, int MetadataError, int? AccessError,
    bool CheckProjectAccess, SourceIndexFailureInputs.SourceBinding[] Sources)
{
    internal sealed record SourceBinding(string Id, SourceFolderInputs Inputs);

    /// <summary>Whether one writing-system file could be opened for reading; 0 means readable.</summary>
    internal sealed record InputAccess(string Name, int Error);

    // Restoring read access changes no length or time, so the read result is part of the observation.
    public InputAccess[] WritingSystemAccess { get; init; } = [];

    public static SourceIndexFailureInputs Observe(MotifDatabase database, bool checkProjectAccess)
    {
        var path = database.Project.FullFwDataPath;
        ProgressFileStamp[] projectFiles;
        var metadataError = 0;
        try { projectFiles = ProgressSourceIdentity.Metadata(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            projectFiles = [];
            metadataError = exception.HResult;
        }
        var access = checkProjectAccess && metadataError == 0 ? ObserveProjectAccess(path) : null;
        var workspace = ProjectWorkspaceKey.Compute(database.Project);
        var sources = ScriptureLocalStore.ForInstallation().Read(workspace).SourcePaths
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new SourceBinding(pair.Key, SourceIndexIdentity.ObserveFolder(pair.Value))).ToArray();
        var writingSystems = checkProjectAccess && metadataError == 0 ? ObserveWritingSystemAccess(projectFiles, path) : [];
        return new(projectFiles, metadataError, access, checkProjectAccess, sources) { WritingSystemAccess = writingSystems };
    }

    public static bool StillCurrent(string identityJson, MotifDatabase database)
    {
        SourceIndexFailureInputs? previous;
        try { previous = JsonSerializer.Deserialize<SourceIndexFailureInputs>(identityJson); }
        catch (JsonException) { return false; }
        if (previous is not { ProjectFiles: not null, Sources: not null }) return false;
        var current = Observe(database, previous.CheckProjectAccess);
        return previous.MetadataError == current.MetadataError && previous.ProjectFiles.SequenceEqual(current.ProjectFiles) &&
            (previous.AccessError is null || current.AccessError is null || previous.AccessError == current.AccessError) &&
            (previous.WritingSystemAccess ?? []).SequenceEqual(current.WritingSystemAccess) &&
            previous.Sources.Length == current.Sources.Length &&
            previous.Sources.Zip(current.Sources).All(pair =>
                pair.First.Id == pair.Second.Id && pair.First.Inputs.Matches(pair.Second.Inputs));
    }

    private static InputAccess[] ObserveWritingSystemAccess(ProgressFileStamp[] files, string projectPath)
    {
        var root = Path.GetDirectoryName(projectPath)!;
        return files.Where(file => file.Name != "project").Select(file =>
        {
            try
            {
                using var stream = new FileStream(Path.Combine(root, file.Name.Replace('/', Path.DirectorySeparatorChar)),
                    FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                _ = stream.ReadByte();
                return new InputAccess(file.Name, 0);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new InputAccess(file.Name, ProjectFileHeldException.IsSharingViolation(exception) ? 0 : exception.HResult);
            }
        }).ToArray();
    }

    private static int? ObserveProjectAccess(string path)
    {
        try
        {
            // The access probe shares the startup gate with LibLCM even though it reads only one byte.
            using var gate = ProjectStartupGate.Enter(path, sourceIndex: true, waitForAvailability: false);
            if (gate is null) return null;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            _ = stream.ReadByte();
            return 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return exception is ProjectStartupBusyException || ProjectFileHeldException.IsSharingViolation(exception)
                ? null : exception.HResult;
        }
    }
}
