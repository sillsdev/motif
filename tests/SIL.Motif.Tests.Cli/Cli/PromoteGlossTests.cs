using System;
using System.IO;
using System.Text.Json;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Contract.Canonicalization;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Model;
using SIL.Motif.Contract.Parsing;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Store;
using Xunit;

namespace SIL.Motif.Tests.Cli;

/// <summary>
/// <c>promote-gloss</c> — the only sanctioned route from the Motif store into the language project
/// (ADR 0036 decision 2): a <c>lexical/lexSense/setGloss</c> operation whose value is
/// evidenced by a stored corpus, carrying that corpus's origin forward as non-hashed provenance so a
/// licence obligation (e.g. CC-BY-SA attribution) is never lost between the evidence and the entry it
/// justified.
/// </summary>
public sealed class PromoteGlossTests : IDisposable
{
    private const string ProductVersion = "1.0";

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "motif-promote-gloss-tests", Guid.NewGuid().ToString("N"));
    private readonly string _fwDataPath;

    public PromoteGlossTests()
    {
        Directory.CreateDirectory(_root);
        _fwDataPath = Path.Combine(_root, "Project.fwdata");
        File.WriteAllText(_fwDataPath, string.Empty);
    }

    private CommandOutcome<CorpusAddedResponse> AddCorpus(
        string fwDataPath, string productVersion, string corpusId, string description, string? uri,
        string? licence, LicenceCapabilities capabilities, string tokeniser, string tokeniserVersion,
        string? tokeniserNotes) =>
        CorpusCommands.AddCorpus(new AddCorpusRequest(
            fwDataPath, productVersion, corpusId, description, uri, licence, capabilities, tokeniser,
            tokeniserVersion, tokeniserNotes));

    private CommandOutcome<DraftCreatedResponse> NewDraft(
        string fwDataPath, string productVersion, string draftName, string? label) =>
        ProposalCommands.New(new NewDraftRequest(fwDataPath, productVersion, draftName, label));

    private CommandOutcome<PromoteGlossAddedResponse> PromoteGloss(
        string fwDataPath, string productVersion, string draftName, string target, string ws, string text,
        string corpusId, string? documentId = null) =>
        ProposalCommands.PromoteGloss(new PromoteGlossRequest(
            fwDataPath, productVersion, draftName, target, ws, text, corpusId, documentId));

    private CommandOutcome<ProposalFinalizedResponse> FinalizeDraft(
        string fwDataPath, string productVersion, string draftName) =>
        ProposalCommands.Finalize(new FinalizeRequest(fwDataPath, productVersion, draftName));

    private CommandOutcome<ProposalDetailProjection> ShowProposal(
        string fwDataPath, string productVersion, string proposalId) =>
        ProposalCommands.Show(new ShowProposalRequest(fwDataPath, productVersion, proposalId));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        GC.SuppressFinalize(this);
    }

    private void SeedCorpus(string corpusId = "wiki-testlang", string? licence = "CC-BY-SA-4.0") =>
        Assert.True(
            AddCorpus(
                _fwDataPath, ProductVersion, corpusId, "Testlang Wikipedia dump",
                uri: "https://example.invalid/dump", licence: licence, capabilities: LicenceCapabilities.Unknown(),
                tokeniser: "whitespace-and-punctuation", tokeniserVersion: "1", tokeniserNotes: null).Succeeded);

    [Fact]
    public void PromoteGloss_AddsTheOperation_AndRecordsTheCorpusOriginAsProvenance()
    {
        SeedCorpus();
        var target = CanonicalId.Mint().Value;
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "d", null).Succeeded);

        var result = PromoteGloss(
            _fwDataPath, ProductVersion, "d", target, "en", "a promoted gloss", "wiki-testlang");

        Assert.True(result.Succeeded);
        Assert.Equal("wiki-testlang", result.Value!.CorpusId);
        Assert.Equal("CC-BY-SA-4.0", result.Value.Licence);
        DraftRationale.Author(
            _fwDataPath, "d", "Promote a reviewed corpus gloss", "Use the attested corpus analysis in the language project.");

        var finalize = FinalizeDraft(_fwDataPath, ProductVersion, "d");
        Assert.True(finalize.Succeeded);
        var proposalId = finalize.Value!.ProposalId;
        var digest = finalize.Value!.IntentDigest;

        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        var record = new ProposalRepository(database).Get(CanonicalId.Parse(proposalId));
        Assert.Equal(digest, record.IntentDigest);
        var objectJson = record.ProposalJson!;
        var envelope = ProposalJsonParser.Parse(objectJson);
        Assert.NotNull(envelope.Extensions);
        var provenance = Assert.Single(envelope.Extensions!.Value.GetProperty("promotions").EnumerateArray());
        Assert.Equal("wiki-testlang", provenance.GetProperty("corpusId").GetString());
        Assert.Equal("CC-BY-SA-4.0", provenance.GetProperty("licence").GetString());

        // The digest must equal what the SAME operation hashes to with no extensions at all.
        var bareProposal = new Proposal(envelope.ContractVersions, envelope.ProposalId, envelope.Requires, envelope.Operations);
        Assert.Equal(digest, IntentDigest.Compute(bareProposal));
    }

    [Fact]
    public void PromoteGloss_SurfacesInShow_ForAReviewerWhoNeverOpensTheStoreFiles()
    {
        SeedCorpus();
        var target = CanonicalId.Mint().Value;
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "d", null).Succeeded);
        Assert.True(
            PromoteGloss(_fwDataPath, ProductVersion, "d", target, "en", "a promoted gloss", "wiki-testlang")
                .Succeeded);
        DraftRationale.Author(
            _fwDataPath, "d", "Promote an attested gloss", "Carry the corpus provenance into the finalized proposal.");
        var finalize = FinalizeDraft(_fwDataPath, ProductVersion, "d");
        Assert.True(finalize.Succeeded);
        var proposalId = finalize.Value!.ProposalId;

        var show = ShowProposal(_fwDataPath, ProductVersion, proposalId);
        Assert.True(show.Succeeded);
        using var extensions = JsonDocument.Parse(show.Value!.ExtensionsJson!);
        var promotion = Assert.Single(extensions.RootElement.GetProperty("promotions").EnumerateArray());
        Assert.Equal("wiki-testlang", promotion.GetProperty("corpusId").GetString());
        Assert.Contains("wiki-testlang", ProposalCommandRenderer.Render(show, asJson: true).Output);
    }

    [Fact]
    public void PromoteGloss_UnknownCorpus_Refuses_AndAddsNoOperation()
    {
        Assert.True(NewDraft(_fwDataPath, ProductVersion, "d", null).Succeeded);
        var before = ReadDraftJson("d");

        var result = PromoteGloss(
            _fwDataPath, ProductVersion, "d", CanonicalId.Mint().Value, "en", "text", "no-such-corpus");

        Assert.False(result.Succeeded);
        Assert.Equal("corpus.not-found", result.Refusal!.Code);
        Assert.Contains("not found", result.Refusal.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, ReadDraftJson("d"));
    }

    [Fact]
    public void PromoteGloss_UnknownDocumentWithinAKnownCorpus_Refuses()
    {
        SeedCorpus();

        Assert.True(NewDraft(_fwDataPath, ProductVersion, "d", null).Succeeded);
        var result = PromoteGloss(
            _fwDataPath, ProductVersion, "d", CanonicalId.Mint().Value, "en", "text", "wiki-testlang",
            "no-such-document");

        Assert.False(result.Succeeded);
        Assert.Equal("corpus.document-not-found", result.Refusal!.Code);
        Assert.Contains("no document", result.Refusal.Message, StringComparison.OrdinalIgnoreCase);
    }

    private string ReadDraftJson(string draftName)
    {
        using var database = ProjectMotifDatabase.Open(_fwDataPath);
        return new ProposalRepository(database).GetDraft(draftName).ProposalJson!;
    }
}
