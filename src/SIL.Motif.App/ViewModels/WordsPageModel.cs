namespace SIL.Motif.App.ViewModels;

/// <summary>The current Selection's word rows, shared with the reader.</summary>
public sealed class WordsPageModel : PageModel
{
    public WordsPageModel(WorkspaceContext context) : base(context)
    {
        Words = context.GetOrCreateTextWords();
    }

    /// <summary>The Selection word rows.</summary>
    public TextWordsViewModel Words { get; }
}
