using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SIL.Motif.App.ViewModels;
using SIL.Motif.App.Views;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Tests.App.Walkthrough;
using Xunit;

namespace SIL.Motif.Tests.App;

[Collection(AvaloniaHeadlessCollection.Name)]
public sealed class TextsListsHandoffAvailabilityTests
{
    [Fact]
    public void EmptyAndUntickedListsGiveTheirHandoffReasonOnTheButton_NotAsANoteBesideIt()
    {
        AvaloniaHeadlessFixture.RunUntilComplete(() =>
        {
            var (workspace, window) = FakeComposedWindow.Create();
            try
            {
                window.Show();
                window.ApplyTemplate();
                window.UpdateLayout();

                var texts = window.GetLogicalDescendants().OfType<ListBoxItem>().Single(item =>
                    AutomationProperties.GetName(item) == "Texts page");
                HeadlessClick.Click(window, texts, "Texts page");
                FakeComposedWindow.Click(window, "Lists tab");

                var lists = workspace.PageModel<TextsPageModel>().TextsLists;
                var wholeListButton = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => ReferenceEquals(button.Command, lists.HandOffListCommand));
                var checkedWordsButton = window.GetVisualDescendants().OfType<Button>()
                    .Single(button => ReferenceEquals(button.Command, lists.HandOffCheckedWordsCommand));
                Assert.False(wholeListButton.IsEffectivelyEnabled);
                Assert.False(checkedWordsButton.IsEffectivelyEnabled);
                Assert.NotEmpty(lists.HandOffListDisabledReason);
                Assert.NotEmpty(lists.HandOffCheckedWordsHelpText);
                Assert.Equal(lists.HandOffCheckedWordsHelpText,
                    AutomationProperties.GetHelpText(checkedWordsButton));
                Assert.Equal(lists.HandOffCheckedWordsHelpText, ToolTip.GetTip(checkedWordsButton));
                Assert.Equal(lists.HandOffListDisabledReason, ToolTip.GetTip(wholeListButton));
                Assert.Equal(lists.HandOffListDisabledReason, AutomationProperties.GetHelpText(wholeListButton));
                Assert.Empty(VisibleReasons(window, lists.HandOffListDisabledReason,
                    lists.HandOffCheckedWordsDisabledReason));

                var words = workspace.Context.Assess.Words;
                words.Load([new AssessmentWordResult(
                    "approved-empty", "no-analysis", false, "Search completed", 10, null)
                {
                    ProjectStanding = ProjectStanding.Approved,
                    OccurrenceCount = 1,
                }]);
                workspace.Context.Assess.Compare.Load(words.AllRows);
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();

                Assert.True(wholeListButton.IsEffectivelyEnabled);
                Assert.False(checkedWordsButton.IsEffectivelyEnabled);
                Assert.Empty(lists.HandOffListDisabledReason);
                Assert.NotEmpty(lists.HandOffCheckedWordsDisabledReason);
                Assert.Equal(lists.HandOffCheckedWordsDisabledReason, AutomationProperties.GetHelpText(checkedWordsButton));
                Assert.Equal(lists.HandOffCheckedWordsDisabledReason, ToolTip.GetTip(checkedWordsButton));
                Assert.Empty(VisibleReasons(window, lists.HandOffCheckedWordsDisabledReason));
            }
            finally
            {
                window.Close();
                workspace.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }

            return Task.CompletedTask;
        }, TimeSpan.FromSeconds(10));
    }

    private static IReadOnlyList<string?> VisibleReasons(MainWindow window, params string[] reasons)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        return window.GetVisualDescendants().OfType<TextBlock>()
            .Where(text => text.IsEffectivelyVisible && text.Text is { } value && reasons.Contains(value))
            .Select(text => text.Text).ToArray();
    }

}
