namespace SIL.Motif.Contract.Responses;

/// <summary>
/// A Corpus created empty, with its origin and tokenisation recorded. <see cref="DerivationNote"/> is the
/// one line <c>add-corpus</c> has always printed explaining whether derived artefacts (n-gram models,
/// published word lists) are permitted from this Corpus, computed once here from its licence capabilities
/// so the CLI never needs a licence-capabilities decision of its own to reproduce it.
/// </summary>
public sealed record CorpusAddedResponse(
    string CorpusId,
    string Description,
    string? Uri,
    string? Licence,
    string Tokeniser,
    string TokeniserVersion,
    string DerivationNote);

/// <summary>One Document added to a Corpus, from a file on disk or a URL retrieved.</summary>
public sealed record CorpusDocumentAddedResponse(
    string CorpusId,
    string DocumentId,
    string Title,
    string Source,
    long CharacterCount,
    string ContentSha256,
    string? Licence);

/// <summary>
/// A whole handoff bundle ingested as a new Corpus. <see cref="DerivationRestrictions"/> and
/// <see cref="AccuracyClaimsNote"/> are the same two prose lines <c>add-corpus-bundle</c> has always
/// printed, computed once here from the bundle's recorded provenance.
/// </summary>
public sealed record CorpusBundleAddedResponse(
    string CorpusId,
    int DocumentCount,
    string OriginDescription,
    string? Licence,
    string DerivationRestrictions,
    string AccuracyClaimsNote);

/// <summary>A durable job just entered the queue.</summary>
public sealed record JobEnqueuedResponse(string JobId, string Kind, string ProjectKey);
