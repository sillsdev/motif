using SIL.Motif.Contract.Commands;
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
            if (baseline is null) return CommandOutcome<GdrListResponse>.Success(new(false, []));
            if (!File.Exists(baseline.FwDataPath)) return BaselineUnavailable<GdrListResponse>();
            using var reader = BaselineReadCache.Open(baseline.FwDataPath);
            try
            {
                return CommandOutcome<GdrListResponse>.Success(new(true, GdrReader.List(reader.Cache)) { Baseline = baseline.Token });
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
            if (baseline is null) return NoBaseline<GdrShowResponse>();
            if (!File.Exists(baseline.FwDataPath)) return BaselineUnavailable<GdrShowResponse>();
            using var reader = BaselineReadCache.Open(baseline.FwDataPath);
            try
            {
                var shown = GdrReader.Show(reader.Cache, id);
                return shown is null
                    ? NotFound<GdrShowResponse>(id)
                    : CommandOutcome<GdrShowResponse>.Success(shown with { Baseline = baseline.Token });
            }
            catch (GdrRefusalException refused)
            {
                return CommandOutcome<GdrShowResponse>.Refused(refused.Refusal);
            }
        });
    }

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
