using SIL.Motif.App.GuideQueries;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.App.Services;

public partial interface ICommandClient
{
    /// <summary>Runs one read-only Guide query line as of the Baseline or a Proposal; the answer is the command's response, never stored.</summary>
    Task<CommandOutcome<FormsQueryResponse>> RunGuideQueryAsync(GuideQueryRunRequest request, CancellationToken cancellationToken);
}
