using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class EvidenceCommandsTests : IDisposable
{
    private const string Version = "0.1.0";
    static EvidenceCommandsTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(WfiAnalysisOperationKinds).Module.ModuleHandle);

    private readonly string _fwDataPath = PlaceholderProject.Create("SIL.Motif.Tests.EvidenceCommands");

    public void Dispose()
    {
        var projectDir = Path.GetDirectoryName(_fwDataPath)!;
        if (Directory.Exists(projectDir)) Directory.Delete(projectDir, recursive: true);
    }

    private (CanonicalId Proposal, CanonicalId Subject) Stage(string subjectOperationTier, string? other = null,
        bool apply = true)
    {
        var subject = CanonicalId.Mint();
        var proposalId = CanonicalId.Mint();
        var evidence = subjectOperationTier == "none" ? "" :
            ",\"provenance\":[{\"type\":\"decisionEvidence\",\"tier\":\"" + subjectOperationTier +
            "\",\"sources\":[],\"detail\":{\"intendedForm\":\"sita\",\"editType\":\"vowel\"," +
            "\"violatedRule\":\"r\",\"standardForm\":\"s\"}}]";
        var json = $$"""
            {"contractVersions":{"lexical":"1.0"},"proposalId":"{{proposalId.Value}}","requires":[],
             "operations":[{"operationId":"{{CanonicalId.Mint().Value}}","kind":"{{LexicalSenseOperationKinds.SetGloss}}",
             "target":"{{subject.Value}}","after":{"ws":"en","text":"x"}{{evidence}}}]}
            """;
        var proposal = ProposalJsonParser.Parse(json);
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var repository = new ProposalRepository(database);
        repository.SaveRevision(new ProposalRevisionRecord(
            proposalId, IntentDigest.Compute(proposal), json, "proposed", null, null, null));
        if (apply) repository.SetStatus(proposalId, "applied", null);
        return (proposalId, subject);
    }

    [Fact]
    public void ShowReturnsTheRecordsForOneSubjectLabelledNotHuman()
    {
        var (proposal, subject) = Stage("misspelled-typo");
        Stage("ill-formed");

        var shown = ProposalCommands.EvidenceShow(new EvidenceShowRequest(_fwDataPath, Version, subject.Value));

        Assert.True(shown.Succeeded, shown.Refusal?.Message);
        var row = Assert.Single(shown.Value!.Records);
        Assert.Equal(proposal.Value, row.ProposalId);
        Assert.Equal("misspelled-typo", row.Tier);
        Assert.False(row.Human);
        Assert.Equal("source-validated", row.Label);
        Assert.Equal("sita", row.Detail["intendedForm"]);
    }

    [Fact]
    public void ShowIsEmptyForASubjectWithNoEvidence()
    {
        Stage("undecided");
        var shown = ProposalCommands.EvidenceShow(new EvidenceShowRequest(_fwDataPath, Version, CanonicalId.Mint().Value));
        Assert.True(shown.Succeeded, shown.Refusal?.Code + shown.Refusal?.Message);
        Assert.Empty(shown.Value!.Records);
    }

    [Fact]
    public void AProposalThatIsNotAppliedIsNotEvidence()
    {
        var (_, subject) = Stage("undecided", apply: false);
        Assert.Empty(ProposalCommands.EvidenceShow(new EvidenceShowRequest(_fwDataPath, Version, subject.Value)).Value!.Records);
    }

    [Fact]
    public void AnOperationWithNoEvidenceIsNotListed()
    {
        Stage("none");
        var listed = ProposalCommands.EvidenceList(new EvidenceListRequest(_fwDataPath, Version));
        Assert.Equal(0, listed.Value!.Total);
        Assert.Equal(1, listed.Value.AppliedProposals);
    }

    [Fact]
    public void ListCountsPerTierThenPagesAndFilters()
    {
        Stage("ill-formed");
        Stage("ill-formed");
        Stage("undecided");

        var all = ProposalCommands.EvidenceList(new EvidenceListRequest(_fwDataPath, Version, Limit: 2)).Value!;
        Assert.Equal(3, all.Total);
        Assert.Equal(2, all.TierCounts["ill-formed"]);
        Assert.Equal(1, all.TierCounts["undecided"]);
        Assert.Equal(2, all.Rows.Count);
        var second = ProposalCommands.EvidenceList(new EvidenceListRequest(_fwDataPath, Version, Offset: 2, Limit: 2)).Value!;
        Assert.Single(second.Rows);
        var filtered = ProposalCommands.EvidenceList(new EvidenceListRequest(_fwDataPath, Version, "undecided")).Value!;
        Assert.Equal(1, filtered.Total);
        Assert.Equal("undecided", Assert.Single(filtered.Rows).Tier);
    }

    [Theory]
    [InlineData("gold", 0, 10, "evidence.unknown-tier")]
    [InlineData(null, -1, 10, "evidence.invalid-page")]
    [InlineData(null, 0, 0, "evidence.invalid-page")]
    [InlineData(null, 0, 501, "evidence.invalid-page")]
    public void ListRefusesABadTierOrPage(string? tier, int offset, int limit, string code) =>
        Assert.Equal(code, ProposalCommands.EvidenceList(
            new EvidenceListRequest(_fwDataPath, Version, tier, offset, limit)).Refusal?.Code);
}
