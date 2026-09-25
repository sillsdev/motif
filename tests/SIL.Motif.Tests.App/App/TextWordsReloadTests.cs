using System.Linq;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins the lifetime of <see cref="TextWordsViewModel"/>'s word reads: a newer Text selection cancels the read
/// it supersedes, only the last selection's words are shown, and a failed read becomes a refusal on the model
/// instead of an exception escaping the selection handler.
/// </summary>
public sealed class TextWordsReloadTests
{
    private const string ProjectPath = @"C:\projects\one.fwdata";
    private static readonly Guid FirstTextId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondTextId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task ThreeQuickSelectionChangesCancelTheSupersededReadsAndShowOnlyTheLastChoice()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var words = new TextWordsViewModel(fake, selection);
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(FirstTextId, "Alpha"), new TextChoiceSummary(SecondTextId, "Beta")],
            HasBaseline: true));
        await selection.SetProjectAsync(ProjectPath);
        await words.SetProjectAsync(ProjectPath);

        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstPending = new TaskCompletionSource<CommandOutcome<TextWordsResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPending = new TaskCompletionSource<CommandOutcome<TextWordsResponse>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var completedReads = 0;
        fake.OnListTextWords((request, cancellationToken) =>
        {
            if (request.TextIds.SequenceEqual([FirstTextId]))
            {
                cancellationToken.Register(() => firstCancelled.TrySetResult());
                firstStarted.TrySetResult();
                return firstPending.Task;
            }
            if (request.TextIds.SequenceEqual([FirstTextId, SecondTextId]))
            {
                cancellationToken.Register(() => secondCancelled.TrySetResult());
                secondStarted.TrySetResult();
                return secondPending.Task;
            }
            if (!request.TextIds.SequenceEqual([SecondTextId]))
                return Task.FromResult(CommandOutcome<TextWordsResponse>.Success(
                    new TextWordsResponse([], [], HasBaseline: true)));
            Interlocked.Increment(ref completedReads);
            return Task.FromResult(CommandOutcome<TextWordsResponse>.Success(new TextWordsResponse(
                [new TextWord("final", null, [], [], [])],
                [new TextLines(SecondTextId, "Beta", [])], HasBaseline: true)));
        });

        selection.Texts[0].IsChecked = true;
        await firstStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        selection.Texts[1].IsChecked = true;
        await secondStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await firstCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        selection.Texts[0].IsChecked = false;
        await secondCancelled.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, completedReads);
        Assert.False(words.IsLoading);
        Assert.Equal("final", Assert.Single(words.Rows).Form);

        var stale = CommandOutcome<TextWordsResponse>.Success(new TextWordsResponse(
            [new TextWord("stale", null, [], [], [])], [], HasBaseline: true));
        firstPending.TrySetResult(stale);
        secondPending.TrySetResult(stale);
        await words.StopAsync();

        Assert.Equal("final", Assert.Single(words.Rows).Form);
        Assert.Equal([SecondTextId], words.Response!.Texts.Select(text => text.TextId));
    }

    [Fact]
    public async Task AFailedReadBecomesARefusalAndClearsTheLoadingState()
    {
        var fake = new FakeCommandClient();
        var words = new TextWordsViewModel(fake, new SelectionViewModel(fake));
        fake.OnListTextWords((_, _) => Task.FromException<CommandOutcome<TextWordsResponse>>(
            new InvalidOperationException("text words query failed")));

        var exception = await Record.ExceptionAsync(() => words.SetProjectAsync(ProjectPath));

        Assert.Null(exception);
        Assert.Equal("text words query failed", words.Refusal?.Message);
        Assert.False(words.IsLoading);
    }

    [Fact]
    public async Task AFailedReadInTheSelectionHandlerDoesNotEscapeIt()
    {
        var fake = new FakeCommandClient();
        var selection = new SelectionViewModel(fake);
        var words = new TextWordsViewModel(fake, selection);
        fake.ListTextsCompletesWith(new TextInventoryResponse(
            [new TextChoiceSummary(FirstTextId, "Alpha")], HasBaseline: true));
        await selection.SetProjectAsync(ProjectPath);
        await words.SetProjectAsync(ProjectPath);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        fake.OnListTextWords((_, _) =>
        {
            failed.TrySetResult();
            return Task.FromException<CommandOutcome<TextWordsResponse>>(new IOException("store unavailable"));
        });

        selection.Texts[0].IsChecked = true;
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await words.StopAsync();

        Assert.Equal("store unavailable", words.Refusal?.Message);
        Assert.False(words.IsLoading);
    }
}
