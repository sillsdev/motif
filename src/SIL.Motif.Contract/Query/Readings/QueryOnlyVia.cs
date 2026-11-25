namespace SIL.Motif.Contract.Query;

/// <summary>
/// <c>only-via(entry:ID)</c>: the word has at least one reading and every reading uses the morpheme. The morpheme is an entry, an
/// analysis (<c>msa:</c>) or a form (<c>form:</c>) id. Running it reads the cached corpus parse, so it needs --grammar.
/// </summary>
/// <param name="Kind"><c>entry</c>, <c>msa</c> or <c>form</c>.</param>
/// <param name="Id">The id.</param>
public sealed record QueryOnlyVia(string Kind, string Id) : QueryExpr;
