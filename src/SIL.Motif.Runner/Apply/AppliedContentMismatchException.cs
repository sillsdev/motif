namespace SIL.Motif.Runner.Apply;

/// <summary>The project's applied log records this Proposal ID with a different intent digest.</summary>
public sealed class AppliedContentMismatchException : InvalidOperationException
{
    public AppliedContentMismatchException(string message) : base(message)
    {
    }
}
