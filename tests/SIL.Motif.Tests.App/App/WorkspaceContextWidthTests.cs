using System.Reflection;
using CommunityToolkit.Mvvm.Input;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins what a <see cref="WorkspaceContext"/> may carry: snapshots, the one shared Assessment run and its Selection,
/// the pending changes, the command seam, desktop services and the shell's own actions; never a model one page owns.
/// </summary>
public sealed class WorkspaceContextWidthTests
{
    [Fact]
    public void TheContextCarriesSnapshotsTheSharedRunAndShellActionsButNoPageOwnedModel()
    {
        Type[] allowed =
        [
            typeof(string), typeof(bool), typeof(WorkspacePage), typeof(WorkspaceEvidence), typeof(WorkspaceBaseline),
            typeof(GrammarSummary), typeof(ChangesViewModel), typeof(AssessViewModel), typeof(SelectionViewModel),
            typeof(ICommandClient), typeof(IHandoffFolderPicker), typeof(IFileDragSource),
            typeof(IAsyncRelayCommand), typeof(IAsyncRelayCommand<string>),
            typeof(System.Collections.ObjectModel.ObservableCollection<KnownProjectSummary>),
        ];
        var carried = typeof(WorkspaceContext).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType);

        Assert.Empty(carried.Where(type => !allowed.Contains(type)));
    }
}
