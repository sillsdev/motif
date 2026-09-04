using System;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Host.Corpus;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Renders <see cref="CorpusCommands"/>' typed outcomes back into the pre-typed-outcome
/// <see cref="CommandResult"/> shape, through the same <see cref="CommandTextRenderer"/> the CLI itself
/// uses. See <see cref="LegacyProposalCommands"/> for why.
/// </summary>
internal static class LegacyCorpusCommands
{
    public static CommandResult AddCorpus(
        string fwDataPath,
        string productVersion,
        string corpusId,
        string description,
        string? uri,
        string? licence,
        LicenceCapabilities capabilities,
        string tokeniser,
        string tokeniserVersion,
        string? tokeniserNotes,
        DateTimeOffset? retrievedUtc = null) =>
        CommandTextRenderer.Render(
            CorpusCommands.AddCorpus(new AddCorpusRequest(
                fwDataPath, productVersion, corpusId, description, uri, licence, capabilities, tokeniser,
                tokeniserVersion, tokeniserNotes, retrievedUtc)),
            asJson: false, successAsJson: false);

    public static CommandResult AddDocument(
        string fwDataPath,
        string productVersion,
        string corpusId,
        string documentId,
        string fileOrUrl,
        string? title,
        string? licence,
        LicenceCapabilities? capabilities) =>
        CommandTextRenderer.Render(
            CorpusCommands.AddDocument(new AddDocumentRequest(
                fwDataPath, productVersion, corpusId, documentId, fileOrUrl, title, licence, capabilities)),
            asJson: false, successAsJson: false);

    public static CommandResult AddBundle(string fwDataPath, string productVersion, string bundlePath) =>
        CommandTextRenderer.Render(
            CorpusCommands.AddBundle(new AddCorpusBundleRequest(fwDataPath, productVersion, bundlePath)),
            asJson: false, successAsJson: false);

    public static CommandResult ListCorpora(string fwDataPath, string productVersion, UsageLog? usage = null) =>
        CommandTextRenderer.Render(
            CorpusCommands.ListCorpora(new ListCorporaRequest(fwDataPath, productVersion), usage), asJson: false);

    public static CommandResult ListCorporaJson(string fwDataPath, string productVersion, UsageLog? usage = null) =>
        CommandTextRenderer.Render(
            CorpusCommands.ListCorpora(new ListCorporaRequest(fwDataPath, productVersion), usage), asJson: true);

    public static CommandResult ShowCorpus(
        string fwDataPath, string productVersion, string corpusId, UsageLog? usage = null) =>
        CommandTextRenderer.Render(
            CorpusCommands.ShowCorpus(new ShowCorpusRequest(fwDataPath, productVersion, corpusId), usage),
            asJson: false);

    public static CommandResult ShowCorpusJson(
        string fwDataPath, string productVersion, string corpusId, UsageLog? usage = null) =>
        CommandTextRenderer.Render(
            CorpusCommands.ShowCorpus(new ShowCorpusRequest(fwDataPath, productVersion, corpusId), usage),
            asJson: true);
}
