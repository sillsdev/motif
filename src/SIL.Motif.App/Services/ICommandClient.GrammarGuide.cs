using SIL.Motif.App.Preview;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;

namespace SIL.Motif.App.Services;

public partial interface ICommandClient
{
    /// <summary>Lists the Grammar Design Records in the current Baseline.</summary>
    Task<CommandOutcome<GdrListResponse>> ListGrammarGuideAsync(
        GdrListRequest request, CancellationToken cancellationToken);

    /// <summary>Reads one Grammar Design Record and its discussion from the current Baseline.</summary>
    Task<CommandOutcome<GdrShowResponse>> ShowGrammarGuideAsync(
        GdrShowRequest request, CancellationToken cancellationToken);

    /// <summary>Finds the project object at a Markdown path, in the section's Baseline.</summary>
    Task<CommandOutcome<GdrPathLink>> LinkGrammarGuidePathAsync(
        GdrLinkToPathRequest request, CancellationToken cancellationToken);

    /// <summary>The store the editor stages edits in; the editor never writes the project.</summary>
    IGdrEditStore OpenGrammarGuideEditStore(string projectPath);
}
