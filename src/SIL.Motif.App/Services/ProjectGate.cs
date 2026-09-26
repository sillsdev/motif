namespace SIL.Motif.App.Services;

/// <summary>
/// Lets one call at a time use the project. <see cref="CommandClient"/> takes it around every call that opens
/// the project through LibLCM or the parser. A test substitutes its own to decide exactly when the project is
/// busy and when it is handed over.
/// </summary>
internal interface IProjectGate
{
    /// <summary>Takes the project if it is free right now; true when this caller now holds it.</summary>
    bool TryEnter();

    /// <summary>
    /// Completes once this caller holds the project, or is cancelled through <paramref name="cancellationToken"/>
    /// while still waiting. A completed wait may still find the token cancelled, since the two can happen together.
    /// </summary>
    Task EnterAsync(CancellationToken cancellationToken);

    /// <summary>Gives the project back after <see cref="TryEnter"/> or <see cref="EnterAsync"/> took it.</summary>
    void Exit();
}

/// <summary>The product's <see cref="IProjectGate"/>: a single slot.</summary>
internal sealed class ProjectGate : IProjectGate
{
    private readonly SemaphoreSlim _slot = new(1, 1);

    public bool TryEnter() => _slot.Wait(0);

    public Task EnterAsync(CancellationToken cancellationToken) => _slot.WaitAsync(cancellationToken);

    public void Exit() => _slot.Release();
}
