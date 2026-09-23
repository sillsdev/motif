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
/// The CLI's first Layer-1 authoring surface (ADR 0009 decision 1): <c>compose-author-lexeme-form</c>
/// resolves one authored intent against a live project into <see cref="AuthorLexemeFormComposer"/>'s
/// operations, appends them to a draft the agent never enumerated by hand, and carries the intent
/// forward as non-hashed provenance rather than dropping it or folding it into the digest.
/// </summary>
[Collection(TestFixtures.LcmCacheTestCollection.Name)]
public sealed class ComposeAuthorLexemeFormTests
{
    private const string ProductVersion = "1.0";

    private readonly SeededProject _seed;
    private readonly string _fwDataPath;

    public ComposeAuthorLexemeFormTests(PristineProjectFixture pristine)
    {
        _seed = pristine.Seed;
        using var scratch = pristine.NewScratch();
        _fwDataPath = scratch.ProjectId.Path;
    }

    private CommandOutcome<DraftCreatedResponse> NewDraft(
        string fwDataPath, string productVersion, string draftName, string? label) =>
        ProposalCommands.New(new NewDraftRequest(fwDataPath, productVersion, draftName, label));

    private CommandOutcome<SetGlossAddedResponse> AddSetGloss(
        string fwDataPath, string productVersion, string draftName, string target, string ws, string text) =>
        ProposalCommands.AddSetGloss(new AddSetGlossRequest(
            fwDataPath, productVersion, draftName, target, ws, text));

    private CommandOutcome<ProposalFinalizedResponse> FinalizeDraft(
        string fwDataPath, string productVersion, string draftName) =>
        ProposalCommands.Finalize(new FinalizeRequest(fwDataPath, productVersion, draftName));

    private CommandOutcome<ReopenedResponse> ReopenDraft(
        string fwDataPath, string productVersion, string draftName, string proposalId) =>
        ProposalCommands.Reopen(new ReopenRequest(fwDataPath, productVersion, draftName, proposalId));

    private CommandOutcome<ComposedOperationsResponse> ComposeAuthorLexemeForm(
        string fwDataPath, string productVersion, string draftName, string intentJson) =>
        ProposalCommands.ComposeAuthorLexemeForm(
            new ComposeAuthorLexemeFormRequest(fwDataPath, productVersion, draftName, intentJson));

    private CommandOutcome<ProposalDetailProjection> ShowProposal(
        string fwDataPath, string productVersion, string proposalId) =>
        ProposalCommands.Show(new ShowProposalRequest(fwDataPath, productVersion, proposalId));

    private string IntentJson(string entry, bool includeGloss) =>
        JsonSerializer.Serialize(includeGloss
            ? new
            {
                entry,
                morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value,
                ws = "fr",
                text = "zzComposedForm",
                sense = CanonicalId.FromGuid(_seed.FirstSenseId).Value,
                glossWs = "en",
                glossText = "a composed gloss",
            }
            : (object)new
            {
                entry,
                morphType = CanonicalId.FromGuid(MoMorphTypeTags.kguidMorphStem).Value,
                ws = "fr",
                text = "zzComposedForm",
            });

    [Fact]
    public void ComposeAuthorLexemeForm_AppendsTheResolvedOperations_NotOneTheAgentEnumerated()
    {
        var entryId = CanonicalId.FromGuid(_seed.FirstEntryId).Value;
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "d", null).Succeeded);

        var result = ComposeAuthorLexemeForm(
            _fwDataPath, ProductVersion, "d", IntentJson(entryId, includeGloss: true));

        Assert.True(result.Succeeded);
        Assert.Equal(2, result.Value!.Operations.Count);
        Assert.Equal(2, result.Value.OperationCount);
        DraftRationale.Author(
            _fwDataPath, "d", "Author a lexeme form", "Create the missing lexeme analysis and its attested gloss.");

        var finalize = FinalizeDraft(_fwDataPath, ProductVersion, "d");
        Assert.True(finalize.Succeeded);
        var proposalId = finalize.Value!.ProposalId;

        var showJson = ShowProposal(_fwDataPath, ProductVersion, proposalId);
        Assert.True(showJson.Succeeded);
        Assert.Contains(showJson.Value!.Operations, operation =>
            operation.Kind == LexEntryLexemeFormOperationKinds.CreateLexemeForm);
        Assert.Contains(showJson.Value.Operations, operation =>
            operation.Kind == LexicalSenseOperationKinds.SetGloss);
    }

    [Fact]
    public void ComposeAuthorLexemeForm_RecordsTheIntentAsNonHashedProvenance_NeverInTheDigest()
    {
        var entryId = CanonicalId.FromGuid(_seed.FirstEntryId).Value;
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "d", null).Succeeded);
        Assert.True(ComposeAuthorLexemeForm(_fwDataPath, ProductVersion, "d", IntentJson(entryId, includeGloss: false))
                .Succeeded);
        DraftRationale.Author(
            _fwDataPath, "d", "Author a lexeme form", "Preserve the composer provenance in the finalized intent.");

        var finalize = FinalizeDraft(_fwDataPath, ProductVersion, "d");
        Assert.True(finalize.Succeeded);
        var proposalId = finalize.Value!.ProposalId;
        var digest = finalize.Value!.IntentDigest;

        var objectJson = GetRevisionJson(proposalId, digest);
        var envelope = ProposalJsonParser.Parse(objectJson);
        Assert.NotNull(envelope.Extensions);
        var provenance = Assert.Single(envelope.Extensions!.Value.GetProperty("composers").EnumerateArray());
        Assert.Equal("AuthorLexemeForm", provenance.GetProperty("composer").GetString());

        // Must equal the same operations' digest with no extensions at all, proving provenance never entered it.
        var bareProposal = new Proposal(envelope.ContractVersions, envelope.ProposalId, envelope.Requires, envelope.Operations);
        Assert.Equal(digest, IntentDigest.Compute(bareProposal));
    }

    [Fact]
    public void Reopen_CarriesTheComposerProvenanceForward_RatherThanSilentlyDroppingIt()
    {
        var entryId = CanonicalId.FromGuid(_seed.FirstEntryId).Value;
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "d", null).Succeeded);
        Assert.True(ComposeAuthorLexemeForm(_fwDataPath, ProductVersion, "d", IntentJson(entryId, includeGloss: false))
                .Succeeded);
        DraftRationale.Author(
            _fwDataPath, "d", "Author a lexeme form", "Create the lexical analysis before adding the related manual edit.");
        var firstFinalize = FinalizeDraft(_fwDataPath, ProductVersion, "d");
        Assert.True(firstFinalize.Succeeded);
        var proposalId = firstFinalize.Value!.ProposalId;

        Assert.True(ReopenDraft(_fwDataPath, ProductVersion, "amend", proposalId).Succeeded);
        // Amend with an ordinary hand-authored operation too, so the draft mixes composed and manual content.
        var secondTarget = CanonicalId.FromGuid(_seed.SecondSenseId).Value;
        Assert.True(AddSetGloss(_fwDataPath, ProductVersion, "amend", secondTarget, "en", "manually added").Succeeded);
        var amendFinalize = FinalizeDraft(_fwDataPath, ProductVersion, "amend");
        Assert.True(amendFinalize.Succeeded);

        var amendedDigest = amendFinalize.Value!.IntentDigest;
        var objectJson = GetRevisionJson(proposalId, amendedDigest);
        var envelope = ProposalJsonParser.Parse(objectJson);
        Assert.NotNull(envelope.Extensions);
        var provenance = Assert.Single(envelope.Extensions!.Value.GetProperty("composers").EnumerateArray());
        Assert.Equal("AuthorLexemeForm", provenance.GetProperty("composer").GetString());
    }

    private string GetRevisionJson(string proposalId, string intentDigest)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var record = new ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
        Assert.Equal(intentDigest, record.IntentDigest);
        return record.ProposalJson!;
    }

}
