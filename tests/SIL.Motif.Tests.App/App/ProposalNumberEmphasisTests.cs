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
    public void TrialFiguresRemainAvailableWithoutTheRetiredInspector(bool dark)
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
                window.Width = 2400;
                window.Height = 1080;
                PageScreenshots.Settle(window);
                var model = workspace.PageModel<ProposalsPageModel>();
                var card = Assert.IsType<ProposalCardViewModel>(model.SelectedProposal);
                Assert.Contains(card.Numbers, number => number.Moved);
                Assert.Contains(card.Numbers, number => !number.Moved);
                Assert.NotNull(card.ApprovedKept);
                Assert.NotNull(card.Coverage);
                Assert.NotNull(card.Warnings);
                Assert.NotNull(card.Work);
                var host = window.FindInWindowOrWorkspace<WorkspaceLayoutHost>("WorkspaceLayout")!;
                Assert.False(host.FindControl<Border>("InspectorPane")!.IsVisible);
                LayoutAssertions.AssertCurrent(window);
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
        Assert.True(text.IsEffectivelyVisible, $"{number.Label} ({text.Text}) is hidden; parent: {text.Parent}.");
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
