using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class SelectionPanelLayoutTests
{
    [Fact]
    public void SelectAllAndClearFitTheSidebarWhenMoreThanThreeTextsMakeClearVisible()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var client = new FakeCommandClient();
            client.ListTextsCompletesWith(new TextInventoryResponse(
                Enumerable.Range(1, 4).Select(index => new TextChoiceSummary(Guid.NewGuid(), "Text " + index)).ToArray(), true));
            var selection = new SelectionViewModel(client);
            await selection.SetProjectAsync("selection-layout.fwdata");
            foreach (var text in selection.Texts) selection.SetTextCounts(text.Id, 400, 400);
            var panel = new SelectionPanel(selection, new TextWordsViewModel(client, selection, client.ReaderOwner));
            var window = new Window { Content = panel, Width = 230, Height = 780 };
            try
            {
                window.Show();
                PageScreenshots.Settle(window);
                var clear = panel.GetVisualDescendants().OfType<Button>()
                    .Single(button => AutomationProperties.GetName(button) == "Clear");
                Assert.True(clear.IsEffectivelyVisible);
                LayoutAssertions.AssertCurrent(window);
            }
            finally { window.Close(); }
        }, TimeSpan.FromSeconds(30));
    }
}
