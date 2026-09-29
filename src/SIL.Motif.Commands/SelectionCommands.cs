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
        if (request.PerWordLimitMs is <= 0)
            return CommandOutcome<DefaultSelectionResponse>.Refused(new Refusal(
                "selection.invalid-time-limit", FailureReason.InvalidArgument,
                "The per-word time limit must be a positive number of milliseconds."));
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
                using var cache = new FwDataProjectLoader().LoadScratchCache(baseline.FwDataPath);
                var repository = cache.ServiceLocator.GetInstance<SIL.LCModel.ITextRepository>();
                var missing = request.TextIds.Distinct().FirstOrDefault(id => !repository.TryGetObject(id, out _));
                if (missing != Guid.Empty)
                    return CommandOutcome<DefaultSelectionResponse>.Refused(new Refusal(
                        "selection.text-not-found", FailureReason.InvalidArgument,
                        $"No Text with GUID '{missing:D}' exists in the current Baseline."));
            }
            var saved = new NamedSelectionRepository(database).SetDefault(request.Name, request.TextIds,
                request.AddedWords, request.PerWordLimitMs, request.PerWordStepLimit);
            return CommandOutcome<DefaultSelectionResponse>.Success(new DefaultSelectionResponse(Project(saved)));
        });
    }

    private static NamedSelectionProjection Project(NamedSelectionRecord saved) => new(
        saved.Name, saved.TextIds, saved.AddedWords, saved.CreatedUtc, saved.UpdatedUtc,
        saved.PerWordLimitMs, saved.PerWordStepLimit);
}
