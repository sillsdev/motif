using System;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Runner.Operations;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using SIL.LCModel;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// The CLI's first grammar Layer-1 authoring surface, alongside <see cref="ComposeAuthorLexemeFormTests"/>:
/// <c>compose-author-feature-structure</c> resolves one authored intent against a live project into
/// <see cref="SIL.Motif.Runner.Composers.AuthorFeatureStructureComposer"/>'s one operation, and carries
/// the intent forward as non-hashed provenance.
/// </summary>
[Collection(TestFixtures.LcmCacheTestCollection.Name)]
public sealed class ComposeAuthorFeatureStructureTests
{
    private const string ProductVersion = "1.0";

    private readonly SeededProject _seed;
    private readonly string _fwDataPath;

    public ComposeAuthorFeatureStructureTests(PristineProjectFixture pristine)
    {
        _seed = pristine.Seed;
        using var scratch = pristine.NewScratch();
        _fwDataPath = scratch.ProjectId.Path;
    }

    private CommandOutcome<DraftCreatedResponse> NewDraft(
        string fwDataPath, string productVersion, string draftName, string? label) =>
        ProposalCommands.New(new NewDraftRequest(fwDataPath, productVersion, draftName, label));

    private CommandOutcome<ProposalFinalizedResponse> FinalizeDraft(
        string fwDataPath, string productVersion, string draftName) =>
        ProposalCommands.Finalize(new FinalizeRequest(fwDataPath, productVersion, draftName));

    private CommandOutcome<ComposedOperationsResponse> ComposeAuthorFeatureStructure(
        string fwDataPath, string productVersion, string draftName, string intentJson) =>
        ProposalCommands.ComposeAuthorFeatureStructure(
            new ComposeAuthorFeatureStructureRequest(fwDataPath, productVersion, draftName, intentJson));

    private CommandOutcome<ProposalDetailProjection> ShowProposal(
        string fwDataPath, string productVersion, string proposalId) =>
        ProposalCommands.Show(new ShowProposalRequest(fwDataPath, productVersion, proposalId));

    private string FirstMsaId()
    {
        using var cache = new SIL.Motif.Host.LcmUtils.FwDataProjectLoader().LoadCache(_fwDataPath);
        var msaGuid = cache.ServiceLocator.GetInstance<ILexSenseRepository>()
            .GetObject(_seed.FirstSenseId).MorphoSyntaxAnalysisRA.Guid;
        return CanonicalId.FromGuid(msaGuid).Value;
    }

    [Fact]
    public void ComposeAuthorFeatureStructure_AppendsTheResolvedOperation_NotOneTheAgentEnumerated()
    {
        var msaId = FirstMsaId();
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "d", null).Succeeded);

        var intentJson = JsonSerializer.Serialize(new { msa = msaId });
        var result = ComposeAuthorFeatureStructure(_fwDataPath, ProductVersion, "d", intentJson);

        Assert.True(result.Succeeded);
        Assert.Single(result.Value!.Operations);
        Assert.Equal(1, result.Value.OperationCount);
        DraftRationale.Author(
            _fwDataPath, "d", "Author a feature structure", "Represent the selected grammatical analysis on the target MSA.");

        var finalize = FinalizeDraft(_fwDataPath, ProductVersion, "d");
        Assert.True(finalize.Succeeded);
        var proposalId = finalize.Value!.ProposalId;

        var showJson = ShowProposal(_fwDataPath, ProductVersion, proposalId);
        Assert.True(showJson.Succeeded);
        Assert.Contains(showJson.Value!.Operations, operation =>
            operation.Kind == MoStemMsaMsFeaturesOperationKinds.CreateMsFeatures);
    }

    [Fact]
    public void ComposeAuthorFeatureStructure_RecordsTheIntentAsNonHashedProvenance_NeverInTheDigest()
    {
        var msaId = FirstMsaId();
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "d", null).Succeeded);
        var intentJson = JsonSerializer.Serialize(new { msa = msaId });
        Assert.True(ComposeAuthorFeatureStructure(_fwDataPath, ProductVersion, "d", intentJson).Succeeded);
        DraftRationale.Author(
            _fwDataPath, "d", "Author a feature structure", "Preserve the composer provenance in the finalized intent.");

        var finalize = FinalizeDraft(_fwDataPath, ProductVersion, "d");
        Assert.True(finalize.Succeeded);
        var digest = finalize.Value!.IntentDigest;
        var proposalId = finalize.Value!.ProposalId;

        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var record = new ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
        Assert.Equal(digest, record.IntentDigest);
        var objectJson = record.ProposalJson!;
        var envelope = ProposalJsonParser.Parse(objectJson);
        Assert.NotNull(envelope.Extensions);
        var provenance = Assert.Single(envelope.Extensions!.Value.GetProperty("composers").EnumerateArray());
        Assert.Equal("AuthorFeatureStructure", provenance.GetProperty("composer").GetString());
        var bareProposal = new Proposal(envelope.ContractVersions, envelope.ProposalId, envelope.Requires, envelope.Operations);
        Assert.Equal(digest, IntentDigest.Compute(bareProposal));
    }
}
