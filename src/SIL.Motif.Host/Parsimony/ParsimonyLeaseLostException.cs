namespace SIL.Motif.Host.Parsimony;

/// <summary>Stops a Parsimony query whose reader lease could no longer be renewed.</summary>
/// <remarks>
/// A cancellation, so an unwinding caller treats it as one. It is its own type so the command layer can
/// refuse it as a retryable busy bundle, rather than as a failed query.
/// </remarks>
public sealed class ParsimonyLeaseLostException : OperationCanceledException
{
    /// <summary>Creates the exception for a query whose reader lease is no longer held.</summary>
    public ParsimonyLeaseLostException()
        : base("The Parsimony reader lease was lost before the query finished.")
    {
    }
}
