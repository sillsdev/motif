using CommunityToolkit.Mvvm.ComponentModel;
using SIL.Motif.App;

namespace SIL.Motif.App.ViewModels;

/// <summary>
/// Names the window's pages. Their order, labels, icons and views are registered together in the page registry;
/// any page can be opened at any time.
/// </summary>
public enum WorkspacePage
{
    /// <summary>Supported next actions and stored project facts.</summary>
    Progress,

    /// <summary>The current Selection's Text reader.</summary>
    Texts,

    /// <summary>The current Selection's word rows.</summary>
    Words,

    /// <summary>The project's Grammar Design Records.</summary>
    GrammarGuide,

    /// <summary>The project's Proposals and their discussion.</summary>
    Proposals,

    /// <summary>Recorded grammar warnings and rule timing.</summary>
    Diagnostics,

    /// <summary>Display choices, project parsing limits, writing systems, Texts, Help and product information.</summary>
    Settings,

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
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PageModel.BadgeCount)) BadgeCount = model.BadgeCount;
        };
    }

    public WorkspacePage Page { get; }

    /// <summary>The page's own model, which its view is built from.</summary>
    public PageModel Model { get; }

    public string Title { get; }

    /// <summary>The sidebar icon as path data on a 24-unit grid.</summary>
    public string Icon { get; }

    /// <summary>The accessible name of the sidebar entry that opens this page.</summary>
    public string AutomationName => string.Format(System.Globalization.CultureInfo.CurrentCulture,
        MotifAppStrings.PageAccessibleNameFormat, Title);

    /// <summary>The stable ID used to open this page from the sidebar.</summary>
    public string AutomationId => AutomationIds.ForPage(Page);

    /// <summary>The count <see cref="Model"/> keeps beside the label, or null when it has none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Badge), nameof(HasBadge), nameof(BadgeTip))]
    private int? _badgeCount;

    /// <summary>The count as the sidebar shows it, or empty when there is none.</summary>
    public string Badge => BadgeCount?.ToString(System.Globalization.CultureInfo.CurrentCulture) ?? string.Empty;

    /// <summary>The meaning of the sidebar count.</summary>
    public string BadgeTip => BadgeCount is not { } count ? Title : Page switch
    {
        WorkspacePage.Proposals => PluralText.Format(MotifAppStrings.ProposalsWaitingBadgePattern, count),
        _ => $"{Title}: {Badge}",
    };

    /// <summary>Whether <see cref="Badge"/> has anything to show.</summary>
    public bool HasBadge => BadgeCount is not null;

    /// <summary>Whether the window offers this page; a page it does not offer is left out of the sidebar.</summary>
    public bool IsAvailable { get; set; } = true;

    /// <summary>Whether this is the page the window is showing.</summary>
    [ObservableProperty]
    private bool _isCurrent;
}
