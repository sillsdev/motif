using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views.Shell;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class PageContentSlotTests
{
    [Fact]
    public void ContentStaysAttachedWhenAnotherContentIsShownAndOnlyTheCurrentOneIsVisible()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await Task.CompletedTask;
            var slot = new PageContentSlot();
            var first = new Button { Content = "first" };
            var second = new Button { Content = "second" };
            slot.Content = first;
            slot.Content = second;
            Assert.Same(second, slot.Content);
            Assert.NotNull(first.GetVisualParent());
            Assert.False(first.IsEffectivelyVisible);
            Assert.True(first.IsVisible);
            slot.Content = first;
            Assert.Same(first, first.GetVisualParent()!.GetVisualChildren().Single());
            Assert.True(first.GetVisualParent()!.IsVisible);
            Assert.False(second.GetVisualParent()!.IsVisible);
            slot.Content = null;
            Assert.False(first.GetVisualParent()!.IsVisible);
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void SettingTheContentItAlreadyHoldsChangesNothing()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await Task.CompletedTask;
            var slot = new PageContentSlot();
            var content = new Button();
            slot.Content = content;
            var wrapper = content.GetVisualParent();
            slot.Content = content;
            Assert.Same(wrapper, content.GetVisualParent());
            Assert.Same(content, slot.Content);
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void ContentMovesBetweenSlotsAndTheOldSlotForgetsIt()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            await Task.CompletedTask;
            var inspector = new PageContentSlot();
            var page = new PageContentSlot();
            var footer = new Button();
            inspector.Content = footer;
            page.Content = footer;
            inspector.Content = null;
            Assert.Same(footer, page.Content);
            var wrapper = Assert.IsAssignableFrom<Panel>(footer.GetVisualParent());
            Assert.True(wrapper.IsVisible);
            Assert.Empty(inspector.GetVisualDescendants().OfType<Button>());
            Assert.Single(page.GetVisualDescendants().OfType<Button>());
            // Back again: the content is not stranded in the first slot's cache.
            page.Content = null;
            inspector.Content = footer;
            Assert.Single(inspector.GetVisualDescendants().OfType<Button>());
            Assert.Empty(page.GetVisualDescendants().OfType<Button>());
            Assert.True(footer.GetVisualParent()!.IsVisible);
        }, TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void TabNeverReachesAHiddenPagesPane()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                window.Width = 1280;
                window.Height = 800;
                foreach (var page in new[] { WorkspacePage.Review, WorkspacePage.Warnings, WorkspacePage.Overview, WorkspacePage.Texts })
                {
                    workspace.CurrentPage = page;
                    PageScreenshots.Settle(window);
                    for (var tab = 0; tab < 80; tab++)
                    {
                        window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
                        if (window.FocusManager?.GetFocusedElement() is Control focused)
                            Assert.True(focused.IsEffectivelyVisible, $"Tab {tab} on {page} focused a hidden {focused.GetType().Name} '{focused.Name}'.");
                    }
                }
            }
            finally { window.Close(); }
        }, TimeSpan.FromMinutes(2));
    }
}
