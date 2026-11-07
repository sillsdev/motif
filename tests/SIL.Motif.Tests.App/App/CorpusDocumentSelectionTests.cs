using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class CorpusDocumentSelectionTests
{
    [Fact]
    public async Task RefusedSwitchClearsSourceAndItsDecorations()
    {
        var selection = new CorpusDocumentSelection();
        string? source = null;
        var decorations = false;
        string? error = null;
        Task Clear() { source = null; decorations = false; return Task.CompletedTask; }
        void Present((string? Text, string? Error) result)
        {
            source = result.Text;
            decorations = result.Text is not null;
            error = result.Error;
        }
        await selection.SwitchAsync(Clear, () => ((string?)"Document A", (string?)null), Present);
        Assert.Equal("Document A", source);
        Assert.True(decorations);
        await selection.SwitchAsync(Clear, () => ((string?)null, (string?)"Document B unavailable"), Present);
        Assert.Null(source);
        Assert.False(decorations);
        Assert.Equal("Document B unavailable", error);
    }

    [Fact]
    public async Task OlderSelectionCannotOpenAfterItsClearCompletes()
    {
        var selection = new CorpusDocumentSelection();
        var pending = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var openedB = false;
        string? visible = "Document A";
        var switchB = selection.SwitchAsync(() => pending.Task, () => { openedB = true; return "B"; }, value => visible = value);
        var switchC = selection.SwitchAsync(() => { visible = null; return Task.CompletedTask; }, () => "C", value => visible = value);
        Assert.Equal("Document A", visible);
        pending.SetResult();
        await Task.WhenAll(switchB, switchC);
        Assert.False(openedB);
        Assert.Equal("C", visible);
    }
}
