using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ProposalNumberEmphasisTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedTrialFiguresAreBoldInTheWideTableAndFocusedInspector(bool dark)
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var fake = new FakeCommandClient();
            ProposalsCaptureFixture.Configure(fake);
            var (workspace, window) = await ReviewScreenshots.OpenAsync(fake);
            var previousTheme = Application.Current!.RequestedThemeVariant;
            try
            {
                Application.Current.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                workspace.CurrentPage = WorkspacePage.Proposals;
                window.Width = 1920;
                window.Height = 1080;
                PageScreenshots.Settle(window);
                var model = workspace.PageModel<ProposalsPageModel>();
                var card = Assert.IsType<ProposalCardViewModel>(model.SelectedProposal);
                Assert.Contains(card.Numbers, number => number.Moved);
                Assert.Contains(card.Numbers, number => !number.Moved);
                var page = window.PageView<ProposalsPage>();
                var list = page.FindControl<ListBox>("ProposalsRowsItems")!;
                var row = Assert.IsType<ListBoxItem>(list.ContainerFromItem(card));
                var measures = new[]
                {
                    (card.ApprovedKept!, card.TableApprovedKeptText),
                    (card.Coverage!, card.TableCoverageText),
                    (card.Warnings!, card.TableWarningsText),
                    (card.WordTime!, card.TableWordTimeText),
                };
                var failures = new List<string>();
                Capture(window, dark, "wide");
                foreach (var (number, value) in measures)
                {
                    var text = Assert.Single(row.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == value);
                    CheckEmphasis(text, number, failures);
                }
                var settings = SettingsTestAccess.Model(window);
                settings.ZoomPercent = 150;
                window.Width = 640;
                window.Height = 600;
                PageScreenshots.Settle(window);
                var host = window.FindControl<WorkspaceLayoutHost>("WorkspaceLayout")!;
                HeadlessClick.Click(window, host.FindControl<Button>("InspectorStrip")!, "Show Inspector");
                PageScreenshots.Settle(window);
                var inspector = host.FindControl<ContentControl>("PageInspectorHost")!;
                Capture(window, dark, "focused");
                foreach (var number in card.Numbers)
                {
                    var text = Assert.Single(inspector.GetVisualDescendants().OfType<CopyableTextBlock>(),
                        text => ReferenceEquals(text.DataContext, number) && text.Text == number.Value);
                    CheckEmphasis(text, number, failures);
                }
                LayoutAssertions.AssertCurrent(window);
                Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
            }
            finally
            {
                Application.Current.RequestedThemeVariant = previousTheme;
                window.Close();
                await workspace.DisposeAsync();
            }
        }, TimeSpan.FromMinutes(2));
    }

    private static void CheckEmphasis(TextBlock text, ProposalNumber number, List<string> failures)
    {
        Assert.True(text.IsEffectivelyVisible);
        Assert.True(text.Bounds.Width > 0 && text.Bounds.Height > 0);
        if ((text.FontWeight == FontWeight.Bold) != number.Moved)
            failures.Add($"{number.Label} ({number.Value}): Moved={number.Moved}, rendered weight={text.FontWeight}.");
    }

    private static void Capture(Window window, bool dark, string state)
    {
        if (Environment.GetEnvironmentVariable(ScreenshotFactAttribute.FolderVariable) is not { } folder) return;
        Directory.CreateDirectory(folder);
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Proposals did not render.");
        frame.Save(Path.Combine(folder, $"proposals-number-emphasis-{state}-{(dark ? "dark" : "light")}.png"),
            PngBitmapEncoderOptions.Default);
    }
}
