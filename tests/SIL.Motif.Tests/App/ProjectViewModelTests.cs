using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
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
    public async Task BrowseCommandRaisesNothingWhenTheCallerCancels()
    {
        var picker = new FakeProjectPicker { PathToReturn = null };
        var viewModel = new ProjectViewModel(new FakeCommandClient(), picker);

        var raised = false;
        viewModel.ProjectChosen += (_, _) => raised = true;
        await viewModel.BrowseCommand.ExecuteAsync(null);

        Assert.False(raised);
    }

    private sealed class FakeProjectPicker : IProjectPicker
    {
        public string? PathToReturn { get; set; }

        public Task<string?> PickProjectFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(PathToReturn);
    }
}
