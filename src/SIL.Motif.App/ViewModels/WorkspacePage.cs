using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Names the window's pages. Their order, labels, icons and views are registered together in the page registry;
/// any page can be opened at any time.
/// </summary>
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

/// <summary>The views of the Texts page, each one a tab.</summary>
public enum TextsTab
{
    /// <summary>The Compare matrix, its fix-first panel, and the list it filters.</summary>
    Matrix,

    /// <summary>The chosen texts read in place, with the project's analysis and parser's answer together.</summary>
    AnalyzeTexts,

    /// <summary>Named questions about words, each one a selection of exact Compare cells.</summary>
    Lists,

    /// <summary>Words that moved between cells in the two most recent Assessments.</summary>
    WhatChanged,
}

/// <summary>
/// One sidebar entry: a page's label, the count its <see cref="Model"/> keeps beside it, and whether it is the page
/// on screen.
/// </summary>
public sealed partial class PageViewModel : ObservableObject
{
    public PageViewModel(WorkspacePage page, string title, string icon, PageModel model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(icon);
        ArgumentNullException.ThrowIfNull(model);
        Page = page;
        Title = title;
        Icon = icon;
        Model = model;
        _badge = model.Badge;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PageModel.Badge)) Badge = model.Badge;
        };
    }

    public WorkspacePage Page { get; }

    /// <summary>The page's own model, which its view is built from.</summary>
    public PageModel Model { get; }

    public string Title { get; }

    /// <summary>The sidebar icon as path data on a 24-unit grid.</summary>
    public string Icon { get; }

    /// <summary>The accessible name of the sidebar entry that opens this page.</summary>
    public string AutomationName => $"{Title} page";

    /// <summary>The stable ID used to open this page from the sidebar.</summary>
    public string AutomationId => AutomationIds.ForPage(Page);

    /// <summary>The count <see cref="Model"/> keeps beside the label, or empty when it has none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    private string _badge = string.Empty;

    /// <summary>Whether <see cref="Badge"/> has anything to show.</summary>
    public bool HasBadge => Badge.Length > 0;

    /// <summary>Whether this is the page the window is showing.</summary>
    [ObservableProperty]
    private bool _isCurrent;
}
