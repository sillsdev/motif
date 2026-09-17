using SIL.Motif.Host;
using System;
using System.IO;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;

namespace SIL.Motif.Commands.Queries;

/// <summary>The one field a current-Baseline read needs: which project to ask about.</summary>
public sealed record CurrentBaselineRequest(string ProjectPath);

/// <summary>
/// A project's recorded current Baseline and a live observation of whether FieldWorks holds it right now.
/// <see cref="Token"/> and <see cref="SourceLastWriteUtc"/> are both <c>null</c> when no Baseline has been
/// captured for this project yet; <see cref="FieldWorksHeldProject"/> is reported either way.
/// </summary>
public sealed record CurrentBaselineResponse(
    BaselineToken? Token, DateTimeOffset? SourceLastWriteUtc, bool FieldWorksHeldProject);

/// <summary>
/// Reads a project's recorded current Baseline and its live lock-file observation. Read-only and outside
/// the command catalog: it changes nothing and has no CLI verb. Never captures, copies, or otherwise
/// touches the project — only the paired store and the lock file's mere presence are read.
/// </summary>
public static class CurrentBaselineQuery
{
    public static CommandOutcome<CurrentBaselineResponse> Query(CurrentBaselineRequest request) =>
        ProjectStoreCommand.Run(request.ProjectPath, ResolveProductVersion(), (database, project) =>
        {
            var workspaceKey = ProjectWorkspaceKey.Compute(project);
            var baseline = new BaselineRepository(database).GetCurrent(workspaceKey);
            var held = File.Exists(project.FullFwDataPath + ".lock");
            return CommandOutcome<CurrentBaselineResponse>.Success(new CurrentBaselineResponse(
                baseline?.Token, baseline?.SourceLastWriteUtc, held));
        });

    private static string ResolveProductVersion() => MotifProductVersion.CurrentText;
}
