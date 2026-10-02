using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins <see cref="ProjectHistoryViewModel"/>: it loads a project's history when set and again on demand,
/// shows a refusal's message rather than clearing what was already shown, and starts empty for a project
/// with none.
/// </summary>
public sealed class ProjectHistoryViewModelTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";

    [Fact]
    public void BeforeAnyProjectThereIsNoHistory()
    {
        var history = new ProjectHistoryViewModel(new FakeCommandClient());

        Assert.False(history.HasEntries);
        Assert.Empty(history.Entries);
    }

    [Fact]
    public async Task SettingAProjectLoadsItsHistoryImmediately()
    {
        var fake = new FakeCommandClient();
        fake.ProjectHistoryIs(new ProjectHistoryResponse(
            [new ProjectHistoryEntry(DateTimeOffset.UtcNow, ProjectHistoryKind.Baseline, "Baseline captured")]));
        var history = new ProjectHistoryViewModel(fake);

        await history.SetProjectAsync(ProjectPath);

        Assert.True(history.HasEntries);
        Assert.Single(history.Entries);
        Assert.Equal(ProjectPath, Assert.Single(fake.ProjectHistoryRequests).ProjectPath);
    }

    [Theory]
    [InlineData(ProjectHistoryKind.Baseline, "Refresh")]
    [InlineData(ProjectHistoryKind.Assessment, "Parse all words")]
    [InlineData(ProjectHistoryKind.Handoff, "AI Handoff")]
    public async Task EachEntryIsTitledInTheWindowsWords(ProjectHistoryKind kind, string title)
    {
        var fake = new FakeCommandClient();
        fake.ProjectHistoryIs(new ProjectHistoryResponse([new ProjectHistoryEntry(DateTimeOffset.UtcNow, kind, "142 words")]));
        var history = new ProjectHistoryViewModel(fake);

        await history.SetProjectAsync(ProjectPath);

        var entry = Assert.Single(history.Entries);
        Assert.Equal(title, entry.Title);
        Assert.Equal("142 words", entry.Summary);
    }

    [Fact]
    public void ARefreshEntryUsesTheWindowWordsForItsSummary()
    {
        var row = ProjectHistoryRow.From(new ProjectHistoryEntry(DateTimeOffset.UtcNow,
            ProjectHistoryKind.Baseline, "Baseline captured."));

        Assert.Equal("Refresh", row.Title);
        Assert.Equal("new Baseline from FieldWorks.", row.Summary);
    }

    [Fact]
    public async Task ARefusalShowsItsMessageAndLeavesTheListEmpty()
    {
        var fake = new FakeCommandClient();
        var refusal = new Refusal("app.not-built", FailureReason.Refused, "The project history query is not built yet.");
        fake.OnProjectHistory((_, _) => Task.FromResult(CommandOutcome<ProjectHistoryResponse>.Refused(refusal)));
        var history = new ProjectHistoryViewModel(fake);

        await history.SetProjectAsync(ProjectPath);

        Assert.False(history.HasEntries);
        Assert.Contains(refusal.Message, history.ShownRefusal!.Details);
    }

    [Fact]
    public async Task LoadAsyncRereadsTheSameProjectOnDemand()
    {
        var fake = new FakeCommandClient();
        fake.ProjectHistoryIs(new ProjectHistoryResponse([]));
        var history = new ProjectHistoryViewModel(fake);
        await history.SetProjectAsync(ProjectPath);

        await history.LoadAsync();

        Assert.Equal(2, fake.ProjectHistoryRequests.Count);
    }

    [Fact]
    public async Task SettingANewProjectClearsThePreviousOnesHistory()
    {
        var fake = new FakeCommandClient();
        fake.ProjectHistoryIs(new ProjectHistoryResponse(
            [new ProjectHistoryEntry(DateTimeOffset.UtcNow, ProjectHistoryKind.Handoff, "Handoff written")]));
        var history = new ProjectHistoryViewModel(fake);
        await history.SetProjectAsync(ProjectPath);
        Assert.True(history.HasEntries);

        fake.ProjectHistoryIs(new ProjectHistoryResponse([]));
        await history.SetProjectAsync(@"C:\projects\two.fwdata");

        Assert.False(history.HasEntries);
    }
}
