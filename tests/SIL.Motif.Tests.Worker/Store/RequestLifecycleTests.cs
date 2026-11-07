using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Projects;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Store;

public sealed class RequestLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-requests-" + Guid.NewGuid().ToString("N"));
    private readonly MotifDatabase _database;
    private readonly ProposalRepository _proposals;
    private readonly RequestRepository _requests;

    public RequestLifecycleTests()
    {
        Directory.CreateDirectory(_root);
        _database = MotifDatabase.OpenOwned(Path.Combine(_root, "p.motif.db"),
            new ProjectLocator(Path.Combine(_root, "p.fwdata"), "p"), MotifSchema.CurrentSchema, new Version(1, 0));
        _proposals = new ProposalRepository(_database);
        _requests = new RequestRepository(_database);
    }

    // A Request with one linked Proposal that has been finalized and so answers it.
    private (CanonicalId Proposal, string Request) Answered()
    {
        var request = _requests.Create("word", "kita", "kita", "Please check the stem.").RequestId;
        var proposal = CanonicalId.Mint("proposal/");
        _proposals.CreateDraft("working", proposal, "{}");
        Assert.Equal(RequestWriteOutcome.Written, _requests.Link(proposal, request));
        Assert.Equal("open", _requests.Get(request)!.Status);

        _proposals.Finalize("working", "sha256:one", "{\"proposalId\":\"" + proposal.Value + "\"}", "label");

        Assert.Equal("answered", _requests.Get(request)!.Status);
        return (proposal, request);
    }

    [Fact]
    public void StartingADraftLeavesTheRequestOpenAndFinalizingAnswersIt() => Answered();

    [Theory]
    [InlineData("withdrawn", "open")]
    [InlineData("superseded", "open")]
    [InlineData("rejected", "answered")]
    public void ADecisionBeforeApplyReopensTheRequestUnlessTheLinguistDeclined(string to, string requestStatus)
    {
        var (proposal, request) = Answered();

        _proposals.Transition(proposal, ["proposed"], to, supersededBy: to == "superseded" ? "other" : null);

        Assert.Equal(requestStatus, _requests.Get(request)!.Status);
    }

    [Fact]
    public void AtMostOneUndecidedProposalAnswersARequest()
    {
        var (_, request) = Answered();
        _requests.Withdraw(request);
        var open = _requests.Create("warning", "w1", "A warning", null).RequestId;
        var first = CanonicalId.Mint("proposal/");
        var second = CanonicalId.Mint("proposal/");
        _proposals.CreateDraft("a", first, "{}");
        _proposals.CreateDraft("b", second, "{}");

        Assert.Equal(RequestWriteOutcome.Written, _requests.Link(first, open));
        Assert.Equal(RequestWriteOutcome.ProposalOpen, _requests.Link(second, open));
        Assert.Equal(RequestWriteOutcome.NotOpen, _requests.Link(second, request));
    }

    [Fact]
    public void WithdrawnRequestsAreNotListedAndOpenOnesAreCounted()
    {
        var kept = _requests.Create("word", "a", "a", null).RequestId;
        var dropped = _requests.Create("word", "b", "b", null).RequestId;

        Assert.Equal(RequestWriteOutcome.Written, _requests.Withdraw(dropped));

        Assert.Equal(1, _requests.OpenCount());
        Assert.Equal([kept], _requests.List(null, 0, 10).Page.Select(item => item.RequestId));
    }

    public void Dispose()
    {
        _database.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
