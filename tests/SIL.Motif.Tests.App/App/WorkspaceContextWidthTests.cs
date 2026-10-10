using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Assistants;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Preferences;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins what a <see cref="WorkspaceContext"/> may carry: snapshots, the project's evidence, the clock, the one shared
/// Assessment run and its Selection, the pending changes, the shell setup dialog, desktop services and shell
/// actions; never a model one page owns.
/// </summary>
public sealed class WorkspaceContextWidthTests
{
    [Fact]
    public void TheContextCarriesSnapshotsTheSharedRunAndShellActionsButNoPageOwnedModel()
    {
        Type[] allowed =
        [
            typeof(string), typeof(bool), typeof(WorkspacePage), typeof(ProjectEvidence), typeof(WorkspaceBaseline),
            typeof(TimeProvider), typeof(WorkspaceSelection),
            typeof(GrammarSummary), typeof(OpenInspectorRequest), typeof(ChangesViewModel), typeof(AssessViewModel), typeof(SelectionViewModel),
            typeof(SetupViewModel), typeof(TextStyles),
            typeof(ICommandClient), typeof(IHandoffFolderPicker), typeof(IFileDragSource), typeof(IClipboard),
            typeof(IDiagnosticFilePicker), typeof(IDiagnosticWindowDialogs), typeof(IUriLauncher),
            typeof(IAsyncRelayCommand), typeof(IAsyncRelayCommand<string>), typeof(IRelayCommand),
            typeof(System.Collections.ObjectModel.ObservableCollection<KnownProjectSummary>),
            typeof(IAdvancedAiModePreferenceStore), typeof(IAssistantConnectionService),
        ];
        var carried = typeof(WorkspaceContext).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);

        Assert.DoesNotContain(carried, type => !allowed.Contains(type));
    }
}
