using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.Controls;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class ProposalsResponsiveScreenshots
{
    private const string CaptureStageVariable = "MOTIF_PROPOSALS_CAPTURE_STAGE";

    private static readonly (string Name, double Width, double Height)[] Sizes =
    [
        ("thin640x600", 640, 600),
        ("half960x780", 960, 780),
        ("full1920x1080", 1920, 1080),
        ("short960x600", 960, 600),
    ];

    private static readonly (string Name, ThemeVariant Variant)[] Themes =
    [
        ("light", ThemeVariant.Light),
        ("dark", ThemeVariant.Dark),
    ];

    private static readonly (int Percent, string Name)[] Zooms =
    [
        (100, "100"),
        (150, "150"),
    ];

    [ScreenshotFact]
    public void CaptureProposalsAutomaticPaneAtFourSizesBothThemesAndZooms()
    {
        var folder = Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable)!;
        var stage = Environment.GetEnvironmentVariable(CaptureStageVariable) switch
        {
            "before" => "before",
            "after" => "after",
            _ => throw new InvalidOperationException($"Set {CaptureStageVariable} to before or after."),
        };
        Directory.CreateDirectory(folder);
        var states = new List<string>();
        var layoutFailures = new List<string>();

        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            using var culture = new CultureScope(CultureInfo.GetCultureInfo("en-US"));
            var fake = new FakeCommandClient();
            ProposalsCaptureFixture.Configure(fake);
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            try
            {
                var model = workspace.PageModel<ProposalsPageModel>();
                await model.ShowSetAsideCommand.ExecuteAsync(null);
                workspace.CurrentPage = WorkspacePage.Proposals;
                var page = window.PageView<ProposalsPage>();
                var host = window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout")!;
                PageScreenshots.Settle(window);

                Assert.Equal(ProposalsCaptureFixture.SelectedProposalId, model.SelectedProposal?.ProposalId);
                Assert.Equal(3, model.ListGroups.Single(group => group.Filter == ProposalListFilter.WaitingForYou).Cards.Count);
                Assert.Equal(4, model.ListGroups.Single(group => group.Filter == ProposalListFilter.Closed).Cards.Count);
                var provider = Assert.IsAssignableFrom<IWorkspacePaneProvider>(page);
                var inspector = Assert.IsAssignableFrom<Control>(provider.InspectorContent);
                Assert.Contains(inspector.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Classes.Contains("inspectorTitle") && text.Text == "Nasal assimilation before /p/");
                Assert.Contains(inspector.GetVisualDescendants().OfType<CopyableTextBlock>(), text =>
                    text.Text == "41 of 52 → 43 of 52");

                var settings = SettingsTestAccess.Model(window);
                foreach (var (zoom, zoomName) in Zooms)
                {
                    settings.ZoomPercent = zoom;
                    foreach (var (themeName, theme) in Themes)
                    {
                        Application.Current!.RequestedThemeVariant = theme;
                        foreach (var (sizeName, width, height) in Sizes)
                        {
                            window.Width = width;
                            window.Height = height;
                            PageScreenshots.Settle(window);
                            var file = $"proposals-workspace-automatic-{stage}-{sizeName}-{themeName}-{zoomName}.png";
                            var exactClientSize = new Size(width, height);
                            var clientSizeExact = true;
                            try
                            {
                                Assert.Equal(exactClientSize, window.ClientSize);
                            }
                            catch (Xunit.Sdk.XunitException)
                            {
                                clientSizeExact = false;
                                layoutFailures.Add($"{file}: expected client {exactClientSize}, got {window.ClientSize}.");
                            }

                            Assert.Equal(ProposalsCaptureFixture.SelectedProposalId, model.SelectedProposal?.ProposalId);
                            Save(window, Path.Combine(folder, file));
                            var inspectorVisible = host.FindControl<Border>("InspectorPane")!.IsVisible &&
                                inspector.GetVisualDescendants().OfType<CopyableTextBlock>().Any(text =>
                                    text.IsEffectivelyVisible && text.Classes.Contains("inspectorTitle") &&
                                    text.Text == "Nasal assimilation before /p/");
                            try
                            {
                                var list = page.FindControl<ListBox>("ProposalsRowsItems")!;
                                var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(model.SelectedProposal!));
                                var tracks = Assert.Single(row.GetVisualDescendants().OfType<Grid>(),
                                    grid => DenseTable.GetColumns(grid) is not null);
                                var minimumIdentityWidth = Assert.IsType<double>(Application.Current.FindResource(
                                    "Component.DenseTable.Column.NarrowWidth"));
                                Assert.True(tracks.ColumnDefinitions[0].ActualWidth >= minimumIdentityWidth,
                                    $"Proposal identity has {tracks.ColumnDefinitions[0].ActualWidth:0.#} DIP; " +
                                    $"at least {minimumIdentityWidth:0.#} DIP is needed.");
                                if (sizeName == "full1920x1080" && zoom == 100)
                                    foreach (var heading in new[] { "Built by", "Coverage", "Warnings", "Word time" })
                                        Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), text =>
                                            text.Classes.Contains("denseTableHeading") && text.Text == heading &&
                                            text.IsEffectivelyVisible);
                                LayoutAssertions.AssertCurrent(window);
                                states.Add($"{file}\tMainWindow WorkspaceLayout, {sizeName}, {themeName}, " +
                                    $"{zoom}% zoom; client={window.ClientSize}; exact client={clientSizeExact}; " +
                                    $"automatic Inspector visible={inspectorVisible}; geometry passed.");
                            }
                            catch (Xunit.Sdk.XunitException failure)
                            {
                                var detail = failure.Message.Replace('\r', ' ').Replace('\n', ' ');
                                layoutFailures.Add($"{file}: {detail}");
                                states.Add($"{file}\tMainWindow WorkspaceLayout, {sizeName}, {themeName}, " +
                                    $"{zoom}% zoom; client={window.ClientSize}; exact client={clientSizeExact}; " +
                                    $"automatic Inspector visible={inspectorVisible}; geometry failed: {detail}");
                            }
                        }
                    }
                }
                File.WriteAllLines(Path.Combine(folder, $"proposals-workspace-automatic-{stage}-states.txt"), states);
                Assert.True(layoutFailures.Count == 0, string.Join(Environment.NewLine, layoutFailures));
            }
            finally
            {
                Application.Current!.RequestedThemeVariant = ThemeVariant.Light;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(3));
    }

    private static void Save(Window window, string path)
    {
        PageScreenshots.Settle(window);
        using var frame = window.CaptureRenderedFrame() ??
            throw new InvalidOperationException($"No frame rendered for {path}.");
        frame.Save(path, PngBitmapEncoderOptions.Default);
    }

}
