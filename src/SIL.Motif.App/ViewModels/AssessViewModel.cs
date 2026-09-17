using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Adapts the Assessment request and response to the shared cancellable command-run module. The module
/// owns lifecycle, cancellation, progress, and refusal classification; this adapter owns Selection input.
/// </summary>
public sealed partial class AssessViewModel : CommandRunViewModel<AssessCommandResponse>
{
    private readonly ICommandClient _commandClient;
    private readonly SelectionViewModel _selection;

    public AssessViewModel(ICommandClient commandClient, SelectionViewModel selection)
    {
        ArgumentNullException.ThrowIfNull(commandClient);
        ArgumentNullException.ThrowIfNull(selection);
        _commandClient = commandClient;
        _selection = selection;
        _selection.PropertyChanged += OnSelectionPropertyChanged;
    }

    /// <summary>The project this Assessment measures, or <c>null</c> before a project has been chosen.</summary>
    [ObservableProperty]
    private string? _projectPath;

    partial void OnProjectPathChanged(string? value) => RunCommand.NotifyCanExecuteChanged();

    protected override bool CanStartCore() => ProjectPath is not null && _selection.CanAssess;

    protected override Task<CommandOutcome<AssessCommandResponse>> ExecuteCoreAsync(
        CancellationToken cancellationToken)
    {
        var request = new AssessRequest(ProjectPath!, _selection.BuildRequest());
        return _commandClient.AssessAsync(request, this, cancellationToken);
    }

    private void OnSelectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SelectionViewModel.CanAssess)) RunCommand.NotifyCanExecuteChanged();
    }
}
