using System.Globalization;
using SIL.Motif.Help;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that the Guide names Review changes' actions as the window labels them, and tells a person what to do after
/// an Apply Motif could not confirm.
/// </summary>
public sealed class ReviewGuideWordsTests
{
    private static string Guide(string page)
    {
        var text = HelpCatalog.Load(CultureInfo.GetCultureInfo("en")).GetHelpPage(HelpEntryKind.Guide, page);
        Assert.NotNull(text);
        return text;
    }

    [Theory]
    [InlineData("review-changes")]
    [InlineData("when-a-change-no-longer-fits")]
    [InlineData("replace-a-pending-change")]
    [InlineData("pending-changes")]
    public void TheGuideNamesTheButtonsTheWindowShows(string page)
    {
        var text = Guide(page);

        Assert.DoesNotContain("**Remove**", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Check again against the refreshed project", text, StringComparison.Ordinal);
        Assert.Contains("**Undo**", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReviewGuideLocatesTheActionsForBlockedApply()
    {
        var text = Guide("review-changes");

        Assert.DoesNotContain("lists each reason and its action", text, StringComparison.Ordinal);
        Assert.Contains("**Check these changes** in the Apply area", text, StringComparison.Ordinal);
        Assert.Contains("**Undo** beside a change", text, StringComparison.Ordinal);
        Assert.Contains("**Refresh** in the top bar", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReviewGuideSaysToRefreshAfterAnUnconfirmedApply()
    {
        var text = Guide("review-changes");

        Assert.Contains("FieldWorks may already have these changes", text, StringComparison.Ordinal);
        Assert.Contains("**Refresh**", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProposalsGuideMatchesItsFiltersAndInspector()
    {
        var text = Guide("proposals");

        Assert.Contains("**Your changes not applied yet**", text, StringComparison.Ordinal);
        Assert.Contains("**Waiting for you** includes Proposals with a current Trial", text, StringComparison.Ordinal);
        Assert.Contains("Proposals that need another try", text, StringComparison.Ordinal);
        Assert.Contains("**Closed** lists Proposals that were applied, declined, superseded or withdrawn", text,
            StringComparison.Ordinal);
        Assert.Contains("**Show closed** includes them with the other groups and clears the current filter", text,
            StringComparison.Ordinal);
        Assert.Contains("When the Inspector is collapsed, choose **Details** from the row's **⋯** menu", text,
            StringComparison.Ordinal);
        Assert.Contains("**Open in Review changes** there to review the Proposal", text, StringComparison.Ordinal);
        Assert.Contains("The **⋯** menu repeats each row's available actions", text, StringComparison.Ordinal);
        Assert.DoesNotContain("**Not tried yet** has no Trial", text, StringComparison.Ordinal);
    }
}
