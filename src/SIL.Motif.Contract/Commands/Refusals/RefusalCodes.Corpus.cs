namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    /// <summary>A corpus bundle could not be read.</summary>
    public const string CorpusBundleInvalid = "corpus.bundle-invalid";
    /// <summary>A corpus document could not be added or read.</summary>
    public const string CorpusDocumentInvalid = "corpus.document-invalid";
    /// <summary>The named corpus has no such document.</summary>
    public const string CorpusDocumentNotFound = "corpus.document-not-found";
    /// <summary>A corpus request is not valid.</summary>
    public const string CorpusInvalid = "corpus.invalid";
    /// <summary>The store holds no corpus with that id.</summary>
    public const string CorpusNotFound = "corpus.not-found";
}
