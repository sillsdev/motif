using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using SIL.Motif.App;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class UserDataLabelTests
{
    [Fact]
    public void ATextTitleWithAnUnderscoreIsShownLiterallyAndInFull()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(async () =>
        {
            var (workspace, window) = await PageScreenshots.OpenOverSampleData(parse: false);
            try
            {
                workspace.Context.OpenPage(WorkspacePage.Texts);
                workspace.PageModel<TextsPageModel>().Tab = TextsTab.AnalyzeTexts;
                const string title = "sample bible_h with a long second part that must wrap rather than be cut off";
                workspace.Selection.Texts.Add(new TextChoiceViewModel(Guid.NewGuid(), title));
                PageScreenshots.Settle(window);

                var panel = Assert.Single(window.GetVisualDescendants().OfType<SelectionPanel>());
                var box = Assert.Single(panel.GetVisualDescendants().OfType<CheckBox>(), check =>
                    check.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == title));
                Assert.DoesNotContain(box.GetVisualDescendants(), node => node is AccessText);
                var label = box.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == title);
                Assert.DoesNotContain(label.TextLayout.TextLines, line => line.HasCollapsed);
                Assert.True(label.Bounds.Height >= label.TextLayout.Height - 0.5,
                    $"The label is given {label.Bounds.Height} px and needs {label.TextLayout.Height}.");
            }
            finally
            {
                window.Close();
            }
        }, TimeSpan.FromMinutes(2));
    }
}
