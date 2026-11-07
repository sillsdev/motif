namespace SIL.Motif.Host.Assess;

/// <summary>Owns unpublished run artifacts until an atomic Assessment commit retains them.</summary>
public sealed class AssessmentArtifactLease : IDisposable
{
    private readonly string _directory;
    private IDisposable? _sharedLease;
    private int _state;

    internal AssessmentArtifactLease(string newlyCreatedDirectory, IDisposable? sharedLease = null)
    {
        _directory = newlyCreatedDirectory;
        _sharedLease = sharedLease;
    }

    /// <summary>Transfers lifetime to the recorded Assessment after its database transaction commits.</summary>
    public void Retain()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) == 2)
            throw new ObjectDisposedException(nameof(AssessmentArtifactLease));
        Interlocked.Exchange(ref _sharedLease, null)?.Dispose();
    }

    /// <summary>Deletes unretained artifacts; repeated disposal and disposal after retention have no effect.</summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) != 0) return;
        Interlocked.Exchange(ref _sharedLease, null)?.Dispose();
        try { Directory.Delete(_directory, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
