using Avalonia.Headless;
using Avalonia.Interactivity;
using LiveMarkdown.Avalonia;
using SIL.Motif.App.Services;
using SIL.Motif.App.ViewModels;
using SIL.Motif.Help;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class HelpPopupViewModelTests
{
    private static readonly RoutedEvent<LinkClickedEventArgs> TestLinkClickEvent =
        RoutedEvent.Register<Link, LinkClickedEventArgs>("TestLinkClick", RoutingStrategies.Bubble);
    private readonly AvaloniaHeadlessFixture _avalonia;

    public HelpPopupViewModelTests(AvaloniaHeadlessFixture avalonia) => _avalonia = avalonia;

    [Fact]
    public void CmdLinkNavigatesToTheCataloguedCommand()
    {
        _avalonia.Invoke(() =>
        {
            var launcher = new RecordingUriLauncher();
            var catalog = HelpCatalog.Load();
            var viewModel = new HelpPopupViewModel(launcher, catalog);
            viewModel.ShowForPage(WorkspacePage.Timing);
            var link = new Link();
            var args = new LinkClickedEventArgs(TestLinkClickEvent, link, new Uri("cmd:assess"));

            viewModel.LinkCommand.Execute(args);

            var entry = Assert.IsType<HelpEntry>(catalog.Find(HelpEntryKind.Command, "assess"));
            Assert.Equal(entry.Title, viewModel.Title);
            Assert.Equal(entry.Description, viewModel.Description);
            Assert.Equal(entry.Url, viewModel.OnlineUrl);
            Assert.Empty(launcher.Launches);
        });
    }

    [Fact]
    public void GuideLinkNavigatesToTheCataloguedNestedGuide()
    {
        _avalonia.Invoke(() =>
        {
            var launcher = new RecordingUriLauncher();
            var catalog = HelpCatalog.Load();
            var viewModel = new HelpPopupViewModel(launcher, catalog);
            var link = new Link();
            var args = new LinkClickedEventArgs(
                TestLinkClickEvent, link, new Uri("guide:agents/start-here"));

            viewModel.LinkCommand.Execute(args);

            var entry = Assert.Single(catalog.Entries, candidate =>
                candidate.Kind == HelpEntryKind.Guide && candidate.Code == "agents/start-here");
            Assert.Equal(entry.Title, viewModel.Title);
            Assert.Equal(entry.Description, viewModel.Description);
            Assert.Equal(entry.Url, viewModel.OnlineUrl);
            Assert.Contains("Call Motif as", viewModel.Markdown);
            Assert.Empty(launcher.Launches);
        });
    }

    [Fact]
    public void OpenOnlineLaunchesTheGuideUrlForTheCurrentPage()
    {
        _avalonia.Invoke(() =>
        {
            var launcher = new RecordingUriLauncher();
            var viewModel = new HelpPopupViewModel(launcher, HelpCatalog.Load());
            viewModel.ShowForPage(WorkspacePage.Timing);

            viewModel.OpenOnlineCommand.Execute(null);

            Assert.Equal(new Uri("https://motif-docs.pages.dev/guide/timing/"), Assert.Single(launcher.Launches));
        });
    }

    [Theory]
    [InlineData(WorkspacePage.Overview)]
    [InlineData(WorkspacePage.Texts)]
    [InlineData(WorkspacePage.TryAWord)]
    [InlineData(WorkspacePage.Timing)]
    [InlineData(WorkspacePage.Warnings)]
    [InlineData(WorkspacePage.Review)]
    [InlineData(WorkspacePage.AiHandoff)]
    public void PageHelpSaysItsTitleAndOpeningOnce(WorkspacePage page)
    {
        _avalonia.Invoke(() =>
        {
            var viewModel = new HelpPopupViewModel(new RecordingUriLauncher(), HelpCatalog.Load());

            viewModel.ShowForPage(page);

            Assert.False(viewModel.ShowsDescription);
            Assert.DoesNotMatch(@"^\s*# ", viewModel.Markdown);
            Assert.DoesNotContain("# " + viewModel.Title + "\n", viewModel.Markdown.ReplaceLineEndings("\n"));
            Assert.StartsWith(OpeningWords(viewModel.Description), StripMarks(viewModel.Markdown.TrimStart()));
        });
    }

    [Fact]
    public void CommandHelpDropsThePageHeadingAndKeepsThePageOpening()
    {
        _avalonia.Invoke(() =>
        {
            var viewModel = new HelpPopupViewModel(new RecordingUriLauncher(), HelpCatalog.Load());

            viewModel.LinkCommand.Execute(new LinkClickedEventArgs(TestLinkClickEvent, new Link(), new Uri("cmd:assess")));

            Assert.Equal("Measure a Selection", viewModel.Title);
            Assert.False(viewModel.ShowsDescription);
            Assert.StartsWith("`assess` sends a Selection", viewModel.Markdown.TrimStart());
            Assert.Contains("## When to use it", viewModel.Markdown);
        });
    }

    [Fact]
    public void TermHelpWithoutAPageShowsItsDescription()
    {
        _avalonia.Invoke(() =>
        {
            var catalog = HelpCatalog.Load();
            var viewModel = new HelpPopupViewModel(new RecordingUriLauncher(), catalog);

            viewModel.LinkCommand.Execute(new LinkClickedEventArgs(TestLinkClickEvent, new Link(), new Uri("term:baseline")));

            var entry = Assert.IsType<HelpEntry>(catalog.Find(HelpEntryKind.Term, "baseline"));
            Assert.Null(entry.HelpPage);
            Assert.Equal(entry.Description, viewModel.Description);
            Assert.True(viewModel.ShowsDescription);
            Assert.Empty(viewModel.Markdown);
        });
    }

    [Fact]
    public void GuideImagesRenderTheirAltText()
    {
        _avalonia.Invoke(() =>
        {
            var viewModel = new HelpPopupViewModel(new RecordingUriLauncher(), HelpCatalog.Load());
            viewModel.ShowForPage(WorkspacePage.Timing);

            Assert.Contains("Slowest words in Timing", viewModel.Markdown);
            Assert.DoesNotContain("shot:timing-slow-words/slowest-words", viewModel.Markdown);
        });
    }

    [Fact]
    public void PanGlossPageExplainsTheWordMarksAndShowsTheirPicture()
    {
        _avalonia.Invoke(() =>
        {
            var viewModel = new HelpPopupViewModel(new RecordingUriLauncher(), HelpCatalog.Load());

            viewModel.ShowPanGlossPage();

            Assert.Equal("PanGloss", viewModel.Title);
            Assert.Equal("On Analyze texts, PanGloss runs the project's grammar on selected texts and compares its readings with what FieldWorks stores.",
                viewModel.Description);
            Assert.Contains("A means Approved", viewModel.Markdown);
            Assert.Contains("U means Unknown", viewModel.Markdown);
            Assert.Contains("D means Disapproved", viewModel.Markdown);
            Assert.Contains("Blue means PanGloss found a new or different reading", viewModel.Markdown);
            Assert.Contains("The compact word strip for geldi", viewModel.Markdown);
            Assert.DoesNotContain("shot:explained-word-card/approved-agrees", viewModel.Markdown);
        });
    }

    private static string OpeningWords(string text) => string.Join(' ', text.Split(' ').Take(6));

    private static string StripMarks(string markdown) =>
        System.Text.RegularExpressions.Regex.Replace(
            System.Text.RegularExpressions.Regex.Replace(markdown, @"\[(?<text>[^\]]+)\]\([^)]+\)", "${text}"),
            @"[*_`]", string.Empty);

    private sealed class RecordingUriLauncher : IUriLauncher
    {
        public List<Uri> Launches { get; } = [];

        public Task<bool> LaunchAsync(Uri uri, CancellationToken cancellationToken = default)
        {
            Launches.Add(uri);
            return Task.FromResult(true);
        }
    }
}
