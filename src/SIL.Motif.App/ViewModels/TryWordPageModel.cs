namespace SIL.Motif.App.ViewModels;

/// <summary>Opens Try a Word on one word and traces it straight away.</summary>
/// <param name="Word">The word to trace.</param>
public sealed record TryWordRequest(string Word) : PageRequest(WorkspacePage.TryAWord);

/// <summary>The Try a Word page's model: one word traced against the current Baseline's grammar on demand.</summary>
public sealed class TryWordPageModel(WorkspaceContext context) : PageModel(context)
{
    public TraceWordViewModel Trace => Context.Assess.Trace;

    protected override void OnProjectCleared() => Trace.Reset();

    // Asked for by a click, so this traces straight away rather than only priming the box.
    protected override void OnRequested(PageRequest request)
    {
        if (request is not TryWordRequest tried) return;
        Context.Assess.SelectWord(tried.Word);
        Trace.SetWord(tried.Word);
        if (Trace.TryCommand.CanExecute(null)) _ = Trace.TryCommand.ExecuteAsync(null);
    }
}
