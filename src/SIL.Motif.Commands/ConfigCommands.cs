using SIL.Motif.Contract.Responses;
using System.Linq;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Config;

namespace SIL.Motif.Commands;

/// <summary>The <c>config</c> verb: showing a project's resolved Assessment configuration.</summary>
/// <remarks>
/// <see cref="ProjectConfigurationReader"/> is the seam that turns <c>&lt;project&gt;.motif.toml</c>, or its
/// absence, into one resolved <see cref="ProjectConfiguration"/> with every default already applied; this
/// class only projects that result, the same way every other read verb projects its result.
/// </remarks>
public static class ConfigCommands
{
    /// <summary>The resolved project configuration.</summary>
    public static CommandOutcome<ProjectConfigurationProjection> Show(ShowConfigRequest request) =>
        ProjectStoreCommand.Run(request.FwDataPath, request.ProductVersion, (_, project) =>
        {
            try
            {
                var configuration = new ProjectConfigurationReader().Read(project);
                return CommandOutcome<ProjectConfigurationProjection>.Success(ToProjection(configuration));
            }
            catch (ProjectConfigurationException exception)
            {
                return CommandOutcome<ProjectConfigurationProjection>.Refused(new Refusal(
                    "config.invalid", FailureReason.Refused, exception.Message));
            }
        });

    private static ProjectConfigurationProjection ToProjection(ProjectConfiguration configuration) => new(
        configuration.GateOnRegression,
        configuration.PurgeOnApply,
        configuration.Scopes
            .Select(scope => new AssessmentScopeProjection(
                scope.Name, scope.Query, scope.Assessor, scope.Engine, scope.Collect,
                (long)scope.PerWordLimit.TotalMilliseconds))
            .ToArray());
}
