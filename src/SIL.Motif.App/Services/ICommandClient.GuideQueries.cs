using SIL.Motif.App.GuideQueries;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.App.Services;

public partial interface ICommandClient
{
    /// <summary>Runs one read-only Guide query line as of the Baseline or a Proposal; the answer is the command's response, never stored.</summary>
    Task<CommandOutcome<FormsQueryResponse>> RunGuideQueryAsync(GuideQueryRunRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Runs one v2 corpus-forms line of a Guide block read-only and never builds a parse index; relative paths resolve against the
    /// project's folder. The answer is a table or a message, never stored.
    /// </summary>
    Task<GuideEvidenceResult> RunGuideEvidenceAsync(string projectPath, string line, CancellationToken cancellationToken);
}
