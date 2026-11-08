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

    /// <summary>Every Proposal, grouped by what to do next, with its numbers before and after its Trial.</summary>
    Proposals,

    /// <summary>Everything about the words in the texts: the Compare matrix, what changed, and the texts read in place.</summary>
    Texts,

    /// <summary>One word traced through the grammar on demand, whether or not the texts hold it.</summary>
    TryAWord,

    /// <summary>Where the parse time went, by kind of rule and by rule.</summary>
    Timing,

    /// <summary>The grammar's own findings, which do not depend on which texts are chosen.</summary>
    Warnings,

    /// <summary>The newest stored Parsimony Report's findings, read without starting a check.</summary>
    Parsimony,

    /// <summary>The changes collected on any page, before any is written to FieldWorks.</summary>
    Review,

    /// <summary>Writing the AI Handoff files and passing them to a chat model.</summary>
    AiHandoff,

    /// <summary>Choosing assistants that may use Motif's tools.</summary>
    AiAssistants,

    /// <summary>Display choices, project parsing limits, writing systems, Texts, Help and product information.</summary>
    Settings,
}

/// <summary>Which pages exist only while Advanced AI mode is on; the sidebar omits them and opening one does nothing.</summary>
public static class WorkspacePages
{
    /// <summary>Whether <paramref name="page"/> requires the assistant tools setting.</summary>
    public static bool RequiresAdvancedAi(WorkspacePage page) => false;
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
        _badgeCount = model.BadgeCount;
        _delta = model.ProposalDelta;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PageModel.BadgeCount)) BadgeCount = model.BadgeCount;
            if (e.PropertyName == nameof(PageModel.ProposalDelta)) Delta = model.ProposalDelta;
        };
    }

    public WorkspacePage Page { get; }

    /// <summary>The page's own model, which its view is built from.</summary>
    public PageModel Model { get; }

    public string Title { get; }

    /// <summary>The sidebar icon as path data on a 24-unit grid.</summary>
    public string Icon { get; }

    /// <summary>The accessible name of the sidebar entry that opens this page.</summary>
    public string AutomationName => Delta is { } delta
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.PageDeltaPageNameFormat,
            $"{Title} page", delta.AccessibleName)
        : $"{Title} page";

    /// <summary>The stable ID used to open this page from the sidebar.</summary>
    public string AutomationId => AutomationIds.ForPage(Page);

    /// <summary>The count <see cref="Model"/> keeps beside the label, or null when it has none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge), nameof(HasBadge), nameof(ChangesTip), nameof(WarningsTip), nameof(BadgeTip))]
    private int? _badgeCount;

    /// <summary>The count as the sidebar shows it, or empty when there is none.</summary>
    public string Badge => BadgeCount?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>The meaning of the sidebar count.</summary>
    public string BadgeTip => BadgeCount is not { } count ? Title : Page switch
    {
        WorkspacePage.Proposals => PluralText.Format(MotifAppStrings.ProposalsWaitingBadgePattern, count),
        WorkspacePage.Warnings => WarningsTip,
        WorkspacePage.Review => ChangesTip,
        _ => $"{Title}: {Badge}",
    };

    /// <summary>The status bar's changes sentence, with its count inside the localized pattern.</summary>
    public string ChangesTip => PluralText.Format(MotifAppStrings.StatusChangesPattern, BadgeCount ?? 0);

    /// <summary>The status bar's warnings sentence, with its count inside the localized pattern.</summary>
    public string WarningsTip => PluralText.Format(MotifAppStrings.StatusWarningsPattern, BadgeCount ?? 0);

    // How far the open Proposal moves the page's number from today's, or null when there is nothing to show.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeltaBadge), nameof(HasDeltaBadge), nameof(DeltaBadgeName), nameof(AutomationName), nameof(RailTip))]
    private PageDelta? _delta;

    /// <summary>The sidebar tooltip: the title, with what the delta badge counts when there is one.</summary>
    public string RailTip => Delta is { } delta
        ? string.Format(System.Globalization.CultureInfo.CurrentCulture, MotifAppStrings.PageDeltaPageNameFormat, Title,
            delta.AccessibleName)
        : Title;

    /// <summary>The delta with its sign, or empty when there is none.</summary>
    public string DeltaBadge => Delta?.Text ?? string.Empty;

    /// <summary>What the delta counts and whose number it is, for the badge's accessible name.</summary>
    public string DeltaBadgeName => Delta?.AccessibleName ?? string.Empty;

    /// <summary>Whether <see cref="DeltaBadge"/> has anything to show.</summary>
    public bool HasDeltaBadge => Delta is not null;

    /// <summary>Whether <see cref="Badge"/> has anything to show.</summary>
    public bool HasBadge => BadgeCount is not null;

    /// <summary>Whether the window offers this page; a page it does not offer is left out of the sidebar.</summary>
    public bool IsAvailable { get; init; } = true;

    /// <summary>Whether this is the page the window is showing.</summary>
    [ObservableProperty]
    private bool _isCurrent;
}
