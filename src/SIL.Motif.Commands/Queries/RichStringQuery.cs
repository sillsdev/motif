using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Baselines;
using SIL.Motif.Host.RichText;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reads rich-text String and MultiString fields from the current Baseline, never from the live project.</summary>
public static class RichStringQuery
{
    /// <summary>Every sense's Definition in each current writing system, with the styles and writing systems they use.</summary>
    public static CommandOutcome<SenseDefinitionsResponse> SenseDefinitions(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        return ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (baseline is null)
                return CommandOutcome<SenseDefinitionsResponse>.Refused(new Refusal(RefusalCodes.ChangeBaselineMissing, FailureReason.Refused,
                    "This project has no Baseline yet. Capture one to read its senses."));
            if (!File.Exists(baseline.FwDataPath))
                return CommandOutcome<SenseDefinitionsResponse>.Refused(new Refusal(RefusalCodes.ChangeBaselineMissing,
                    FailureReason.Refused, "The exact Baseline file is unavailable. Capture a new Baseline to read its senses."));
            using var reader = BaselineReadCache.Open(baseline.FwDataPath);
            return CommandOutcome<SenseDefinitionsResponse>.Success(SenseDefinitionReader.Read(reader.Cache) with { Baseline = baseline.Token });
        });
    }
}
