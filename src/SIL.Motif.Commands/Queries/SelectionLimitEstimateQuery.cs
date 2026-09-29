using SIL.Motif.Contract.Commands;
using SIL.Motif.Host;
using SIL.Motif.Host.Assess;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>Reads the newest stored parser timing rate for the setup estimate.</summary>
public static class SelectionLimitEstimateQuery
{
    /// <summary>Returns the latest ParseTime rate, or the typical-machine fallback without usable statistics.</summary>
    public static CommandOutcome<ParserStepRate> ReadParserStepRate(string projectPath) =>
        ProjectStoreCommand.Run(projectPath, MotifProductVersion.CurrentText, (database, _) =>
        {
            var repository = new AssessmentRepository(database);
            var latest = repository.ListByKind(AssessmentKind.ParseTime.ToStoredKind()).LastOrDefault();
            return CommandOutcome<ParserStepRate>.Success(StepLimitEstimator.FromAssessment(
                latest is null ? null : repository.Get(latest.AssessmentId)));
        });
}
