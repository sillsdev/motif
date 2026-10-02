using System;
using System.Text.Json;
using SIL.Motif.Contract;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Commands.Store;
using SIL.Motif.Host;
using SIL.Motif.Worker.Baselines;
using SIL.Motif.Worker.Projects;
using SIL.Motif.Worker.Store;

namespace SIL.Motif.Commands.Queries;

/// <summary>
/// The grammar check stored for the current Baseline, or null when nobody has requested one.
/// </summary>
public sealed record StoredGrammarCheckResponse(GrammarCheckResponse? Check);

/// <summary>
/// Reads grammar findings from the Motif store without starting PanGloss, each with the Selection's words it
/// touches in the stored Parse all words.
/// </summary>
public static class StoredGrammarCheckQuery
{
    /// <summary>Reads the most recent check for the project's current Baseline.</summary>
    public static CommandOutcome<StoredGrammarCheckResponse> Query(GrammarCheckRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ProjectStoreCommand.Run(request.ProjectPath, MotifProductVersion.CurrentText, (database, project) =>
        {
            var baseline = new BaselineRepository(database).GetCurrent(ProjectWorkspaceKey.Compute(project));
            if (baseline is null)
                return CommandOutcome<StoredGrammarCheckResponse>.Success(
                    new(new GrammarCheckResponse([], HasBaseline: false)));
            var token = JsonSerializer.Serialize(baseline.Token, MotifJson.CreateOptions());
            var check = new GrammarCheckRepository(database).GetLatest(token);
            return CommandOutcome<StoredGrammarCheckResponse>.Success(
                new(check is null ? null : WarningWordsQuery.WithYourWords(database, project, check, baseline.Token)));
        });
    }
}
