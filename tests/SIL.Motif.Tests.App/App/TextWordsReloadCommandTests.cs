using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that a Text selection change reloads the words through <see cref="TextWordsViewModel.ReloadCommand"/>,
/// so the read the checkbox started is a task someone can observe rather than an unobserved handler, and a read
/// that throws surfaces as a refusal on the model while the command's own task still completes.
/// </summary>
public sealed class TextWordsReloadCommandTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";
    private static readonly Guid TextId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task ASelectionChangeReloadsThroughAnObservableCommand()
    {
        var (fake, selection, words) = await OpenAsync();
        fake.OnListTextWords((_, _) => Task.FromResult(CommandOutcome<TextWordsResponse>.Success(
            new TextWordsResponse([], [], HasBaseline: true))));

        selection.Texts[0].IsChecked = true;

        var reload = Assert.IsAssignableFrom<Task>(words.ReloadCommand.ExecutionTask);
        await reload.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(reload.IsCompletedSuccessfully);
        Assert.False(words.IsLoading);
    }

    [Fact]
    public async Task ASelectionReloadThatThrowsShowsTheRefusalAndItsTaskStillCompletes()
    {
        var (fake, selection, words) = await OpenAsync();
        fake.OnListTextWords((_, _) =>
            Task.FromException<CommandOutcome<TextWordsResponse>>(new IOException("store unavailable")));

        selection.Texts[0].IsChecked = true;

        var reload = Assert.IsAssignableFrom<Task>(words.ReloadCommand.ExecutionTask);
        await reload.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(reload.IsCompletedSuccessfully);
        Assert.Equal("store unavailable", words.Refusal?.Message);
        Assert.False(words.IsLoading);
    }

    private static async Task<(FakeCommandClient, SelectionViewModel, TextWordsViewModel)> OpenAsync()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var words = new TextWordsViewModel(fake, selection);
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(TextId, "Alpha")], HasBaseline: true));
        await selection.SetProjectAsync(ProjectPath);
        await words.SetProjectAsync(ProjectPath);
        return (fake, selection, words);
    }
}
