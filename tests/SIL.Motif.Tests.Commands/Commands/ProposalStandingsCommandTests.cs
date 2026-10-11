using SIL.Motif.Contract.Jobs;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Catalog;
using SIL.Motif.Commands.Store;
using SIL.Motif.Contract.Assess;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ProposalStandingsCommandTests : IDisposable
{
    private static readonly JsonSerializerOptions DraftOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.ProposalStandingsTests",
        Guid.NewGuid().ToString("N"));

    public ProposalStandingsCommandTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void ListsThePendingDraftUnderNotAppliedYet()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var id = CanonicalId.Mint("proposal/");
        new ProposalRepository(fixture.Database).CreateDraft(PendingChanges.DraftName, id,
            JsonSerializer.Serialize(GlossDraft(id, "pending"), DraftOptions));

        var standings = Read(fixture);

        var card = Assert.Single(standings.Proposals);
        Assert.True(card.IsPendingDraft);
        Assert.Equal(ProposalGroup.NotAppliedYet, card.Group);
        Assert.Null(card.Trial);
    }

    [Fact]
    public void AProposalWithACurrentTrialWaitsForYou()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["trial-root"], allWords: true);

        var card = Assert.Single(Read(fixture).Proposals);

        Assert.Equal(ProposalGroup.WaitingForYou, card.Group);
        Assert.Equal(TrialFreshness.Current, card.Trial!.Freshness);
        Assert.Equal("trial", card.Trial.JobId);
    }

    [Fact]
    public void AProposalWhoseBaselineMovedNeedsAnotherTry()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime);
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["trial-root"]);
        fixture.RecordBaseline("sha256:" + new string('c', 64), "2026-09-24T13:00:00Z");

        var card = Assert.Single(Read(fixture).Proposals);

        Assert.Equal(ProposalGroup.NeedsAnotherTry, card.Group);
        Assert.Equal(TrialFreshness.BaselineMoved, card.Trial!.Freshness);
    }

    [Fact]
    public void ANeverTriedProposalIsInItsOwnGroupNotNeedsAnotherTry()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        fixture.SaveProposal();

        var card = Assert.Single(Read(fixture).Proposals);

        Assert.Equal(ProposalGroup.NotTried, card.Group);
        Assert.Equal(TrialStanding.NotTried, card.Standing);
        Assert.Null(card.Trial);
    }

    [Fact]
    public void EachCardCarriesTheStandingTheSharedRuleGivesItsTrial()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var current = fixture.SaveProposal();
        var failed = fixture.SaveProposal();
        fixture.RecordAssessment(current, "trial-root", AssessmentKinds.ParseTime);
        fixture.CreateJob(current.Json, "2026-09-24T11:00:00Z", "ok", ["trial-root"]);
        fixture.CreateTerminalJob(failed.Json, "2026-09-24T11:00:00Z", "bad", JobStatus.Failed,
            JobFailureCategory.Unknown);

        var cards = Read(fixture).Proposals.ToDictionary(card => card.ProposalId);

        Assert.Equal(TrialStanding.Current, cards[current.Id.Value].Standing);
        Assert.Equal(ProposalGroup.WaitingForYou, cards[current.Id.Value].Group);
        Assert.Equal(TrialStanding.Failed, cards[failed.Id.Value].Standing);
        Assert.Equal(ProposalGroup.NeedsAnotherTry, cards[failed.Id.Value].Group);
    }

    [Fact]
    public void NumbersPairTheProjectsAndTheTrials()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordProjectAssessment("project-root", AssessmentKinds.ParseTime, "no-analysis");
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime, "analysed");
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["trial-root"], allWords: true);

        var summary = Assert.Single(Read(fixture).Proposals).Trial!;

        Assert.True(summary.TextCoveragePercent.HasAfter);
        Assert.NotEqual(summary.TextCoveragePercent.Before, summary.TextCoveragePercent.After);
        Assert.True(summary.TextCoveragePercent.Moved);
        Assert.True(summary.ApprovedTotal.HasAfter);
    }

    [Fact]
    public void ATrialOverOnlyTheTouchedWordsPairsTheTotalsToo()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.RecordProjectAssessment("project-root", AssessmentKinds.ParseTime, "no-analysis");
        fixture.RecordAssessment(proposal, "trial-root", AssessmentKinds.ParseTime, "analysed");
        fixture.CreateJob(proposal.Json, "2026-09-24T11:00:00Z", "trial", ["trial-root"], allWords: false);

        var summary = Assert.Single(Read(fixture).Proposals).Trial!;

        Assert.Equal(TrialWordScope.ChosenWords, summary.WordScope);
        Assert.True(summary.TextCoveragePercent.HasAfter);
        Assert.True(summary.ApprovedKept.HasAfter);
    }

    [Fact]
    public void ADefaultSelectionTrialDoesNotMakeEveryProposalOverlap()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var first = fixture.SaveProposal();
        var second = fixture.SaveProposal();
        fixture.CreateDefaultSelectionJob(first.Json, "2026-09-24T11:00:00Z", "first", []);
        fixture.CreateDefaultSelectionJob(second.Json, "2026-09-24T11:05:00Z", "second", []);

        var cards = Read(fixture).Proposals;

        Assert.All(cards, card => Assert.Equal(TrialWordScope.DefaultSelection, card.Trial!.WordScope));
        Assert.All(cards, card => Assert.DoesNotContain(
            cards.Single(other => other.ProposalId != card.ProposalId).ProposalId, card.AlsoChangedBy));
    }

    [Fact]
    public void TwoProposalsChangingOneWordNameEachOther()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var first = fixture.SaveProposal();
        var second = fixture.SaveProposal();
        fixture.CreateJob(first.Json, "2026-09-24T11:00:00Z", "first", []);
        fixture.CreateJob(second.Json, "2026-09-24T11:05:00Z", "second", []);

        var cards = Read(fixture).Proposals;

        Assert.Equal(2, cards.Count);
        foreach (var card in cards)
        {
            var other = cards.Single(candidate => candidate.ProposalId != card.ProposalId);
            Assert.Equal([other.ProposalId], card.AlsoChangedBy);
        }
    }

    [Fact]
    public void SetAsideIsCountedNotListedByDefault()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        fixture.SaveProposal();
        var done = CanonicalId.Mint("proposal/");
        var json = ProposalCommands.BuildProposalJson(GlossDraft(done, "done"));
        new ProposalRepository(fixture.Database).SaveRevision(new ProposalRevisionRecord(done,
            IntentDigest.Compute(ProposalJsonParser.Parse(json)), json, "applied", "Done", null, null));

        var hidden = Read(fixture);
        var shown = Read(fixture, includeSetAside: true);

        Assert.Equal(1, hidden.SetAsideCount);
        Assert.DoesNotContain(hidden.Proposals, card => card.Group == ProposalGroup.SetAside);
        Assert.Contains(shown.Proposals, card => card.Group == ProposalGroup.SetAside && card.Label == "Done");
    }

    [Fact]
    public void ADeferredProposalIsStillOpenAndAClosedOneIsCountedAsClosed()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var deferred = fixture.SaveProposal();
        var withdrawn = fixture.SaveProposal();
        var repository = new ProposalRepository(fixture.Database);
        repository.SetStatus(deferred.Id, "deferred", null);
        repository.SetStatus(withdrawn.Id, "withdrawn", null);

        var standings = Read(fixture);

        var card = Assert.Single(standings.Proposals);
        Assert.Equal(deferred.Id.Value, card.ProposalId);
        Assert.Equal(ProposalGroup.NotTried, card.Group);
        Assert.False(card.IsDraft);
        Assert.Equal(1, standings.SetAsideCount);
    }

    [Fact]
    public void ChangeKindsCountRulesAllomorphsAndApprovedParses()
    {
        var id = CanonicalId.Mint("proposal/");
        var draft = GlossDraft(id, "x");
        draft.Operations.Clear();
        draft.ContractVersions = new Dictionary<string, string>(StringComparer.Ordinal)
            { ["grammar"] = "1.0", ["lexical"] = "1.0", ["analysis"] = "1.0" };
        draft.Operations.Add(Operation("grammar/phPhonData/createPhonRules"));
        draft.Operations.Add(Operation("grammar/phPhonData/createPhonRules"));
        draft.Operations.Add(Operation("lexical/lexEntry/createLexemeForm"));
        draft.Operations.Add(Operation(WfiAnalysisOperationKinds.AddRefEvaluations,
            new { member = HumanEvaluationPayload.Approves, note = HumanEvaluationPayload.Disapproves }));
        draft.Operations.Add(Operation("grammar/phTerminalUnit/setName"));

        var kinds = ProposalStandingsCommand.ChangeKinds(
            ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft)));

        Assert.Equal(["rule:2", "allomorph:1", "parse-approved:1", "change:1"],
            kinds.Select(kind => $"{kind.Kind}:{kind.Count}"));
        Assert.Equal(["2 rules", "1 allomorph", "1 parse approved", "1 change"], kinds.Select(kind => kind.Text));
    }

    [Fact]
    public void SeveralApprovedParsesReadInThePlural()
    {
        var id = CanonicalId.Mint("proposal/");
        var draft = GlossDraft(id, "x");
        draft.Operations.Clear();
        draft.ContractVersions = new Dictionary<string, string>(StringComparer.Ordinal) { ["analysis"] = "1.0" };
        for (var i = 0; i < 3; i++)
            draft.Operations.Add(Operation(WfiAnalysisOperationKinds.AddRefEvaluations,
                new { member = HumanEvaluationPayload.Approves }));

        var kinds = ProposalStandingsCommand.ChangeKinds(
            ProposalJsonParser.Parse(ProposalCommands.BuildProposalJson(draft)));

        Assert.Equal("3 parses approved", Assert.Single(kinds).Text);
    }

    [Fact]
    public void ARunningTrialIsMarkedSoNoSecondOneIsOffered()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        fixture.CreateRunningJob(proposal.Json, "2026-09-24T11:00:00Z", "running");

        var card = Assert.Single(Read(fixture).Proposals);

        Assert.Equal(ProposalGroup.NeedsAnotherTry, card.Group);
        Assert.Equal(TrialFreshness.Incomplete, card.Trial!.Freshness);
        Assert.Equal(TrialStanding.Trying, card.Standing);
    }

    [Fact]
    public void AChangedProposalNeedsAnotherTry()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var id = CanonicalId.Mint("proposal/");
        var repository = new ProposalRepository(fixture.Database);
        repository.CreateDraft(PendingChanges.DraftName, id, JsonSerializer.Serialize(GlossDraft(id, "before"), DraftOptions));
        fixture.CreateJob(ProposalCommands.BuildProposalJson(GlossDraft(id, "before")), "2026-09-24T11:00:00Z",
            "trial", []);
        repository.SaveDraft(PendingChanges.DraftName, JsonSerializer.Serialize(GlossDraft(id, "after"), DraftOptions));

        var card = Assert.Single(Read(fixture).Proposals);

        Assert.Equal(TrialFreshness.ProposalChanged, card.Trial!.Freshness);
        Assert.Equal(ProposalGroup.NotAppliedYet, card.Group);
    }

    [Fact]
    public void AReopenedProposalIsListedNotSetAside()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var proposal = fixture.SaveProposal();
        var path = fixture.Project.FullFwDataPath;
        var version = SIL.Motif.Host.MotifProductVersion.CurrentText;
        var reopened = ProposalCommands.Reopen(new SIL.Motif.Commands.Requests.ReopenRequest(path, version,
            "again", proposal.Id.Value));
        Assert.True(reopened.Succeeded, reopened.Refusal?.Message);

        var standings = Read(fixture, includeSetAside: true);

        var card = Assert.Single(standings.Proposals);
        Assert.NotEqual(ProposalGroup.SetAside, card.Group);
        Assert.Equal(0, standings.SetAsideCount);
    }

    [Fact]
    public void LatestForAllMatchesLatestForEachProposal()
    {
        using var fixture = new TrialsTests.ProjectFixture(_root);
        var first = fixture.SaveProposal();
        var second = fixture.SaveProposal();
        fixture.CreateJob(first.Json, "2026-09-24T11:00:00Z", "first-old", []);
        fixture.CreateJob(first.Json, "2026-09-24T12:00:00Z", "first-new", []);
        fixture.CreateJob(second.Json, "2026-09-24T11:30:00Z", "second", []);

        var all = Trials.LatestForAll(fixture.Database, fixture.Project);

        Assert.Equal(2, all.Count);
        Assert.Equal("first-new", all[first.Id.Value].JobId);
        Assert.Equal("second", all[second.Id.Value].JobId);
        Assert.Equal(Trials.LatestFor(fixture.Database, fixture.Project, first.Id.Value)!.JobId,
            all[first.Id.Value].JobId);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static ProposalStandings Read(TrialsTests.ProjectFixture fixture, bool includeSetAside = false)
    {
        var result = ProposalStandingsCommand.Read(fixture.Database, fixture.Project, includeSetAside);
        Assert.True(result.Succeeded, result.Refusal?.Message);
        return result.Value!;
    }

    private static DraftOperation Operation(string kind, object? after = null)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(after ?? new { }));
        return new DraftOperation
        {
            OperationId = CanonicalId.Mint("operation/").Value,
            Kind = kind,
            Target = CanonicalId.Mint("lexSense/").Value,
            After = document.RootElement.EnumerateObject()
                .ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal),
        };
    }

    private static DraftDocument GlossDraft(CanonicalId proposalId, string text) => new()
    {
        ProposalId = proposalId.Value,
        ContractVersions = new Dictionary<string, string>(StringComparer.Ordinal) { ["lexical"] = "1.0" },
        Operations = [Operation("lexical/lexSense/setGloss", new { ws = "en", text })],
    };
}
