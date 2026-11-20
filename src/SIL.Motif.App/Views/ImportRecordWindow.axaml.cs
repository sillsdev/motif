using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>Previews an edited record file, stages its changes in a new Draft and opens the Draft for review.</summary>
public sealed partial class ImportRecordWindow : Window
{
    /// <summary>Creates the window over a model; the window disposes the model when it closes.</summary>
    public ImportRecordWindow(ImportRecordDialogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        AvaloniaXamlLoader.Load(this);
        Model = model;
        DataContext = model;
        model.DraftOpened += (_, _) => Close();
        Closed += (_, _) => Model.Dispose();
    }

    /// <summary>Builds the window for a project, opening the staged Draft in <paramref name="context"/>'s Proposals page.</summary>
    /// <param name="context">The workspace the Draft opens in.</param>
    /// <param name="projectPath">The project the file was exported from.</param>
    /// <param name="picker">Chooses the file; when null the window's own storage dialog is used.</param>
    internal static ImportRecordWindow For(WorkspaceContext context, string projectPath, IRecordFilePicker? picker)
    {
        ArgumentNullException.ThrowIfNull(context);
        var late = new LatePicker(picker);
        var model = new ImportRecordDialogModel(new ImportRecordViewModel(context.Commands, projectPath), late,
            context.OpenStagedProposalAsync);
        var window = new ImportRecordWindow(model);
        late.Fallback = new AvaloniaStoragePickers(window);
        return window;
    }

    // The window must exist before its own storage dialog can, so the model is given this stand-in first.
    private sealed class LatePicker(IRecordFilePicker? chosen) : IRecordFilePicker
    {
        public IRecordFilePicker? Fallback { get; set; }

        public Task<string?> PickRecordFileAsync(CancellationToken cancellationToken = default) =>
            (chosen ?? Fallback ?? throw new InvalidOperationException("The window has no file chooser."))
                .PickRecordFileAsync(cancellationToken);
    }

    /// <summary>What the window shows and does.</summary>
    public ImportRecordDialogModel Model { get; }

    // Cancel stops a preview in flight and closes; it is never disabled, so it works while busy too.
    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        Model.Cancel();
        Close();
    }
}
