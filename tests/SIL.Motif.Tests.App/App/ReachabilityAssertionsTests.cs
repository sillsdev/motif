using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using SIL.Motif.App.Views;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class ReachabilityAssertionsTests(AvaloniaHeadlessFixture avalonia)
{
    [Fact]
    public void AnEnabledControlOutsideEveryScrollRouteIsUnreachable()
    {
        avalonia.Invoke(() =>
        {
            var button = new Button { Name = "OffscreenAction", Content = "Continue", Width = 80, Height = 32 };
            Canvas.SetLeft(button, 180);
            Canvas.SetTop(button, 30);
            var window = new Window
            {
                Content = new Canvas { Width = 300, Height = 100, Children = { button } },
                Width = 120,
                Height = 90,
            };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var findings = ReachabilityAssertions.Inspect(window,
                    new LayoutFindingContext("synthetic", "thin", 100, "light"));

                var finding = Assert.Single(findings, item => item.Check == LayoutCheckNames.UnreachableControl);
                Assert.Equal("OffscreenAction", finding.Control.Name);
                Assert.Contains("client area", finding.Detail, StringComparison.Ordinal);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void AZeroAreaControlHasNoRenderedHitTarget()
    {
        avalonia.Invoke(() =>
        {
            var button = new Button { Width = 0, Height = 0 };
            var window = new Window { Content = button, Width = 120, Height = 90 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                Assert.True(ReachabilityAssertions.IsReachable(button, window));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void AControlInsideAReachableHoverRevealRowIsReachable()
    {
        avalonia.Invoke(() =>
        {
            var button = new Button { Name = "RevealAction", Content = "Inspect", Width = 60, Height = 28 };
            Canvas.SetLeft(button, 150);
            var revealHost = new Canvas { Width = 50, Height = 32, Classes = { "hoverReveal" }, Children = { button } };
            var canvas = new Canvas { Width = 80, Height = 50, Children = { revealHost } };
            var window = new Window { Content = canvas, Width = 100, Height = 80 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                Assert.True(ReachabilityAssertions.IsReachable(revealHost, window));
                Assert.True(ReachabilityAssertions.IsReachable(button, window));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void SelectableTextIsNotAnInteractiveControl()
    {
        var text = new CopyableTextBlock { Text = "Select this text" };

        Assert.False(ReachabilityAssertions.IsInteractive(text));
    }

    [Theory]
    [InlineData(ScrollBarVisibility.Auto, true)]
    [InlineData(ScrollBarVisibility.Disabled, false)]
    public void AScrollViewerOnlyMakesItsContentReachableWhenBothAxesCanRevealIt(
        ScrollBarVisibility visibility, bool expectedReachable)
    {
        avalonia.Invoke(() =>
        {
            var action = new Button { Content = "Continue", Width = 72, Height = 32 };
            Canvas.SetLeft(action, 210);
            Canvas.SetTop(action, 210);
            var canvas = new Canvas { Width = 320, Height = 320, Children = { action } };
            var scroll = new ScrollViewer
            {
                Width = 110,
                Height = 110,
                HorizontalScrollBarVisibility = visibility,
                VerticalScrollBarVisibility = visibility,
                Content = canvas,
            };
            var window = new Window { Content = scroll, Width = 140, Height = 140 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var reachable = ReachabilityAssertions.IsReachable(action, window);
                Assert.True(reachable == expectedReachable,
                    $"Expected reachable={expectedReachable}, found {reachable}; action={action.TranslatePoint(default, canvas)}, " +
                    $"extent={scroll.Extent}, viewport={scroll.Viewport}, offset={scroll.Offset}, " +
                    $"bars={scroll.HorizontalScrollBarVisibility}/{scroll.VerticalScrollBarVisibility}.");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void AControlAtTheBottomOfScrolledContentWithAMarginIsReachable()
    {
        avalonia.Invoke(() =>
        {
            var action = new Button { Content = "Continue", Width = 72, Height = 32 };
            Canvas.SetLeft(action, 10);
            Canvas.SetTop(action, 280);
            var canvas = new Canvas
            {
                Width = 100, Height = 320, Margin = new Thickness(0, 100, 0, 0), Children = { action },
            };
            var scroll = new ScrollViewer
            {
                Width = 110,
                Height = 110,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = canvas,
            };
            var window = new Window { Content = scroll, Width = 140, Height = 140 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                Assert.Equal(420, scroll.Extent.Height, 1);
                Assert.True(ReachabilityAssertions.IsReachable(action, window),
                    $"The scroll extent {scroll.Extent} includes the content's margin, so the bottom action is reachable.");
                Assert.Equal(default, scroll.Offset);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void AListBoxRowBelowItsViewportIsReachableByScrolling()
    {
        avalonia.Invoke(() =>
        {
            var list = new ListBox
            {
                Width = 150,
                Height = 60,
                ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel()),
                Items =
                {
                    new ListBoxItem { Content = new Button { Content = "First", Height = 32 } },
                    new ListBoxItem { Content = new Button { Content = "Second", Height = 32 } },
                    new ListBoxItem { Content = new Button { Content = "Third", Height = 32 } },
                    new ListBoxItem { Content = new Button { Content = "Below the fold", Height = 32 } },
                },
            };
            var outer = new ScrollViewer
            {
                Width = 150,
                Height = 60,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new Grid { Width = 300, Height = 60, Children = { list } },
            };
            var window = new Window { Content = outer, Width = 180, Height = 100 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var viewer = Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>());
                var button = Assert.Single(list.GetVisualDescendants().OfType<Button>(),
                    item => Equals(item.Content, "Below the fold"));
                Assert.True(viewer.Extent.Height > viewer.Viewport.Height + ReachabilityAssertions.Tolerance);
                Assert.True(outer.Extent.Width > outer.Viewport.Width + ReachabilityAssertions.Tolerance);
                Assert.True(ReachabilityAssertions.IsReachable(button, window));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void AControlBelowAFixedInnerViewportIsUnreachableEvenWhenTheOuterViewerScrolls() =>
        WithNestedViewers(innerTop: 0, buttonTop: 160, ScrollBarVisibility.Disabled, (window, button, inner, outer) =>
        {
            var viewport = Assert.Single(inner.GetVisualDescendants().OfType<ScrollContentPresenter>());
            Assert.True(button.TranslatePoint(default, viewport)!.Value.Y >= viewport.Bounds.Height);
            Assert.False(ReachabilityAssertions.IsReachable(button, window));
        });

    [Fact]
    public void AControlBelowAnInnerViewportIsReachableWhenThatViewerScrolls() =>
        WithNestedViewers(innerTop: 0, buttonTop: 160, ScrollBarVisibility.Auto, (window, button, inner, outer) =>
            Assert.True(ReachabilityAssertions.IsReachable(button, window)));

    [Fact]
    public void AControlInsideAFixedInnerViewportIsReachableWhenTheOuterViewerScrollsItIn() =>
        WithNestedViewers(innerTop: 180, buttonTop: 10, ScrollBarVisibility.Disabled, (window, button, inner, outer) =>
        {
            Assert.True(inner.TranslatePoint(default, outer)!.Value.Y >= outer.Viewport.Height);
            Assert.True(ReachabilityAssertions.IsReachable(button, window));
        });

    private void WithNestedViewers(double innerTop, double buttonTop, ScrollBarVisibility innerScrolling,
        Action<Window, Button, ScrollViewer, ScrollViewer> check)
    {
        avalonia.Invoke(() =>
        {
            var button = new Button { Name = "InnerClippedAction", Content = "Continue", Width = 72, Height = 32 };
            Canvas.SetTop(button, buttonTop);
            var inner = new ScrollViewer
            {
                Width = 110, Height = 60,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = innerScrolling,
                // Top-aligned, so a viewport that cannot scroll shows the canvas from its top rather than centred.
                Content = new Canvas
                {
                    Width = 110, Height = 200, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                    Children = { button },
                },
            };
            Canvas.SetTop(inner, innerTop);
            var outer = new ScrollViewer
            {
                Width = 150, Height = 90,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Canvas { Width = 110, Height = 400, Children = { inner } },
            };
            var window = new Window { Content = outer, Width = 180, Height = 120 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                Assert.True(outer.Extent.Height > outer.Viewport.Height + ReachabilityAssertions.Tolerance);
                check(window, button, inner, outer);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void AnOpenFlyoutUsesItsOwnClientBounds()
    {
        avalonia.Invoke(() =>
        {
            var behindText = new TextBlock
            {
                Name = "BehindPopupText", Text = "under popup that cannot fit in this tiny text box", Width = 48,
                TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.None,
            };
            Canvas.SetLeft(behindText, 40);
            Canvas.SetTop(behindText, 30);
            var popupText = new TextBlock { Text = "popup text" };
            var popupButton = new Button { Name = "FlyoutAction", Content = "Open" };
            var anchor = new Button
            {
                Name = "FlyoutAnchor", Width = 10, Height = 10,
                Flyout = new Flyout
                {
                    Content = new Border
                    {
                        Width = 120,
                        Height = 70,
                        Child = new StackPanel { Children = { popupButton, popupText } },
                    },
                },
            };
            var window = new Window
            {
                Content = new Canvas { Width = 200, Height = 100, Children = { behindText, anchor } },
                Width = 220, Height = 120,
            };
            try
            {
                window.Show();
                anchor.Flyout!.ShowAt(anchor);
                PageScreenshots.Settle(window);

                var findings = ReachabilityAssertions.Inspect(window,
                    new LayoutFindingContext("synthetic", "thin", 100, "light"));

                Assert.DoesNotContain(findings, finding =>
                    finding.Check == LayoutCheckNames.UnreachableControl && finding.Control.Name == "FlyoutAction");
                Assert.DoesNotContain(findings, finding => finding.Check == LayoutCheckNames.OverlappingText &&
                    finding.Control.Name == "BehindPopupText");
                Assert.DoesNotContain(findings, finding => finding.Check == LayoutCheckNames.TextBoxOverflow &&
                    finding.Control.Name == "BehindPopupText");
                Assert.DoesNotContain(findings, finding =>
                    finding.Check == LayoutCheckNames.PopupOutsideWindow);
            }
            finally
            {
                anchor.Flyout?.Hide();
                window.Close();
            }
        });
    }

    [Fact]
    public void AnOpenFlyoutOutsideTheWindowIsReportedWithAStableCheckName()
    {
        avalonia.Invoke(() =>
        {
            var topBar = new Border { Name = "TopBar", Height = 18 };
            var flyoutAction = new Button { Name = "FlyoutAction", Content = "Continue" };
            var anchor = new Button
            {
                Name = "FlyoutAnchor", Content = "Open",
                Flyout = new Flyout { Content = new Border { Width = 180, Height = 36, Child = flyoutAction } },
            };
            var body = new StackPanel { Children = { anchor } };
            var window = new Window
            {
                Content = new Grid { RowDefinitions = { new RowDefinition(GridLength.Auto), new RowDefinition(1, GridUnitType.Star) },
                    Children = { topBar, body } },
                Width = 100, Height = 80,
            };
            Grid.SetRow(body, 1);
            try
            {
                window.Show();
                anchor.Flyout!.ShowAt(anchor);
                PageScreenshots.Settle(window);

                var findings = ReachabilityAssertions.Inspect(window,
                    new LayoutFindingContext("synthetic", "half", 150, "light"));

                Assert.Contains(findings, finding => finding.Check == LayoutCheckNames.PopupOutsideWindow);
            }
            finally
            {
                anchor.Flyout?.Hide();
                window.Close();
            }
        });
    }

    [Fact]
    public void TextBehindTheSetupModalScrimIsNotReportedAsOverlappingDialogText()
    {
        avalonia.Invoke(() =>
        {
            var pageText = new TextBlock
            {
                Name = "PageText", Text = "Page behind setup that cannot fit in this tiny text box", Width = 48,
                TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.None,
            };
            var dialog = new SetupDialog { IsVisible = true };
            var dialogContent = Assert.IsType<Grid>(dialog.Content);
            var dialogText = new TextBlock { Name = "DialogText", Text = "Setup dialog" };
            dialogContent.Children.Add(dialogText);
            var unreachableAction = new Button { Name = "ModalOffscreenAction", Content = "Continue" };
            Canvas.SetLeft(unreachableAction, 500);
            dialogContent.Children.Add(new Canvas { Children = { unreachableAction } });
            var window = new Window
            {
                Content = new Grid { Children = { pageText, dialog } }, Width = 240, Height = 120,
            };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var findings = ReachabilityAssertions.Inspect(window,
                    new LayoutFindingContext("synthetic", "thin", 100, "light"));

                Assert.DoesNotContain(findings, finding => finding.Check == LayoutCheckNames.OverlappingText &&
                    (finding.Control.Name == "PageText" || finding.RelatedControl?.Name == "PageText"));
                Assert.DoesNotContain(findings, finding => finding.Check == LayoutCheckNames.TextBoxOverflow &&
                    finding.Control.Name == "PageText");
                Assert.Contains(findings, finding => finding.Check == LayoutCheckNames.UnreachableControl &&
                    finding.Control.Name == "ModalOffscreenAction");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void NoWrapTextThatExceedsItsArrangedBoxIsReported()
    {
        avalonia.Invoke(() =>
        {
            var text = new TextBlock
            {
                Name = "NarrowLabel", Text = "Words in Selection places", Width = 48,
                TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.None,
            };
            var window = new Window { Content = text, Width = 100, Height = 60 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var finding = Assert.Single(ReachabilityAssertions.Inspect(window,
                    new LayoutFindingContext("synthetic", "half", 150, "light")),
                    item => item.Check == LayoutCheckNames.TextBoxOverflow);
                Assert.Contains("needs", finding.Detail, StringComparison.Ordinal);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void FindingsForRepeatedControlPairsCollapseAndKeepTheirCount()
    {
        var repeated = new LayoutFinding("synthetic", "thin", 100, "light", LayoutCheckNames.OverlappingText,
            new ControlDescription("TextBlock", null, null, "one row", "cell"),
            new LayoutBounds(10, 10, 20, 10), "TextBlock##.cell|TextBlock##.cell", "first text",
            new ControlDescription("TextBlock", null, null, "neighbor one", "cell"));
        var second = repeated with
        {
            Control = repeated.Control with { AutomationName = "another row" },
            RelatedControl = repeated.RelatedControl! with { AutomationName = "neighbor two" },
            Bounds = new LayoutBounds(10, 25, 20, 10),
        };

        var finding = Assert.Single(ReachabilityAssertions.CollapseDuplicates([repeated, second]));

        Assert.Equal(2, finding.Count);
    }

    [Fact]
    public void NavItemsAreReachableWhenTheListBoxCanScrollItsVirtualizedExtent()
    {
        avalonia.Invoke(() =>
        {
            var list = new ListBox
            {
                Name = "PageList", Width = 150, Height = 60,
                ItemsSource = Enumerable.Range(0, 20).Select(index => $"Page {index}"),
            };
            var window = new Window { Content = list, Width = 180, Height = 100 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var finding = ReachabilityAssertions.NavCompletenessFindings(window,
                    new LayoutFindingContext("synthetic", "short", 100, "light"), window);

                Assert.Empty(finding);
                var viewer = Assert.Single(list.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.True(viewer.Extent.Height > viewer.Viewport.Height + ReachabilityAssertions.Tolerance);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void AHiddenNavItemWithoutAWorkingScrollRouteIsReported()
    {
        avalonia.Invoke(() =>
        {
            var list = new ListBox
            {
                Name = "PageList", Width = 150, Height = 45,
                ItemsSource = Enumerable.Range(0, 20).Select(index => $"Page {index}"),
            };
            ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            var window = new Window { Content = list, Width = 180, Height = 90 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var findings = ReachabilityAssertions.NavCompletenessFindings(window,
                    new LayoutFindingContext("synthetic", "short", 100, "light"), window);

                Assert.Contains(findings, finding => finding.Check == LayoutCheckNames.NavItemHidden);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void PageRootHorizontalExtentAndOverflowAreReported()
    {
        avalonia.Invoke(() =>
        {
            var content = new Canvas { Width = 180, Height = 40 };
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content,
            };
            var page = new Border { Child = scroll };
            var pageHost = new Panel { Name = "PageHost", Children = { page } };
            var window = new Window { Content = pageHost, Width = 100, Height = 70 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var findings = ReachabilityAssertions.PageHorizontalScrollFindings(window,
                    new LayoutFindingContext("synthetic", "thin", 100, "light"), window);

                Assert.Contains(findings, finding => finding.Check == LayoutCheckNames.PageHorizontalScroll &&
                    finding.Control.Type == nameof(ScrollViewer));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void AHiddenTabDoesNotReportItsRetainedHorizontalExtent()
    {
        avalonia.Invoke(() =>
        {
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Canvas { Width = 180, Height = 40 },
            };
            var tab = new Border { Child = scroll };
            var page = new Grid { Children = { tab } };
            var pageHost = new Panel { Name = "PageHost", Children = { page } };
            var window = new Window { Content = pageHost, Width = 100, Height = 70 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var context = new LayoutFindingContext("synthetic", "thin", 100, "light");
                Assert.NotEmpty(ReachabilityAssertions.PageHorizontalScrollFindings(window, context, window));

                tab.IsVisible = false;
                PageScreenshots.Settle(window);

                Assert.Empty(ReachabilityAssertions.PageHorizontalScrollFindings(window, context, window));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void PageRootContentThatFitsHasNoHorizontalOverflowFinding()
    {
        avalonia.Invoke(() =>
        {
            var content = new Canvas { Width = 60, Height = 40 };
            var scroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = content,
            };
            var pageHost = new Panel { Name = "PageHost", Children = { new Border { Child = scroll } } };
            var window = new Window { Content = pageHost, Width = 100, Height = 70 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                Assert.DoesNotContain(ReachabilityAssertions.PageHorizontalScrollFindings(window,
                    new LayoutFindingContext("synthetic", "thin", 100, "light"), window),
                    finding => finding.Check == LayoutCheckNames.PageHorizontalScroll);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void PageContentExtendingBelowBodyDoesNotReportHorizontalOverflow()
    {
        avalonia.Invoke(() =>
        {
            var scroll = new ScrollViewer
            {
                Width = 80,
                Height = 40,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new Canvas { Width = 60, Height = 90 },
            };
            var pageHost = new Panel
            {
                Name = "PageHost",
                Width = 100,
                Height = 60,
                Children = { new Border { Child = scroll } },
            };
            var window = new Window { Content = pageHost, Width = 120, Height = 90 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                Assert.True(scroll.Extent.Height > scroll.Viewport.Height + ReachabilityAssertions.Tolerance);
                Assert.DoesNotContain(ReachabilityAssertions.PageHorizontalScrollFindings(window,
                    new LayoutFindingContext("synthetic", "short", 100, "light"), window),
                    finding => finding.Check == LayoutCheckNames.PageHorizontalScroll);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void TextBoxHorizontalScrollingDoesNotReportPageHorizontalOverflow()
    {
        avalonia.Invoke(() =>
        {
            var textBox = new TextBox
            {
                Width = 50,
                Height = 30,
                Text = new string('x', 100),
                TextWrapping = TextWrapping.NoWrap,
            };
            var pageHost = new Panel { Name = "PageHost", Width = 100, Height = 60, Children = { textBox } };
            var window = new Window { Content = pageHost, Width = 120, Height = 90 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var textScroll = Assert.Single(textBox.GetVisualDescendants().OfType<ScrollViewer>());
                Assert.True(textScroll.Extent.Width > textScroll.Viewport.Width + ReachabilityAssertions.Tolerance);
                Assert.DoesNotContain(ReachabilityAssertions.PageHorizontalScrollFindings(window,
                    new LayoutFindingContext("synthetic", "short", 100, "light"), window),
                    finding => finding.Check == LayoutCheckNames.PageHorizontalScroll);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void AListItemScrollViewerIsNotMistakenForPageRootHorizontalScroll()
    {
        avalonia.Invoke(() =>
        {
            var cellScroll = new ScrollViewer
            {
                Width = 50,
                Height = 25,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new Canvas { Width = 70, Height = 20 },
            };
            var list = new ListBox
            {
                Width = 100,
                Height = 60,
                Items = { new ListBoxItem { Content = cellScroll } },
            };
            var pageHost = new Panel { Name = "PageHost", Width = 100, Height = 60, Children = { list } };
            var window = new Window { Content = pageHost, Width = 120, Height = 90 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                Assert.True(cellScroll.Extent.Width > cellScroll.Viewport.Width + ReachabilityAssertions.Tolerance);
                Assert.DoesNotContain(ReachabilityAssertions.PageHorizontalScrollFindings(window,
                    new LayoutFindingContext("synthetic", "thin", 100, "light"), window),
                    finding => finding.Check == LayoutCheckNames.PageHorizontalScroll);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void AcceptanceCheckNamesRemainStable()
    {
        Assert.Equal("popup-outside-window", LayoutCheckNames.PopupOutsideWindow);
        Assert.Equal("nav-item-hidden", LayoutCheckNames.NavItemHidden);
        Assert.Equal("page-horizontal-scroll", LayoutCheckNames.PageHorizontalScroll);
    }

    [Fact]
    public void CaptureSelectionAcceptsThe150ZoomAndDarkThemeAxes()
    {
        var previous = new[]
        {
            Environment.GetEnvironmentVariable("MOTIF_CAPTURE_MATRIX"),
            Environment.GetEnvironmentVariable("MOTIF_CAPTURE_SCENES"),
            Environment.GetEnvironmentVariable("MOTIF_CAPTURE_SIZES"),
            Environment.GetEnvironmentVariable("MOTIF_CAPTURE_ZOOMS"),
            Environment.GetEnvironmentVariable("MOTIF_CAPTURE_THEMES"),
        };
        try
        {
            Environment.SetEnvironmentVariable("MOTIF_CAPTURE_MATRIX", null);
            Environment.SetEnvironmentVariable("MOTIF_CAPTURE_SCENES", "overview");
            Environment.SetEnvironmentVariable("MOTIF_CAPTURE_SIZES", "half");
            Environment.SetEnvironmentVariable("MOTIF_CAPTURE_ZOOMS", "150");
            Environment.SetEnvironmentVariable("MOTIF_CAPTURE_THEMES", "dark");

            var selection = MatrixCaptureSelection.Read(["overview"]);

            Assert.Equal("overview", Assert.Single(selection.Scenes));
            Assert.Equal(new MatrixSelectionPoint("half", 150, "dark"), Assert.Single(selection.Points));
        }
        finally
        {
            var names = new[]
            {
                "MOTIF_CAPTURE_MATRIX", "MOTIF_CAPTURE_SCENES", "MOTIF_CAPTURE_SIZES",
                "MOTIF_CAPTURE_ZOOMS", "MOTIF_CAPTURE_THEMES",
            };
            for (var index = 0; index < names.Length; index++)
                Environment.SetEnvironmentVariable(names[index], previous[index]);
        }
    }

    [Theory]
    [InlineData(64, 64, false)]
    [InlineData(50, 64, false)]
    [InlineData(50, 54, true)]
    [InlineData(20, 24, true)]
    public void ScrolledTextOnlyOverlapsAPinnedLabelWhereBothArePainted(
        double textTop, double pinnedTop, bool expectedOverlap)
    {
        avalonia.Invoke(() =>
        {
            var text = new TextBlock { Name = "ScrolledLabel", Text = "Scrolled label", Width = 140, Height = 24 };
            Canvas.SetTop(text, textTop + 40);
            var scroll = new ScrollViewer
            {
                Width = 160, Height = 60,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Content = new Canvas { Width = 160, Height = 200, Children = { text } },
            };
            var pinned = new TextBlock { Name = "PinnedLabel", Text = "Pinned label", Width = 140, Height = 24 };
            Canvas.SetTop(pinned, pinnedTop);
            var window = new Window
            {
                Content = new Canvas { Children = { scroll, pinned } }, Width = 180, Height = 120,
            };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                scroll.Offset = new Vector(0, 40);
                PageScreenshots.Settle(window);

                Assert.Equal(40, scroll.Offset.Y);
                Assert.Equal(textTop, text.TranslatePoint(default, window)!.Value.Y);
                Assert.Equal(60, scroll.Viewport.Height);
                Assert.True(Math.Min(textTop + 24, pinnedTop + 24) - Math.Max(textTop, pinnedTop) >
                    ReachabilityAssertions.Tolerance);
                var overlaps = ReachabilityAssertions.Inspect(window,
                    new LayoutFindingContext("synthetic", "short", 100, "light"))
                    .Where(finding => finding.Check == LayoutCheckNames.OverlappingText).ToArray();

                if (expectedOverlap)
                {
                    var finding = Assert.Single(overlaps);
                    Assert.Equal("ScrolledLabel", finding.Control.Name);
                    Assert.Equal("PinnedLabel", finding.RelatedControl?.Name);
                }
                else Assert.Empty(overlaps);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void ClippedTextStillReportsMissingGlyphsAndOwnBoxOverflow()
    {
        avalonia.Invoke(() =>
        {
            var text = new TextBlock
            {
                Name = "ClippedLabel", Text = "Long label with a missing glyph \u0378", Width = 40, Height = 24,
                TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.None,
            };
            Canvas.SetTop(text, 80);
            var scroll = new ScrollViewer
            {
                Width = 160, Height = 60,
                VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                Content = new Canvas { Width = 160, Height = 200, Children = { text } },
            };
            var window = new Window { Content = scroll, Width = 180, Height = 120 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var transformed = text.GetTransformedBounds()!.Value;
                Assert.Equal(default, transformed.Bounds.TransformToAABB(transformed.Transform)
                    .Intersect(transformed.Clip));

                var findings = ReachabilityAssertions.Inspect(window,
                    new LayoutFindingContext("synthetic", "short", 100, "light"));

                Assert.Contains(findings, finding => finding.Check == LayoutCheckNames.MissingGlyph &&
                    finding.Control.Name == "ClippedLabel" && finding.Identity == "U+0378");
                Assert.Contains(findings, finding => finding.Check == LayoutCheckNames.TextBoxOverflow &&
                    finding.Control.Name == "ClippedLabel");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void TextInSeparateRowsDoesNotOverlap()
    {
        avalonia.Invoke(() =>
        {
            var first = new TextBlock { Text = "first" };
            var second = new TextBlock { Text = "second" };
            var window = new Window
            {
                Content = new StackPanel { Children = { first, second } }, Width = 180, Height = 100,
            };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                Assert.DoesNotContain(ReachabilityAssertions.Inspect(window,
                    new LayoutFindingContext("synthetic", "thin", 100, "light")),
                    finding => finding.Check == LayoutCheckNames.OverlappingText);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void TextSharingAGridCellIsReportedWhenItIsNotAnIntentionalOverlay()
    {
        avalonia.Invoke(() =>
        {
            var first = new TextBlock { Name = "FirstLabel", Text = "first label" };
            var second = new TextBlock { Name = "SecondLabel", Text = "second label" };
            var window = new Window
            {
                Content = new Grid { Children = { first, second } }, Width = 180, Height = 60,
            };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var finding = Assert.Single(ReachabilityAssertions.Inspect(window,
                    new LayoutFindingContext("synthetic", "thin", 100, "light")),
                    item => item.Check == LayoutCheckNames.OverlappingText);
                Assert.Equal("FirstLabel", finding.Control.Name);
                Assert.Equal("SecondLabel", finding.RelatedControl?.Name);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void AGridCellOverlayWithAnExplicitZIndexIsNotReportedAsTextOverlap()
    {
        avalonia.Invoke(() =>
        {
            var first = new TextBlock { Text = "first" };
            var second = new TextBlock { Text = "badge" };
            second.ZIndex = 1;
            var window = new Window
            {
                Content = new Grid { Children = { first, second } }, Width = 180, Height = 60,
            };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                Assert.DoesNotContain(ReachabilityAssertions.Inspect(window,
                    new LayoutFindingContext("synthetic", "thin", 100, "light")),
                    finding => finding.Check == LayoutCheckNames.OverlappingText);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void VisibleTextWithoutAMappedGlyphReportsTheCodePointAndChosenFamily()
    {
        avalonia.Invoke(() =>
        {
            var text = new TextBlock { Text = "A\u0378" };
            var window = new Window { Content = text, Width = 180, Height = 60 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var finding = Assert.Single(ReachabilityAssertions.MissingGlyphFindings(text,
                    new LayoutFindingContext("synthetic", "thin", 100, "light"), window));
                Assert.Equal("U+0378", finding.Identity);
                Assert.Contains("Requested family:", finding.Detail, StringComparison.Ordinal);
                Assert.Contains("chosen family:", finding.Detail, StringComparison.Ordinal);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void TopBarChildrenMustFitAndMustNotOverlap()
    {
        avalonia.Invoke(() =>
        {
            var first = new Button { Name = "Brand", Width = 120, Height = 30, Content = "Motif" };
            var second = new Button { Name = "Project", Width = 80, Height = 30, Content = "Project" };
            DockPanel.SetDock(first, Dock.Left);
            DockPanel.SetDock(second, Dock.Right);
            var topBar = new Border
            {
                Name = "TopBar", Width = 100, Height = 40,
                Child = new DockPanel { Children = { first, second } },
            };
            var window = new Window { Content = topBar, Width = 140, Height = 80 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                var findings = ReachabilityAssertions.TopBarFindings(topBar,
                    new LayoutFindingContext("synthetic", "thin", 100, "light"));
                Assert.Contains(findings, finding => finding.Check == "title-bar-overlap");
                Assert.Contains(findings, finding => finding.Check == "title-bar-overflow");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void SeparateTopBarChildrenStayWithinTheBarWithoutOverlap()
    {
        avalonia.Invoke(() =>
        {
            var first = new Button { Width = 30, Height = 30, Content = "Brand" };
            var second = new Button { Width = 30, Height = 30, Content = "Menu" };
            DockPanel.SetDock(first, Dock.Left);
            DockPanel.SetDock(second, Dock.Right);
            var topBar = new Border
            {
                Name = "TopBar", Width = 100, Height = 40,
                Child = new DockPanel { Children = { first, second } },
            };
            var window = new Window { Content = topBar, Width = 140, Height = 80 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);

                Assert.Empty(ReachabilityAssertions.TopBarFindings(topBar,
                    new LayoutFindingContext("synthetic", "thin", 100, "light")));
            }
            finally { window.Close(); }
        });
    }
}
