using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Host.Store;
using System.Collections.Specialized;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="ProjectViewModel"/>: it lists Known projects from <see cref="ICommandClient"/> in the
/// order the query returns them, and raises <see cref="ProjectViewModel.ProjectChosen"/> with the chosen
/// path whether the choice came from picking a Known project or from <see cref="IProjectPicker"/>.
/// </summary>
public sealed class ProjectViewModelTests
{
    [Fact]
    public async Task LoadKnownProjectsAsyncPopulatesKnownProjectsInQueryOrder()
    {
        var fake = new FakeCommandClient();
        var projects = new[]
        {
            new KnownProjectSummary(@"C:\projects\newest.fwdata", DateTimeOffset.UtcNow),
            new KnownProjectSummary(@"C:\projects\oldest.fwdata", DateTimeOffset.UtcNow.AddDays(-1)),
        };
        fake.KnownProjectsListIs(projects);
        var viewModel = new ProjectViewModel(fake, new FakeProjectPicker());

        await viewModel.LoadKnownProjectsAsync();

        Assert.Equal(projects, viewModel.KnownProjects);
    }

    [Fact]
    public async Task LoadingTheSameKnownProjectsDoesNotNotifyCollectionChanged()
    {
        var projects = new[]
        {
            new KnownProjectSummary(@"C:\projects\newest.fwdata", DateTimeOffset.UtcNow),
            new KnownProjectSummary(@"C:\projects\oldest.fwdata", DateTimeOffset.UtcNow.AddDays(-1)),
        };
        var fake = new FakeCommandClient();
        fake.KnownProjectsListIs(projects);
        var viewModel = new ProjectViewModel(fake, new FakeProjectPicker());
        await viewModel.LoadKnownProjectsAsync();
        fake.KnownProjectsListIs(projects.Select(project =>
            project with { LastSeenUtc = project.LastSeenUtc.AddMinutes(1) }).ToArray());
        var notifications = 0;
        viewModel.KnownProjects.CollectionChanged += CountNotification;

        await viewModel.LoadKnownProjectsAsync();

        Assert.Equal(0, notifications);
        Assert.Equal(
            projects.Select(project => project.FullFwDataPath),
            viewModel.KnownProjects.Select(project => project.FullFwDataPath));

        void CountNotification(object? sender, NotifyCollectionChangedEventArgs args) => notifications++;
    }

    [Fact]
    public async Task SelectingAKnownProjectRaisesProjectChosenWithItsPath()
    {
        var fake = new FakeCommandClient();
        var project = new KnownProjectSummary(@"C:\projects\one.fwdata", DateTimeOffset.UtcNow);
        fake.KnownProjectsListIs([project]);
        var viewModel = new ProjectViewModel(fake, new FakeProjectPicker());
        await viewModel.LoadKnownProjectsAsync();

        string? chosen = null;
        viewModel.ProjectChosen += (_, path) => chosen = path;
        viewModel.SelectedKnownProject = viewModel.KnownProjects[0];

        Assert.Equal(project.FullFwDataPath, chosen);
    }

    [Fact]
    public async Task BrowseCommandRaisesProjectChosenWithThePickedPath()
    {
        var picker = new FakeProjectPicker { PathToReturn = @"C:\projects\browsed.fwdata" };
        var viewModel = new ProjectViewModel(new FakeCommandClient(), picker);

        string? chosen = null;
        viewModel.ProjectChosen += (_, path) => chosen = path;
        await viewModel.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\projects\browsed.fwdata", chosen);
    }

    [Fact]
    public async Task AnOpenProjectIsShownAsThePickersChoiceWithoutBeingChosenAgain()
    {
        var fake = new FakeCommandClient();
        var listed = new KnownProjectSummary(@"C:\projects\listed.fwdata", DateTimeOffset.UtcNow);
        fake.KnownProjectsListIs([listed]);
        var viewModel = new ProjectViewModel(fake, new FakeProjectPicker());
        await viewModel.LoadKnownProjectsAsync();
        var raised = 0;
        viewModel.ProjectChosen += (_, _) => raised++;

        viewModel.ShowChosen(@"C:\PROJECTS\listed.fwdata");
        Assert.Same(listed, viewModel.SelectedKnownProject);
        Assert.Null(viewModel.OpenUnlistedPath);

        // A browsed project is named but not added, so picking it from the list later still opens it.
        viewModel.ShowChosen(@"C:\projects\browsed.fwdata");
        Assert.Null(viewModel.SelectedKnownProject);
        Assert.Equal(@"C:\projects\browsed.fwdata", viewModel.OpenUnlistedPath);
        Assert.Single(viewModel.KnownProjects);
        Assert.Equal(0, raised);
    }

    [Fact]
    public async Task BrowseCommandRaisesNothingWhenTheCallerCancels()
    {
        var picker = new FakeProjectPicker { PathToReturn = null };
        var viewModel = new ProjectViewModel(new FakeCommandClient(), picker);

        var raised = false;
        viewModel.ProjectChosen += (_, _) => raised = true;
        await viewModel.BrowseCommand.ExecuteAsync(null);

        Assert.False(raised);
    }

    [Theory]
    [InlineData("corrupt")]
    [InlineData("wrong-schema")]
    public async Task ARefusedMachineStoreShowsRecoveryAndDoesNotBlockBrowse(string shape)
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-machine-store-browse", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var machinePath = Path.Combine(root, "motif.db");
            if (shape == "corrupt")
                File.WriteAllText(machinePath, "not a sqlite database");
            else
            {
                using var machine = MachineDatabase.Open(root);
                using var connection = machine.OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA user_version=999;";
                command.ExecuteNonQuery();
            }

            var picker = new FakeProjectPicker();
            var viewModel = new ProjectViewModel(RealCommandClient.Create(root), picker, root);
            await viewModel.LoadKnownProjectsAsync();

            Assert.True(viewModel.HasMachineStoreRecovery);
            Assert.Contains(machinePath, viewModel.MachineStoreRecoveryMessage, StringComparison.Ordinal);
            Assert.Contains("Keep a copy", viewModel.MachineStoreRecoveryMessage, StringComparison.Ordinal);
            Assert.Contains("Operation: read Known projects", viewModel.MachineStoreProblemReport!.ToText(),
                StringComparison.Ordinal);
            Assert.DoesNotContain(root, viewModel.MachineStoreProblemReport.ToText(), StringComparison.Ordinal);

            await viewModel.BrowseCommand.ExecuteAsync(null);

            Assert.Equal(1, picker.PickCalls);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private sealed class FakeProjectPicker : IProjectPicker
    {
        public string? PathToReturn { get; set; }

        public int PickCalls { get; private set; }

        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default)
        {
            PickCalls++;
            return Task.FromResult(PathToReturn);
        }
    }
}
