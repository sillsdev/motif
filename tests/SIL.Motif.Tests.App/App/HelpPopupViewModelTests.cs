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
    public void PanGlossPageExplainsTheSupportedGrammarsInLinguistLanguage()
    {
        _avalonia.Invoke(() =>
        {
            var viewModel = new HelpPopupViewModel(new RecordingUriLauncher(), HelpCatalog.Load());

            viewModel.ShowPanGlossPage();

            Assert.Equal("PanGloss", viewModel.Title);
            Assert.Equal("PanGloss parses XAmple and HermitCrab grammars fast. Fully compatible.",
                viewModel.Description);
            Assert.Contains("PanGloss parses XAmple and HermitCrab grammars fast. Fully compatible.",
                viewModel.Markdown);
        });
    }

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
