using SIL.Motif.Contract.Projects;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Apply;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands;

/// <summary>
/// Adds to a Proposal's detail the value the saved project holds now for each field a change touches, so a person
/// can read "old to new" before any Dry Run or Trial exists.
/// </summary>
internal static class ProposalCurrentValues
{
    /// <summary>Returns <paramref name="detail"/> with each operation's <c>Current</c>, or with the reason none could be read.</summary>
    internal static ProposalDetailProjection Attach(MotifDatabase database, ProjectLocator project,
        ProposalDetailProjection detail, SIL.Motif.Contract.Model.Proposal content)
    {
        try
        {
            var baseline = new BaselineRepository(database).GetCurrentPublished(ProjectWorkspaceKey.Compute(project));
            var values = ProjectReadCache.ReadCurrent(project, baseline,
                File.GetLastWriteTimeUtc(project.FullFwDataPath).Ticks,
                (cache, _) => FootprintProbe.ReadCurrentValues(cache, content));
            return detail with
            {
                Operations = detail.Operations.Select(operation => values.TryGetValue(operation.OperationId, out var effects)
                    ? operation with
                    {
                        Current = effects.Select(effect =>
                            new CurrentFieldValue(effect.CanonicalId.Value, effect.Field, effect.Before)).ToArray(),
                    }
                    : operation).ToArray(),
            };
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException
            or UnauthorizedAccessException or ProjectSavingException or ProjectBaselineBusyException
            or SIL.LCModel.LcmFileLockedException or SIL.Motif.Projection.ProjectWritingSystemsUnavailableException)
        {
            return detail with { CurrentUnavailable = "The saved project could not be read: " + ex.Message };
        }
    }
}
