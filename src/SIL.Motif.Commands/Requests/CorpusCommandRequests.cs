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

/// <summary>Selects a Document for the shared read-only corpus viewer.</summary>
public sealed record OpenCorpusDocumentRequest(string FwDataPath, string ProductVersion, string CorpusId, string DocumentId);

/// <summary>Selects a byte range bound to an exact Document hash.</summary>
public sealed record CorpusRangeRequest(string FwDataPath, string ProductVersion, string CorpusId,
    string DocumentId, string Sha256, long Offset, int Length);

/// <summary>UTF-8 bytes returned for a hash-bound Document range.</summary>
public sealed record CorpusRangeResponse(string Sha256, long Offset, byte[] Bytes);

/// <summary>One pasted Document with its supplied identity and provenance.</summary>
public sealed record PasteCorpusDocumentRequest(string FwDataPath, string ProductVersion, string CorpusId,
    string DocumentId, string Title, string Text, string? Licence, LicenceCapabilities? Capabilities,
    System.Collections.Generic.IReadOnlyDictionary<string, string>? Attributes = null);

/// <summary>Rebuilds the project Corpus index from its published sidecars.</summary>
public sealed record RebuildCorpusIndexRequest(string FwDataPath, string ProductVersion);

/// <summary>Reports how many published Corpora were indexed.</summary>
public sealed record RebuildCorpusIndexResponse(int CorpusCount);
