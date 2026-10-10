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

/// <summary>Selects one retained corpus by its exact identity.</summary>
/// <param name="FwDataPath">The recorded project path.</param>
/// <param name="ProductVersion">The Motif product version writing the store.</param>
/// <param name="CorpusId">The exact stored corpus identity.</param>
public sealed record ShowCorpusRequest(string FwDataPath, string ProductVersion, string CorpusId);
