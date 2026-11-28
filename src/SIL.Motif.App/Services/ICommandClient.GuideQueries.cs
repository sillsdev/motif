using SIL.Motif.App.GuideQueries;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

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

    /// <summary>Closes the project the Guide's evidence blocks keep open and deletes its private copy; the next block loads it again.</summary>
    void ReleaseGuideSession();

    /// <summary>
    /// Stages the saved table of one query block of a Guide section in the pending changes, from a live run (the same stage as
    /// <c>guide refresh-tables</c>); it is the only way the window writes a table.
    /// </summary>
    Task<CommandOutcome<GdrRefreshTablesResponse>> RefreshGuideTablesAsync(GdrRefreshTablesRequest request, CancellationToken cancellationToken);
}
