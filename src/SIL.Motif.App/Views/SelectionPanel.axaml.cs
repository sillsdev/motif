using Avalonia.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;

namespace SIL.Motif.App.Views;

/// <summary>Text and word Selection editor, and the checked Texts' words, bound to their own view models.</summary>
public sealed partial class SelectionPanel : UserControl
{
    public SelectionPanel(SelectionViewModel selection, TextWordsViewModel words)
        : this(selection, words, null)
    {
    }

    internal SelectionPanel(SelectionViewModel selection, TextWordsViewModel words, SettingsViewModel? settings)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(words);
        Selection = selection;
        Words = words;
        Settings = settings;
        DataContext = this;
        AvaloniaXamlLoader.Load(this);
    }

    public SelectionViewModel Selection { get; }

    public TextWordsViewModel Words { get; }

    internal SettingsViewModel? Settings { get; }

    private async void OnNoStepLimitClick(object? sender, RoutedEventArgs e)
    {
        if (Settings is not null) await Settings.CommitParsingLimitsAsync().ConfigureAwait(true);
    }

    private async void OnUseEstimateClick(object? sender, RoutedEventArgs e)
    {
        if (Settings is null) return;
        Settings.UseMotifEstimate();
        await Settings.CommitParsingLimitsAsync().ConfigureAwait(true);
    }

    private async void OnLimitLostFocus(object? sender, RoutedEventArgs e)
    {
        if (Settings is not null) await Settings.CommitParsingLimitsAsync().ConfigureAwait(true);
    }

    [KeyboardShortcutHandler("TextReader:CommitParsingLimits", "TextReader:CancelParsingLimits")]
    private async void OnLimitKeyDown(object? sender, KeyEventArgs e)
    {
        if (Settings is null) return;
        var commit = KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, e.Key, e.KeyModifiers,
            targetBehaviors: [KeyboardShortcutBehavior.CommitParsingLimits]);
        if (commit is not null)
        {
            e.Handled = true;
            await Settings.CommitParsingLimitsAsync().ConfigureAwait(true);
        }
        else if (KeyboardShortcutRegistry.Find(KeyboardShortcutScope.TextReader, e.Key, e.KeyModifiers,
                     targetBehaviors: [KeyboardShortcutBehavior.CancelParsingLimits]) is not null &&
                 Settings.HasUncommittedParsingLimitChanges)
        {
            Settings.CancelParsingLimitEdit();
            e.Handled = true;
        }
    }
}
