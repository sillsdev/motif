using SIL.Motif.App.Preview;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.Gdr;
using SIL.Motif.Runner.Operations;

namespace SIL.Motif.Tests.App;

public sealed partial class FakeCommandClient
{
    public Func<GdrListRequest, CancellationToken, Task<CommandOutcome<GdrListResponse>>>? GrammarGuideListHandler { get; set; }
    public Func<GdrShowRequest, CancellationToken, Task<CommandOutcome<GdrShowResponse>>>? GrammarGuideShowHandler { get; set; }
    public Func<GdrLinkToPathRequest, CancellationToken, Task<CommandOutcome<GdrPathLink>>>? GrammarGuideLinkHandler { get; set; }
    public List<GdrListRequest> GrammarGuideListRequests { get; } = [];
    public List<GdrShowRequest> GrammarGuideShowRequests { get; } = [];
    public List<GdrLinkToPathRequest> GrammarGuideLinkRequests { get; } = [];

    /// <summary>The store the Grammar Guide editor stages in; an in-memory one that holds each section's latest edit by default.</summary>
    public IGdrEditStore GrammarGuideEditStore { get; set; } = new FakeGdrEditStore();

    public Task<CommandOutcome<GdrListResponse>> ListGrammarGuideAsync(
        GdrListRequest request, CancellationToken cancellationToken)
    {
        GrammarGuideListRequests.Add(request);
        return GrammarGuideListHandler?.Invoke(request, cancellationToken) ?? Completed(new GdrListResponse(false, []));
    }

    public Task<CommandOutcome<GdrShowResponse>> ShowGrammarGuideAsync(
        GdrShowRequest request, CancellationToken cancellationToken)
    {
        GrammarGuideShowRequests.Add(request);
        return GrammarGuideShowHandler?.Invoke(request, cancellationToken) ?? Refused<GdrShowResponse>(
            new Refusal("guide.not-configured", FailureReason.NotFound, "No Grammar Guide section configured."));
    }

    public Task<CommandOutcome<GdrPathLink>> LinkGrammarGuidePathAsync(
        GdrLinkToPathRequest request, CancellationToken cancellationToken)
    {
        GrammarGuideLinkRequests.Add(request);
        return GrammarGuideLinkHandler?.Invoke(request, cancellationToken) ?? Refused<GdrPathLink>(
            new Refusal("guide.not-configured", FailureReason.NotFound, "No link configured."));
    }

    public IGdrEditStore OpenGrammarGuideEditStore(string projectPath) => GrammarGuideEditStore;
}

/// <summary>A Draft in memory: it keeps each section's latest staged edit, and Apply clears them all.</summary>
public sealed class FakeGdrEditStore : IGdrEditStore
{
    private readonly Dictionary<string, TextParagraphsPayload> _staged = new(StringComparer.Ordinal);

    /// <summary>What Apply did, in order.</summary>
    public int Applies { get; private set; }

    public Task<string?> StageAsync(string recordId, TextParagraphsPayload paragraphs)
    {
        _staged[recordId] = paragraphs;
        return Task.FromResult<string?>(null);
    }

    public Task<string?> DiscardAsync(string recordId)
    {
        _staged.Remove(recordId);
        return Task.FromResult<string?>(null);
    }

    public Task<GdrPendingRead> PendingAsync(string recordId) =>
        Task.FromResult(new GdrPendingRead(_staged.GetValueOrDefault(recordId)));

    public Task<GdrApplyRefusal?> ApplyAsync()
    {
        Applies++;
        _staged.Clear();
        return Task.FromResult<GdrApplyRefusal?>(null);
    }

    /// <summary>The edit staged for <paramref name="recordId"/>, or null.</summary>
    public TextParagraphsPayload? Staged(string recordId) => _staged.GetValueOrDefault(recordId);
}
