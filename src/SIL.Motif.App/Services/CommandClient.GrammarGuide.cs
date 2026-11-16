using SIL.Motif.App.Preview;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    public Task<CommandOutcome<GdrListResponse>> ListGrammarGuideAsync(
        GdrListRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => GdrQuery.List(request), cancellationToken);

    public Task<CommandOutcome<GdrShowResponse>> ShowGrammarGuideAsync(
        GdrShowRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => GdrQuery.Show(request), cancellationToken);

    public Task<CommandOutcome<GdrPathLink>> LinkGrammarGuidePathAsync(
        GdrLinkToPathRequest request, CancellationToken cancellationToken) =>
        OneAtATime(() => GdrQuery.LinkToPath(request), cancellationToken);

    public IGdrEditStore OpenGrammarGuideEditStore(string projectPath) => new GdrEditStore(projectPath);
}
