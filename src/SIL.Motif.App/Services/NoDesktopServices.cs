namespace SIL.Motif.App.Services;

/// <summary>
/// What a view model holds when it was built without a clipboard or diagnostic file dialogs: every use
/// throws, so a composition that forgot one fails where it is used rather than doing nothing.
/// </summary>
internal sealed class NoDesktopServices : IClipboard, IDiagnosticFilePicker
{
    internal static NoDesktopServices Instance { get; } = new();

    private NoDesktopServices() { }

    public Task SetTextAsync(string text, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("This window was composed without a clipboard.");

    public Task<string?> OpenDiagnosticAsync(CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("This window was composed without diagnostic file dialogs.");

    public Task<bool> SaveDiagnosticAsync(string suggestedFileName, string json, CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException("This window was composed without diagnostic file dialogs.");
}
