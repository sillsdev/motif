using CommunityToolkit.Mvvm.ComponentModel;

namespace SIL.Motif.App.ViewModels;

/// <summary>The window's pages, in the order the sidebar lists them. Any page can be opened at any time.</summary>
public enum WorkspacePage
{
    /// <summary>Where the project stands: its Baseline, its latest Assessment, its grammar and its history.</summary>
    Overview,

    /// <summary>Everything about the words in the texts: the Compare matrix, what changed, and the texts read in place.</summary>
    Texts,

    /// <summary>One word traced through the grammar on demand, whether or not the texts hold it.</summary>
    TryAWord,

    /// <summary>Where the parse time went, by kind of rule and by rule.</summary>
    Timing,

    /// <summary>The grammar's own findings, which do not depend on which texts are chosen.</summary>
    Warnings,

    /// <summary>The changes collected on any page, before any is written to FieldWorks.</summary>
    Review,

    /// <summary>Writing the AI Handoff files and passing them to a chat model.</summary>
    AiHandoff,
}

/// <summary>The sidebar label of each <see cref="WorkspacePage"/>.</summary>
public static class WorkspacePages
{
    /// <summary>What the sidebar calls <paramref name="page"/>.</summary>
    public static string TitleOf(WorkspacePage page) => page switch
    {
        WorkspacePage.Overview => "Overview",
        WorkspacePage.Texts => "Texts",
        WorkspacePage.TryAWord => "Try a Word",
        WorkspacePage.Timing => "Timing",
        WorkspacePage.Warnings => "Warnings",
        WorkspacePage.Review => "Review changes",
        WorkspacePage.AiHandoff => "AI Handoff",
        _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
    };
}

/// <summary>The views of the words that share the Texts page, each one a tab.</summary>
public enum TextsTab
{
    /// <summary>Every word in the matrix of what the project held against what the parser did, and the list it filters.</summary>
    Matrix,

    /// <summary>What changed since the run before: the words that moved between cells of the matrix.</summary>
    WhatChanged,

    /// <summary>One row per word the parser was asked about, with its analyses.</summary>
    Words,

    /// <summary>Which texts and added words are measured, and the chosen texts' words and lines.</summary>
    Texts,

    /// <summary>The chosen texts read in place, each occurrence compared with what the project stores there.</summary>
    InText,
}

/// <summary>One sidebar entry: a page's label, an optional count, and whether it is the page on screen.</summary>
public sealed partial class PageViewModel : ObservableObject
{
    public PageViewModel(WorkspacePage page, string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        Page = page;
        Title = title;
    }

    public WorkspacePage Page { get; }

    public string Title { get; }

    /// <summary>The accessible name of the sidebar entry that opens this page.</summary>
    public string AutomationName => $"{Title} page";

    /// <summary>A short count beside the label, or empty when the page has nothing to count.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    private string _badge = string.Empty;

    /// <summary>Whether <see cref="Badge"/> has anything to show.</summary>
    public bool HasBadge => Badge.Length > 0;

    /// <summary>Whether this is the page the window is showing.</summary>
    [ObservableProperty]
    private bool _isCurrent;
}
