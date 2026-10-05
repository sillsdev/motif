using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveMarkdown.Avalonia;
using SIL.Motif.App.Services;
using SIL.Motif.Help;

namespace SIL.Motif.App.ViewModels;

/// <summary>Provides the current Help entry and routes its in-app and online links.</summary>
public sealed partial class HelpPopupViewModel : ObservableObject
{
    private readonly HelpContentSource _content;
    private readonly IUriLauncher _uriLauncher;

    /// <summary>Builds the Help view model with the desktop adapter for online links.</summary>
    /// <param name="uriLauncher">The adapter that opens a web address.</param>
    /// <param name="catalog">The help catalog, or null to load the current locale.</param>
    public HelpPopupViewModel(IUriLauncher uriLauncher, HelpCatalog? catalog = null)
    {
        ArgumentNullException.ThrowIfNull(uriLauncher);
        _uriLauncher = uriLauncher;
        _content = new HelpContentSource(catalog ?? HelpCatalog.Load());
        OpenOnlineCommand = new AsyncRelayCommand(OpenOnlineAsync);
        LinkCommand = new AsyncRelayCommand<LinkClickedEventArgs>(FollowLinkAsync);
        SetPage(_content.ForPage(WorkspacePage.Overview));
    }

    /// <summary>The title of the current help entry.</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>The short explanation of the current help entry.</summary>
    [ObservableProperty]
    private string _description = string.Empty;

    /// <summary>Whether the header shows the description; a page's own opening paragraph already says it.</summary>
    [ObservableProperty]
    private bool _showsDescription;

    /// <summary>The Markdown of the current help entry, less the heading the header shows.</summary>
    [ObservableProperty]
    private string _markdown = string.Empty;

    /// <summary>The online documentation address for the current help entry.</summary>
    [ObservableProperty]
    private string _onlineUrl = string.Empty;

    /// <summary>Opens the current help page in the default browser.</summary>
    public IAsyncRelayCommand OpenOnlineCommand { get; }

    /// <summary>Follows a command, term, control, or Guide link inside Help, or opens a web link.</summary>
    public IAsyncRelayCommand<LinkClickedEventArgs> LinkCommand { get; }

    /// <summary>Shows the help entry for <paramref name="page"/> or a focused control with catalogued help.</summary>
    /// <param name="page">The selected page.</param>
    /// <param name="focusedAutomationId">The focused control's stable id, when it has one.</param>
    public void ShowForPage(WorkspacePage page, string? focusedAutomationId = null) =>
        SetPage(_content.ForPage(page, focusedAutomationId));

    /// <summary>Shows the short guide to PanGloss from Analyze texts.</summary>
    public void ShowPanGlossPage() => SetPage(_content.PanGlossPage());

    /// <summary>Shows the guide to writing-system display in the window.</summary>
    public void ShowWritingSystemsPage() => SetPage(_content.WritingSystemsPage());

    private async Task OpenOnlineAsync() =>
        await _uriLauncher.LaunchAsync(new Uri(OnlineUrl, UriKind.Absolute));

    private async Task FollowLinkAsync(LinkClickedEventArgs? args)
    {
        if (args?.HRef is not { } href) return;
        if (_content.TryResolveLink(href, out var page))
        {
            SetPage(page);
            return;
        }

        if (href.IsAbsoluteUri && href.Scheme is "http" or "https")
            await _uriLauncher.LaunchAsync(href);
    }

    private void SetPage(HelpPageContent page)
    {
        Title = page.Title;
        Description = page.Description;
        Markdown = page.Markdown;
        ShowsDescription = string.IsNullOrWhiteSpace(page.Markdown);
        OnlineUrl = page.Url;
    }

    private sealed record HelpPageContent(string Title, string Description, string Markdown, string Url);

    private sealed class HelpContentSource
    {
        private static readonly Regex ShotImage = new(
            @"!\[(?<alt>[^\]]*)\]\(shot:[^)]+\)", RegexOptions.CultureInvariant);
        private static readonly Regex OpeningHeading = new(@"\A\s*# [^\n]*\n?", RegexOptions.CultureInvariant);
        private readonly HelpCatalog _catalog;

        public HelpContentSource(HelpCatalog catalog) => _catalog = catalog;

        public HelpPageContent ForPage(WorkspacePage page, string? focusedAutomationId = null)
        {
            if (!string.IsNullOrWhiteSpace(focusedAutomationId) &&
                _catalog.Find(HelpEntryKind.Ui, focusedAutomationId) is { } focused)
                return EntryPage(focused);

            var pageEntry = _catalog.Find(HelpEntryKind.Ui, PageCode(page));
            if (pageEntry?.HelpPage is not null) return EntryPage(pageEntry);

            var guide = GuidePage(page);
            return pageEntry is null
                ? guide
                : guide with { Title = pageEntry.Title, Description = pageEntry.Description };
        }

        public HelpPageContent PanGlossPage()
        {
            var entry = _catalog.Find(HelpEntryKind.Guide, "pangloss")
                ?? throw new InvalidDataException("The PanGloss guide page is missing from the Help catalog.");
            return EntryPage(entry);
        }

        public HelpPageContent WritingSystemsPage()
        {
            var entry = _catalog.Find(HelpEntryKind.Guide, "writing-systems")
                ?? throw new InvalidDataException("The writing systems guide page is missing from the Help catalog.");
            return EntryPage(entry);
        }

        public bool TryResolveLink(Uri uri, out HelpPageContent page)
        {
            var kind = uri.Scheme switch
            {
                "cmd" => HelpEntryKind.Command,
                "term" => HelpEntryKind.Term,
                "ui" => HelpEntryKind.Ui,
                "guide" => HelpEntryKind.Guide,
                _ => (HelpEntryKind?)null,
            };
            if (kind is null)
            {
                page = null!;
                return false;
            }

            var colon = uri.OriginalString.IndexOf(':');
            var code = Uri.UnescapeDataString(uri.OriginalString[(colon + 1)..]);
            if (_catalog.Find(kind.Value, code) is not { } entry)
            {
                page = null!;
                return false;
            }

            page = EntryPage(entry);
            return true;
        }

        private HelpPageContent EntryPage(HelpEntry entry) => new(
            entry.Title,
            entry.Description,
            PrepareMarkdown(entry.HelpPage ?? string.Empty),
            entry.Url);

        private HelpPageContent GuidePage(WorkspacePage page)
        {
            var code = GuideCode(page);
            var entry = _catalog.Find(HelpEntryKind.Guide, code)
                ?? throw new InvalidDataException($"The guide page '{code}' is missing from the Help catalog.");
            return EntryPage(entry);
        }

        private static string PrepareMarkdown(string markdown) =>
            ShotImage.Replace(OpeningHeading.Replace(markdown, string.Empty), "${alt}");

        private static string PageCode(WorkspacePage page) => page switch
        {
            WorkspacePage.Overview => "OverviewPage",
            WorkspacePage.Texts => "TextsPage",
            WorkspacePage.TryAWord => "TryAWordPage",
            WorkspacePage.Timing => "TimingPage",
            WorkspacePage.Warnings => "WarningsPage",
            WorkspacePage.Review => "ReviewPage",
            WorkspacePage.AiHandoff => "AiHandoffPage",
            _ => throw new ArgumentOutOfRangeException(nameof(page)),
        };

        private static string GuideCode(WorkspacePage page) => page switch
        {
            WorkspacePage.Overview => "overview",
            WorkspacePage.Texts => "texts",
            WorkspacePage.TryAWord => "try-a-word",
            WorkspacePage.Timing => "timing",
            WorkspacePage.Warnings => "warnings",
            WorkspacePage.Review => "review-changes",
            WorkspacePage.AiHandoff => "ai-handoff",
            _ => throw new ArgumentOutOfRangeException(nameof(page)),
        };
    }
}
