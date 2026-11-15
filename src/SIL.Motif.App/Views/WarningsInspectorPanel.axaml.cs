using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>The selected warning's readable evidence, named objects and recorded words.</summary>
public sealed partial class WarningsInspectorPanel : UserControl
{
    /// <summary>Creates the warning details for <paramref name="grammar"/>.</summary>
    public WarningsInspectorPanel(GrammarViewModel grammar)
    {
        ArgumentNullException.ThrowIfNull(grammar);
        Grammar = grammar;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
        ExactWordPresentationHost = CreateHost("WarningsInspectorWordRows");
        MembershipCandidatePresentationHost = CreateHost("WarningsInspectorMembershipRows");
        SpellingCandidatePresentationHost = CreateHost("WarningsInspectorSpellingRows");
    }

    /// <summary>The grammar data shown by the selected finding.</summary>
    public GrammarViewModel Grammar { get; }

    /// <summary>The host for exact word matches in the selected finding.</summary>
    public IWordPresentationHost ExactWordPresentationHost { get; }

    /// <summary>The host for membership candidate words in the selected finding.</summary>
    public IWordPresentationHost MembershipCandidatePresentationHost { get; }

    /// <summary>The host for spelling candidate words in the selected finding.</summary>
    public IWordPresentationHost SpellingCandidatePresentationHost { get; }

    private void OnRequestAttached(object? sender, Avalonia.VisualTreeAttachmentEventArgs args)
    {
        if (sender is AskAssistantButton request && request.FindControl<Button>("AskButton") is { } button)
            button.Content = MotifAppStrings.BoardAskClaude;
    }

    private void OnShowWords(object? sender, Avalonia.Interactivity.RoutedEventArgs args)
    {
        var list = this.GetVisualDescendants().OfType<ItemsControl>().FirstOrDefault(items =>
            items.Name is "WarningsInspectorWordRows" or "WarningsInspectorMembershipRows" or "WarningsInspectorSpellingRows" &&
            items.ItemCount > 0);
        list?.BringIntoView();
    }

    private IWordPresentationHost CreateHost(string listName) =>
        new WarningWordHost(this, listName);

    private sealed class WarningWordHost(WarningsInspectorPanel panel, string listName) : IWordPresentationHost
    {
        private IWordPresentationHost? Current() => panel.GetVisualDescendants().OfType<ItemsControl>()
            .FirstOrDefault(items => items.Name == listName) is { } list
                ? new WordListPresentationHost<WordRowViewModel>(list, row => row.Presentation, WordListCardSections.Warning)
                : null;

        public Task<WordCardReadResult> ReadCardAsync(WordPresentationKey key, long revision, CancellationToken token) =>
            Current()?.ReadCardAsync(key, revision, token) ?? Task.FromResult(WordCardReadResult.Failed(
                MotifAppStrings.DiagnosticsUnavailable, false));

        public ValueTask<WordActionResult> HandleAsync(WordRequest request, CancellationToken token) =>
            Current()?.HandleAsync(request, token) ?? ValueTask.FromResult(new WordActionResult(false));
    }
}
