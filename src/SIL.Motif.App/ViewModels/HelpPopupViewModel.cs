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

    /// <summary>The rendered Markdown source of the current help entry.</summary>
    [ObservableProperty]
    private string _markdown = string.Empty;

    /// <summary>The online documentation address for the current help entry.</summary>
    [ObservableProperty]
    private string _onlineUrl = string.Empty;

    /// <summary>Opens the current help page in the default browser.</summary>
    public IAsyncRelayCommand OpenOnlineCommand { get; }

    /// <summary>Follows a command or term link inside Help, or opens a web link.</summary>
    public IAsyncRelayCommand<LinkClickedEventArgs> LinkCommand { get; }

    /// <summary>Shows the help entry for <paramref name="page"/> or a focused control with catalogued help.</summary>
    /// <param name="page">The selected page.</param>
    /// <param name="focusedAutomationId">The focused control's stable id, when it has one.</param>
    public void ShowForPage(WorkspacePage page, string? focusedAutomationId = null) =>
        SetPage(_content.ForPage(page, focusedAutomationId));

    /// <summary>Shows the short guide to PanGloss from Analyze texts.</summary>
    public void ShowPanGlossPage() => SetPage(_content.PanGlossPage());

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
        OnlineUrl = page.Url;
    }

    private sealed record HelpPageContent(string Title, string Description, string Markdown, string Url);

    private sealed class HelpContentSource
    {
        private static readonly Regex ShotImage = new(
            @"!\[(?<alt>[^\]]*)\]\(shot:[^)]+\)", RegexOptions.CultureInvariant);
        private static readonly Regex MarkdownLink = new(
            @"\[(?<text>[^\]]+)\]\([^)]+\)", RegexOptions.CultureInvariant);
        private static readonly Regex MarkdownMarks = new(@"\*{1,2}|_{1,2}|`", RegexOptions.CultureInvariant);
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
            var markdown = ReadGuide(_catalog.Locale, "pangloss") ?? ReadGuide("en", "pangloss")
                ?? throw new InvalidDataException("The PanGloss guide page is missing from the Help resources.");
            var description = FindDescription(markdown);
            return new HelpPageContent(FindTitle(markdown), description, PrepareMarkdown(markdown),
                BuildGuideUrl(_catalog.Locale, "pangloss"));
        }

        public bool TryResolveLink(Uri uri, out HelpPageContent page)
        {
            var kind = uri.Scheme switch
            {
                "cmd" => HelpEntryKind.Command,
                "term" => HelpEntryKind.Term,
                "ui" => HelpEntryKind.Ui,
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
            var slug = GuideSlug(page);
            var markdown = ReadGuide(_catalog.Locale, slug) ?? ReadGuide("en", slug)
                ?? throw new InvalidDataException($"The guide page '{slug}' is missing from the Help resources.");
            var title = FindTitle(markdown);
            var description = FindDescription(markdown);
            return new HelpPageContent(title, description, PrepareMarkdown(markdown), BuildGuideUrl(_catalog.Locale, slug));
        }

        private string? ReadGuide(string locale, string slug)
        {
            var resourceName = $"help/{locale}/guide/{slug}.md";
            var assembly = typeof(HelpCatalog).Assembly;
            var name = assembly.GetManifestResourceNames().FirstOrDefault(candidate =>
                string.Equals(candidate.Replace('\\', '/'), resourceName, StringComparison.Ordinal));
            if (name is null) return null;
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static string FindTitle(string markdown) =>
            markdown.Split('\n').Select(line => line.TrimEnd('\r'))
                .FirstOrDefault(line => line.StartsWith("# ", StringComparison.Ordinal))?[2..].Trim()
            ?? throw new InvalidDataException("A guide page must start with a title heading.");

        private static string FindDescription(string markdown)
        {
            var lines = markdown.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
            var titleIndex = Array.FindIndex(lines, line => line.StartsWith("# ", StringComparison.Ordinal));
            var paragraph = lines.Skip(titleIndex + 1).SkipWhile(string.IsNullOrWhiteSpace)
                .TakeWhile(line => !string.IsNullOrWhiteSpace(line));
            var text = string.Join(" ", paragraph).Trim();
            text = MarkdownLink.Replace(text, "${text}");
            return MarkdownMarks.Replace(text, string.Empty);
        }

        private static string PrepareMarkdown(string markdown) =>
            ShotImage.Replace(markdown, "${alt}");

        private static string BuildGuideUrl(string locale, string slug) =>
            HelpCatalog.SiteRoot + (locale == "en" ? string.Empty : "/" + locale) + "/guide/" + slug + "/";

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

        private static string GuideSlug(WorkspacePage page) => page switch
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
