using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reads Grammar Design Records from the current Baseline, never from the live project.</summary>
public static class GdrQuery
{
    /// <summary>Lists the records; with no Baseline the list is empty and says so.</summary>
    public static CommandOutcome<GdrListResponse> List(GdrListRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (request.ExpectedBaseline is { } expected && expected != baseline?.Token) return BaselineChanged<GdrListResponse>();
            if (baseline is null) return CommandOutcome<GdrListResponse>.Success(new(false, []));
            if (!File.Exists(baseline.FwDataPath)) return BaselineUnavailable<GdrListResponse>();
            using var reader = BaselineReadCache.Open(baseline.FwDataPath);
            try
            {
                return CommandOutcome<GdrListResponse>.Success(new(true, GdrReader.List(reader.Cache))
                {
                    Baseline = baseline.Token,
                    IsStale = IsStale(project, baseline),
                });
            }
            catch (GdrRefusalException refused)
            {
                return CommandOutcome<GdrListResponse>.Refused(refused.Refusal);
            }
        });
    }

    /// <summary>Reads one record's Discussion with the project's styles and writing systems and any others it uses.</summary>
    public static CommandOutcome<GdrShowResponse> Show(GdrShowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Id))
            return EmptyId<GdrShowResponse>();
        var id = request.Id.Trim();
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (request.ExpectedBaseline is { } expected && expected != baseline?.Token) return BaselineChanged<GdrShowResponse>();
            if (baseline is null) return NoBaseline<GdrShowResponse>();
            if (!File.Exists(baseline.FwDataPath)) return BaselineUnavailable<GdrShowResponse>();
            using var reader = BaselineReadCache.Open(baseline.FwDataPath);
            try
            {
                var shown = GdrReader.Show(reader.Cache, id, Path.GetFileNameWithoutExtension(request.ProjectPath));
                return shown is null
                    ? NotFound<GdrShowResponse>(id)
                    : CommandOutcome<GdrShowResponse>.Success(shown with { Baseline = baseline.Token, IsStale = IsStale(project, baseline) });
            }
            catch (GdrRefusalException refused)
            {
                return CommandOutcome<GdrShowResponse>.Refused(refused.Refusal);
            }
        });
    }

    /// <summary>
    /// A link to the object at a project path, found in the current Baseline and naming the project and the FieldWorks
    /// tool that opens the object (<see cref="GdrReader.LinkToPath"/>).
    /// </summary>
    public static CommandOutcome<GdrPathLink> LinkToPath(GdrLinkToPathRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (request.ExpectedBaseline is { } expected && expected != baseline?.Token) return BaselineChanged<GdrPathLink>();
            if (baseline is null) return NoBaseline<GdrPathLink>();
            if (!File.Exists(baseline.FwDataPath)) return BaselineUnavailable<GdrPathLink>();
            using var reader = BaselineReadCache.Open(baseline.FwDataPath);
            try
            {
                var link = GdrReader.LinkToPath(reader.Cache, Path.GetFileNameWithoutExtension(request.ProjectPath), request.Path);
                return CommandOutcome<GdrPathLink>.Success(link with { Baseline = baseline.Token, IsStale = IsStale(project, baseline) });
            }
            catch (GdrRefusalException refused)
            {
                return CommandOutcome<GdrPathLink>.Refused(refused.Refusal);
            }
        });
    }

    // Saved since the capture: the records shown may no longer be what FieldWorks holds.
    private static bool IsStale(ProjectLocator project, BaselineRecord baseline) =>
        File.Exists(project.FullFwDataPath) &&
        new DateTimeOffset(File.GetLastWriteTimeUtc(project.FullFwDataPath), TimeSpan.Zero) > baseline.SourceLastWriteUtc;

    private static CommandOutcome<T> BaselineChanged<T>() where T : class => CommandOutcome<T>.Refused(new Refusal(
        RefusalCodes.GdrBaselineChanged, FailureReason.Refused,
        "The Baseline changed before this record was read. Refresh and try again."));

    private static CommandOutcome<T> EmptyId<T>() where T : class => CommandOutcome<T>.Refused(new Refusal(
        RefusalCodes.GdrEmptyId, FailureReason.InvalidArgument, "Name a Grammar Design Record, for example GDR-1."));

    private static CommandOutcome<T> NotFound<T>(string id) where T : class => CommandOutcome<T>.Refused(new Refusal(
        RefusalCodes.GdrNotFound, FailureReason.NotFound, $"No Grammar Design Record is named '{id}'."));

    private static CommandOutcome<T> NoBaseline<T>() where T : class => CommandOutcome<T>.Refused(new Refusal(
        RefusalCodes.GdrNoBaseline, FailureReason.NotFound,
        "This project has no Baseline yet. Capture one to read its Grammar Design Records."));

    private static CommandOutcome<T> BaselineUnavailable<T>() where T : class => CommandOutcome<T>.Refused(new Refusal(
        RefusalCodes.GdrBaselineUnavailable, FailureReason.StoreInconsistent,
        "The exact Baseline file is unavailable. Capture a new Baseline to read its Grammar Design Records."));
}
