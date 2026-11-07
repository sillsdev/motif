using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Collection(LcmCacheParallelCollections.Group1)]
public sealed class RequestCommandsTests(PristineProjectFixture pristine)
{
    private string NewProject()
    {
        using var cache = pristine.NewScratch();
        var path = cache.ProjectId.Path;
        cache.Dispose();
        return path;
    }

    [Fact]
    public void ALongWarningIsCutToFitAndAReopenedProposalCannotDoubleUpOnItsRequest()
    {
        var path = NewProject();
        var long1 = RequestCommands.Create(new CreateRequestRequest(path, "1.0", "warning", new string('a', 900) + "1",
            new string('m', 600), "Look."));
        var long2 = RequestCommands.Create(new CreateRequestRequest(path, "1.0", "warning", new string('a', 900) + "2",
            "short", null));

        Assert.True(long1.Succeeded, long1.Refusal?.Message);
        Assert.Equal(RequestCommands.MaxLabelLength, long1.Value!.Request.TargetLabel.Length);
        Assert.EndsWith("\u2026", long1.Value.Request.TargetLabel);
        Assert.InRange(long1.Value.Request.TargetId.Length, 1, RequestCommands.MaxTargetIdLength);
        Assert.NotEqual(long1.Value.Request.TargetId, long2.Value!.Request.TargetId);
        Assert.Equal("The linguist wrote: \u201cit\\\u201ds\u201d", RequestCommands.QuoteNote("it\u201ds"));

        var requestId = long1.Value.Request.RequestId;
        var first = ProposalCommands.New(new NewDraftRequest(path, "1.0", "first", "First", requestId));
        Assert.True(ProposalCommands.AddSetGloss(new AddSetGlossRequest(path, "1.0", "first",
            SIL.Motif.Contract.Ids.CanonicalId.Mint().Value, "en", "First")).Succeeded);
        DraftRationale.Author(path, "first", "First", "Why this is right.");
        var finalized = ProposalCommands.Finalize(new FinalizeRequest(path, "1.0", "first"));
        Assert.True(finalized.Succeeded, finalized.Refusal?.Message);
        using (var database = ProjectMotifDatabase.Open(path))
        {
            var proposals = new ProposalRepository(database);
            var id = SIL.Motif.Contract.Ids.CanonicalId.Parse(first.Value!.ProposalId);
            proposals.Transition(id, ["proposed"], "withdrawn");
        }
        var second = ProposalCommands.New(new NewDraftRequest(path, "1.0", "second", "Second", requestId));
        Assert.True(second.Succeeded, second.Refusal?.Message);

        using (var database = ProjectMotifDatabase.Open(path))
            Assert.Throws<RequestProposalOpenException>(() => new ProposalRepository(database).ReopenAsDraft(
                SIL.Motif.Contract.Ids.CanonicalId.Parse(first.Value.ProposalId), "again", "{}"));
        var reopened = ProposalCommands.Reopen(new ReopenRequest(path, "1.0", "again", first.Value.ProposalId));

        Assert.Equal(RefusalCodes.RequestProposalOpen, reopened.Refusal?.Code);
    }

    [Fact]
    public void ADeclinedProposalOffersAskAgainOnceAndAnOpenOneCannotStartASecondProposal()
    {
        var path = NewProject();
        var created = RequestCommands.Create(new CreateRequestRequest(path, "1.0", "warning", "w1", "A warning", "Look."));
        var requestId = created.Value!.Request.RequestId;

        var started = ProposalCommands.New(new NewDraftRequest(path, "1.0", "first", "First", requestId));
        Assert.True(started.Succeeded, started.Refusal?.Message);
        var second = ProposalCommands.New(new NewDraftRequest(path, "1.0", "second", "Second", requestId));
        Assert.Equal(RefusalCodes.RequestProposalOpen, second.Refusal?.Code);
        var unknown = ProposalCommands.New(new NewDraftRequest(path, "1.0", "third", "Third", "request/missing"));
        Assert.Equal(RefusalCodes.RequestNotFound, unknown.Refusal?.Code);

        // A Draft is not a decided Proposal, so it has nothing to ask again.
        var early = RequestCommands.AskAgain(new AskAgainRequest(path, "1.0", started.Value!.ProposalId));
        Assert.Equal(RefusalCodes.RequestNoRequest, early.Refusal?.Code);

        using (var database = ProjectMotifDatabase.Open(path))
        {
            var proposals = new ProposalRepository(database);
            var id = SIL.Motif.Contract.Ids.CanonicalId.Parse(started.Value.ProposalId);
            proposals.Finalize("first", "sha256:one", "{\"proposalId\":\"" + id.Value + "\"}", "First");
            proposals.Transition(id, ["proposed"], "rejected");
        }

        var again = RequestCommands.AskAgain(new AskAgainRequest(path, "1.0", started.Value.ProposalId));
        var twice = RequestCommands.AskAgain(new AskAgainRequest(path, "1.0", started.Value.ProposalId));

        Assert.True(again.Succeeded, again.Refusal?.Message);
        Assert.Equal(again.Value!.Request.RequestId, twice.Value!.Request.RequestId);
        Assert.Equal(requestId, again.Value.Request.AskedAgainOf);
        Assert.Equal("Look.", again.Value.Request.Note);
        var open = RequestCommands.List(new ListRequestsRequest(path, "1.0", "open"));
        Assert.Equal([again.Value.Request.RequestId], open.Value!.Requests.Select(item => item.RequestId));
        Assert.Equal(1, open.Value.OpenCount);
    }

    [Fact]
    public void HandoffReadsEveryPageOfOpenRequests()
    {
        var path = NewProject();
        using var database = ProjectMotifDatabase.Open(path);
        var repository = new RequestRepository(database);
        for (var i = 0; i < 205; i++) repository.Create("word", $"word{i}", $"word{i}", null);
        var requests = HandoffCommand.ReadOpenRequests(repository);
        Assert.Equal(205, requests.Length);
        Assert.Equal(205, requests.Select(request => request.RequestId).Distinct().Count());
        var dir = Path.Combine(Path.GetTempPath(), "motif-requests-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            HandoffWriter.WriteRequestsJson(dir, requests);
            using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "requests.json")));
            Assert.Equal(205, json.RootElement.GetArrayLength());
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void HandoffWritesOpenRequestsOneRecordPerLineWithQuotedNotesAndNamesThemInTheGuide()
    {
        var dir = Path.Combine(Path.GetTempPath(), "motif-requests-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            HandoffWriter.WriteRequestsJson(dir, [
                new RequestItem("request/a", "word", "kita", "kita", "Check ”stem”.", "open", "2026-01-01T00:00:00Z", null, null),
                new RequestItem("request/b", "matrix-cell", "1/2", "Row, Column", null, "open", "2026-01-02T00:00:00Z", null, null)]);

            var lines = File.ReadAllLines(Path.Combine(dir, "requests.json")).Where(line => line.Trim().StartsWith('{')).ToArray();
            Assert.Equal(2, lines.Length);
            using var first = JsonDocument.Parse(lines[0].TrimEnd(','));
            Assert.Equal("request/a", first.RootElement.GetProperty("requestId").GetString());
            Assert.Equal("The linguist wrote: “Check \\”stem\\”.”", first.RootElement.GetProperty("note").GetString());
            var markdown = HandoffWriter.BuildHandoffMarkdown(true, "example-key", "mirusi", hasRequests: true);
            Assert.Contains("`requests.json`", markdown, StringComparison.Ordinal);
            Assert.Contains("Start there", markdown, StringComparison.Ordinal);
            Assert.True(markdown.Split('\n').Length <= 100);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
