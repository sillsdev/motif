using SIL.Motif.Host.Baselines;
using System.Text.Json;
using SIL.LCModel;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>Reads and changes the project's saved default Selection.</summary>
public static class SelectionCommands
{
    /// <summary>Reads the default Selection saved in the paired project database.</summary>
    public static CommandOutcome<DefaultSelectionResponse> ReadDefault(ReadDefaultSelectionRequest request) =>
        ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            var saved = new NamedSelectionRepository(database).GetDefault();
            var setupSkipped = new ProjectSetupRepository(database).IsSkipped();
            return CommandOutcome<DefaultSelectionResponse>.Success(new DefaultSelectionResponse(
                saved is null ? null : Project(saved), setupSkipped));
        });

    /// <summary>Saves named inputs and makes that Selection the default for Assessments.</summary>
    public static CommandOutcome<DefaultSelectionResponse> SetDefault(SetDefaultSelectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.TextIds is null || request.AddedWords is null || string.IsNullOrWhiteSpace(request.Name))
            return CommandOutcome<DefaultSelectionResponse>.Refused(new Refusal(
                "selection.invalid", FailureReason.InvalidArgument,
                "A Selection name and non-null Text and word lists are required."));
        var invalidLimits = request.Limits?.ValidationError();
        if (request.Limits is null || invalidLimits is not null)
            return CommandOutcome<DefaultSelectionResponse>.Refused(new Refusal(
                "selection.invalid-limits", FailureReason.InvalidArgument,
                invalidLimits ?? "A valid Selection limit policy is required."));
        if (request.TextIds.Count == 0 && request.AddedWords.All(string.IsNullOrWhiteSpace))
            return CommandOutcome<DefaultSelectionResponse>.Refused(new Refusal(
                "selection.empty", FailureReason.InvalidArgument,
                "Choose at least one Text or add one word before saving the default Selection."));

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var baseline = new BaselineRepository(database).GetCurrent(workspaceKey);
            if (baseline is not null && request.TextIds.Count > 0)
            {
                using var reader = BaselineReadCache.Open(baseline.FwDataPath);
                var cache = reader.Cache;
                var repository = cache.ServiceLocator.GetInstance<SIL.LCModel.ITextRepository>();
                var missing = request.TextIds.Distinct().FirstOrDefault(id => !repository.TryGetObject(id, out _));
                if (missing != Guid.Empty)
                    return CommandOutcome<DefaultSelectionResponse>.Refused(new Refusal(
                        "selection.text-not-found", FailureReason.InvalidArgument,
                        $"No Text with GUID '{missing:D}' exists in the current Baseline."));
            }
            try
            {
                var saved = new NamedSelectionRepository(database).SetDefault(request.Name, request.TextIds,
                    request.AddedWords, request.Limits, request.ExpectedRevision);
                return CommandOutcome<DefaultSelectionResponse>.Success(new DefaultSelectionResponse(Project(saved)));
            }
            catch (SelectionRevisionConflictException exception)
            {
                return CommandOutcome<DefaultSelectionResponse>.Refused(new Refusal(
                    "selection.revision-conflict", FailureReason.Refused, exception.Message));
            }
        });
    }

    /// <summary>Changes only the current Default Selection's parsing limits at its expected revision.</summary>
    public static CommandOutcome<NamedSelectionProjection> SetLimits(SetSelectionLimitsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var limits = request.Limits;
        var error = limits?.ValidationError();
        if (string.IsNullOrWhiteSpace(request.SelectionName) ||
            string.IsNullOrWhiteSpace(request.ExpectedRevision) || limits is null || error is not null)
            return CommandOutcome<NamedSelectionProjection>.Refused(new Refusal(
                "selection.invalid-limits", FailureReason.InvalidArgument,
                error ?? "A Selection name, expected revision, and valid limits are required."));

        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            try
            {
                return CommandOutcome<NamedSelectionProjection>.Success(
                    Project(new NamedSelectionRepository(database).SetLimits(
                        request.SelectionName, request.ExpectedRevision!, limits)));
            }
            catch (SelectionRevisionConflictException exception)
            {
                return CommandOutcome<NamedSelectionProjection>.Refused(new Refusal(
                    "selection.revision-conflict", FailureReason.Refused, exception.Message));
            }
        });
    }

    private static NamedSelectionProjection Project(NamedSelectionRecord saved) => new(
        saved.Name, saved.TextIds, saved.AddedWords, saved.CreatedUtc, saved.UpdatedUtc,
        saved.Limits, saved.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
