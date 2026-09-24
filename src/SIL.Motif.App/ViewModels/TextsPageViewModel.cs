using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// The Texts page's own state: which of its <see cref="TextsTab"/>s is showing. Kept apart from the workspace so
/// the Texts page can grow tabs without touching what the other pages share.
/// </summary>
public sealed partial class TextsPageViewModel : ObservableObject
{
    public TextsPageViewModel() => ShowTabCommand = new RelayCommand<TextsTab>(tab => Tab = tab);

    /// <summary>Which view of the words the Texts page is showing.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowMatrix))]
    [NotifyPropertyChangedFor(nameof(ShowWhatChanged))]
    [NotifyPropertyChangedFor(nameof(ShowWords))]
    [NotifyPropertyChangedFor(nameof(ShowTexts))]
    [NotifyPropertyChangedFor(nameof(ShowInText))]
    private TextsTab _tab;

    public bool ShowMatrix => Tab == TextsTab.Matrix;

    public bool ShowWhatChanged => Tab == TextsTab.WhatChanged;

    public bool ShowWords => Tab == TextsTab.Words;

    public bool ShowTexts => Tab == TextsTab.Texts;

    public bool ShowInText => Tab == TextsTab.InText;

    /// <summary>Opens the tab passed as the command parameter.</summary>
    public IRelayCommand<TextsTab> ShowTabCommand { get; }
}
