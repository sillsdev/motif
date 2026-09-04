using System;
using SIL.Motif.Host.Corpus;

namespace SIL.Motif.Commands.Requests;

public sealed record AddCorpusRequest(
    string FwDataPath,
    string ProductVersion,
    string CorpusId,
    string Description,
    string? Uri,
    string? Licence,
    LicenceCapabilities Capabilities,
    string Tokeniser,
    string TokeniserVersion,
    string? TokeniserNotes,
    DateTimeOffset? RetrievedUtc = null);

public sealed record AddDocumentRequest(
    string FwDataPath,
    string ProductVersion,
    string CorpusId,
    string DocumentId,
    string FileOrUrl,
    string? Title,
    string? Licence,
    LicenceCapabilities? Capabilities);

public sealed record AddCorpusBundleRequest(string FwDataPath, string ProductVersion, string BundlePath);

public sealed record ListCorporaRequest(string FwDataPath, string ProductVersion);

public sealed record ShowCorpusRequest(string FwDataPath, string ProductVersion, string CorpusId);
