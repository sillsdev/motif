using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using SIL.Motif.App.Controls;
using SIL.Motif.App.Controls.WordPresentation;
using SIL.Motif.App.Services;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.TextViewParity;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;
using WritingSystemText = SIL.Motif.App.Controls.WritingSystemText;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class Stage3DrawnTests
{
    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void DrawnRunsHaveMirroredColumnsAndNamedPeers(string name) => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var (window, word, sample) = Open(name);
        try
        {
            Assert.Equal(15, word.Runs.Count);
            Assert.Equal(sample.WordText(0), word.Runs[0].Text);
            Assert.Equal(sample.StoredForms(0), new[] { word.Runs[1].Text, word.Runs[3].Text, word.Runs[5].Text });
            var first = word.Runs[1].Bounds;
            var second = word.Runs[3].Bounds;
            Assert.True(sample.RightToLeft ? first.X > second.X : first.X < second.X);
            var peer = ControlAutomationPeer.CreatePeerForElement(word);
            var children = peer.GetChildren();
            Assert.Equal(word.Runs.Select(run => run.Text), children.Select(child => child.GetName()));
            Assert.All(children, child => Assert.True(child.GetBoundingRectangle().Width > 0));
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void ConstituentPeersExposeTheirActualTopLevelBoundsAndFocus(string name) => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var (window, word, _) = Open(name);
        try
        {
            window.Position = new PixelPoint(120, 180);
            Dispatcher.UIThread.RunJobs();
            var peer = ControlAutomationPeer.CreatePeerForElement(word);
            var children = peer.GetChildren();
            Assert.Equal(word.Runs.Count, children.Count);
            for (var i = 0; i < children.Count; i++)
            {
                var child = children[i];
                var bounds = word.Runs[i].Bounds;
                var origin = word.TranslatePoint(bounds.TopLeft, window)!.Value;
                var actual = child.GetBoundingRectangle();
                Assert.Equal(origin.X, actual.X, 0.01);
                Assert.Equal(origin.Y, actual.Y, 0.01);
                Assert.Equal(bounds.Width, actual.Width, 0.01);
                Assert.Equal(bounds.Height, actual.Height, 0.01);
                Assert.Same(peer, child.GetParent());
                child.SetFocus();
                Assert.True(child.HasKeyboardFocus());
            }
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void PartialGlossDragCopiesTheSelectedText(string name) => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var (window, word, _) = Open(name);
        try
        {
            var gloss = word.Runs[14];
            Assert.True(gloss.Text.Length >= 3);
            window.Clipboard!.ClearAsync().GetAwaiter().GetResult();
            var start = word.TranslatePoint(new Point(gloss.Bounds.Left + 1, gloss.Bounds.Center.Y), window)!.Value;
            var end = word.TranslatePoint(new Point(gloss.Bounds.Left + gloss.Bounds.Width * 0.6, gloss.Bounds.Center.Y), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var primary = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            window.KeyPress(Key.C, primary, PhysicalKey.None, null);
            window.KeyRelease(Key.C, primary, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
            var copied = window.Clipboard.TryGetTextAsync().GetAwaiter().GetResult();
            Assert.False(string.IsNullOrEmpty(copied));
            Assert.StartsWith(copied, gloss.Text);
            Assert.True(copied.Length < gloss.Text.Length);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("ltr")]
    [InlineData("rtl")]
    public void PartialWordDragFollowsTheWritingSystemDirection(string name) => AvaloniaHeadlessPlatform.Invoke(() =>
    {
        var (window, word, sample) = Open(name);
        try
        {
            var run = word.Runs[0];
            window.Clipboard!.ClearAsync().GetAwaiter().GetResult();
            var left = run.Bounds.Left + 1;
            var right = run.Bounds.Right - 1;
            var from = sample.RightToLeft ? right : left;
            var to = sample.RightToLeft ? right - (right - left) * 0.6 : left + (right - left) * 0.6;
            var start = word.TranslatePoint(new Point(from, run.Bounds.Center.Y), window)!.Value;
            var end = word.TranslatePoint(new Point(to, run.Bounds.Center.Y), window)!.Value;
            window.MouseDown(start, MouseButton.Left);
            window.MouseMove(end, RawInputModifiers.LeftMouseButton);
            window.MouseUp(end, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            var primary = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
            window.KeyPress(Key.C, primary, PhysicalKey.None, null);
            window.KeyRelease(Key.C, primary, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();
            var copied = window.Clipboard.TryGetTextAsync().GetAwaiter().GetResult();
            Assert.False(string.IsNullOrEmpty(copied));
            Assert.StartsWith(copied, run.Text);
            Assert.True(copied.Length < run.Text.Length);
        }
        finally { window.Close(); }
    });

    private static (MotifTestWindow Window, InterlinearWord Word, TextViewSample Sample) Open(string name)
    {
        WalkthroughFonts.Register();
        var sample = TextViewSample.Named(name);
        var styles = new TextStyles();
        styles.SetFallbackFamilies([WalkthroughFonts.DejaVuSansFamily]);
        WritingSystemDisplay Display(string id, bool rtl) => new(id, id, id, WritingSystemKind.Vernacular, 0, false,
            WalkthroughFonts.DejaVuSansFamily, string.Empty, rtl, new Dictionary<string, double> { ["Normal"] = 12 });
        styles.ReplaceContext([Display(sample.FormWritingSystem, sample.RightToLeft), Display(sample.GlossWritingSystem, false)], "en");
        var word = new InterlinearWord { Data = sample.Token(0) };
        var window = new MotifTestWindow { Content = word, Width = 900, Height = 400 };
        WritingSystemText.SetResolver(window, styles);
        window.Show();
        for (var pass = 0; pass < 3; pass++) { Dispatcher.UIThread.RunJobs(); window.UpdateLayout(); }
        return (window, word, sample);
    }
}
