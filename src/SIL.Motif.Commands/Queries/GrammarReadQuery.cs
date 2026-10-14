using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Projection.Grammar;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reads typed, authored project grammar objects from the current Baseline.</summary>
public static class GrammarReadQuery
{
    private const int MaximumOffset = 100_000;
    private const int MaximumPageSize = 100;

    /// <summary>Reads one kind of grammar object without opening or changing the live project.</summary>
    public static CommandOutcome<GrammarReadResponse> Query(GrammarReadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Kind))
            return Invalid("Choose a supported grammar object kind.");
        if (request.Id is not null && (string.IsNullOrWhiteSpace(request.Id) ||
                !CanonicalId.TryParse(request.Id, out _)))
            return Invalid("An exact id must be a canonical entity id.");
        if (request.Id is not null && request.Query is not null)
            return Invalid("Choose either an exact id or a name query, not both.");
        if (request.Offset is < 0 or > MaximumOffset)
            return Invalid($"The offset must be between 0 and {MaximumOffset}.");
        if (request.Limit is < 1 or > MaximumPageSize)
            return Invalid($"The limit must be between 1 and {MaximumPageSize}.");

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (baseline is null)
                return Refused("grammar-read.baseline-missing", FailureReason.NotFound,
                    "Capture a Baseline before reading project grammar objects.");
            if (!File.Exists(baseline.FwDataPath))
                return Refused("grammar-read.baseline-unavailable", FailureReason.StoreInconsistent,
                    "The current Baseline file is missing. Capture a new Baseline before reading grammar objects.");

            using var reader = BaselineReadCache.Open(baseline.FwDataPath);
            var response = GrammarReadProjectionBuilder.Build(reader.Cache, request);
            var projectSave = File.Exists(project.FullFwDataPath)
                ? new DateTimeOffset(File.GetLastWriteTimeUtc(project.FullFwDataPath), TimeSpan.Zero)
                : (DateTimeOffset?)null;
            return CommandOutcome<GrammarReadResponse>.Success(response with
            {
                Baseline = baseline.Token,
                SourceLastWriteUtc = baseline.SourceLastWriteUtc,
                ProjectLastWriteUtc = projectSave,
                IsStale = projectSave > baseline.SourceLastWriteUtc,
            });
        });
    }

    private static CommandOutcome<GrammarReadResponse> Invalid(string message) =>
        Refused("grammar-read.invalid-request", FailureReason.InvalidArgument, message);

    private static CommandOutcome<GrammarReadResponse> Refused(string code, FailureReason reason, string message) =>
        CommandOutcome<GrammarReadResponse>.Refused(new Refusal(code, reason, message));
}
