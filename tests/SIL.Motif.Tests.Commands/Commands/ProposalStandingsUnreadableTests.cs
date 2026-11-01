using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker.Jobs;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ProposalStandingsUnreadableTests : IDisposable
{
    private const string UnknownKind = "lexical/lexSense/nope";

    private static readonly JsonSerializerOptions DraftOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.ProposalStandingsUnreadableTests",
        Guid.NewGuid().ToString("N"));

    public ProposalStandingsUnreadableTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void AStoredProposalThatCannotBeParsedIsReportedAndTheReadableOnesAreListed()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var good = fixture.SaveProposal();
        var badId = CanonicalId.Mint("proposal/");
        var badJson = ProposalCommands.BuildProposalJson(GlossDraft(badId)).Replace("lexical/lexSense/setGloss",
            UnknownKind, StringComparison.Ordinal);
        var repository = new ProposalRepository(fixture.Database);
        repository.SaveRevision(new ProposalRevisionRecord(badId, "sha256:" + new string('f', 64), badJson,
            "proposed", "Broken", null, null));

        var standings = Read(fixture);

        Assert.Equal(good.Id.Value, Assert.Single(standings.Proposals).ProposalId);
        var unreadable = Assert.Single(standings.Unreadable);
        Assert.Equal(badId.Value, unreadable.ProposalId);
        Assert.Equal("Broken", unreadable.Label);
        Assert.Null(unreadable.DraftName);
        Assert.Contains(UnknownKind, unreadable.Reason, StringComparison.Ordinal);
        Assert.Equal(badJson, repository.List(new ProposalListFilter(IncludeArchived: true))
            .Single(record => record.ProposalId == badId).ProposalJson);
    }

    [Fact]
    public void ADraftThatIsNotJsonOrNotAProposalIsReportedByIdAndTheReadableOnesAreListed()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var good = fixture.SaveProposal();
        var repository = new ProposalRepository(fixture.Database);
        var garbageId = CanonicalId.Mint("proposal/");
        repository.CreateDraft("garbage", garbageId, "this is not json");
        var wrongKindId = CanonicalId.Mint("proposal/");
        var wrongKind = GlossDraft(wrongKindId);
        wrongKind.Operations[0].Kind = UnknownKind;
        repository.CreateDraft("wrong-kind", wrongKindId, JsonSerializer.Serialize(wrongKind, DraftOptions));

        var standings = Read(fixture);

        Assert.Equal(good.Id.Value, Assert.Single(standings.Proposals).ProposalId);
        Assert.Equal(new[] { garbageId.Value, wrongKindId.Value }.Order(StringComparer.Ordinal),
            standings.Unreadable.Select(item => item.ProposalId).Order(StringComparer.Ordinal));
        Assert.All(standings.Unreadable, item => Assert.False(string.IsNullOrWhiteSpace(item.Reason)));
        Assert.Equal(["garbage", "wrong-kind"], standings.Unreadable.Select(item => item.DraftName).Order(StringComparer.Ordinal));
        Assert.Equal("this is not json", repository.GetDraft("garbage").ProposalJson);
    }

    [Fact]
    public void ADraftThatBecameUnreadableAfterItsTrialIsReportedNotThrown()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var good = fixture.SaveProposal();
        var id = CanonicalId.Mint("proposal/");
        var repository = new ProposalRepository(fixture.Database);
        repository.CreateDraft("tried", id, JsonSerializer.Serialize(GlossDraft(id), DraftOptions));
        fixture.CreateJob(ProposalCommands.BuildProposalJson(GlossDraft(id)), "2026-09-24T11:00:00Z", "trial", []);
        repository.SaveDraft("tried", "{ broken");

        var standings = Read(fixture);

        Assert.Equal(good.Id.Value, Assert.Single(standings.Proposals).ProposalId);
        Assert.Equal(id.Value, Assert.Single(standings.Unreadable).ProposalId);
    }

    [Fact]
    public void ATrialWhoseStoredProposalCannotBeParsedDoesNotHideTheOtherProposals()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var good = fixture.SaveProposal();
        fixture.CreateJob(good.Json, "2026-09-24T11:00:00Z", "good-trial", []);
        var otherId = CanonicalId.Mint("proposal/");
        fixture.CreateRunningJob(ProposalCommands.BuildProposalJson(GlossDraft(otherId)).Replace(
            "lexical/lexSense/setGloss", UnknownKind, StringComparison.Ordinal), "2026-09-24T12:00:00Z", "bad-trial");

        var card = Assert.Single(Read(fixture).Proposals);

        Assert.Equal(good.Id.Value, card.ProposalId);
        Assert.Equal("good-trial", card.Trial!.JobId);
        Assert.Equal("good-trial", Trials.LatestFor(fixture.Database, fixture.Project, good.Id.Value)!.JobId);
    }

    [Fact]
    public void FindingAProposalsTrialJobsSkipsAJobWhoseStoredProposalCannotBeParsed()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var good = fixture.SaveProposal();
        fixture.CreateJob(good.Json, "2026-09-24T11:00:00Z", "good-trial", []);
        fixture.CreateRunningJob(ProposalCommands.BuildProposalJson(GlossDraft(CanonicalId.Mint("proposal/"))).Replace(
            "lexical/lexSense/setGloss", UnknownKind, StringComparison.Ordinal), "2026-09-24T12:00:00Z", "bad-trial");

        Assert.Equal(["good-trial"], TrialScratchRetention.JobsForProposal(fixture.Database, good.Id));
    }

    [Fact]
    public void AStoreFailureWhileLoadingProposalsIsNotMistakenForAnUnreadableProposal()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", []);
        using (var connection = fixture.Database.OpenConnection())
        {
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE ProposalRevisions SET ProposalJson = x'FFFE';";
            command.ExecuteNonQuery();
        }

        Assert.Throws<InvalidDataException>(() => Trials.LatestForAll(fixture.Database, fixture.Project));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static ProposalStandings Read(TrialsTests.ProjectFixture fixture)
    {
        var result = ProposalStandingsCommand.Read(fixture.Database, fixture.Project, includeSetAside: true);
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return result.Value!;
    }

    private static DraftDocument GlossDraft(CanonicalId proposalId)
    {
        using var after = JsonDocument.Parse("""{"ws":"en","text":"x"}""");
        return new DraftDocument
        {
            ProposalId = proposalId.Value,
            ContractVersions = new Dictionary<string, string>(StringComparer.Ordinal) { ["lexical"] = "1.0" },
            Operations =
            [
                new DraftOperation
                {
                    OperationId = CanonicalId.Mint("operation/").Value,
                    Kind = "lexical/lexSense/setGloss",
                    Target = CanonicalId.Mint("lexSense/").Value,
                    After = after.RootElement.EnumerateObject()
                        .ToDictionary(property => property.Name, property => property.Value.Clone(),
                            StringComparer.Ordinal),
                },
            ],
        };
    }
}
