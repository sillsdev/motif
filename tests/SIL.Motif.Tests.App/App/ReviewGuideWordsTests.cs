using System.Globalization;
using SIL.Motif.Help;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Pins that Proposal guidance names the window's actions and tells a person what to do after
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
    public void TheApplyGuideLocatesTheActionsForBlockedApply()
    {
        var text = Guide("apply-to-fieldworks");
        Assert.Contains("**Proposals**", text, StringComparison.Ordinal);
        Assert.Contains("**Apply…**", text, StringComparison.Ordinal);
        Assert.Contains("**Refresh**", text, StringComparison.Ordinal);
        Assert.Contains("**Try Again**", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheApplyGuideSaysToRefreshAfterAnUnconfirmedApply()
    {
        var text = Guide("apply-to-fieldworks");
        Assert.Contains("FieldWorks may already have these changes", text, StringComparison.Ordinal);
        Assert.Contains("**Refresh**", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheProposalsGuideMatchesDocumentsComparisonAndEvidence()
    {
        var text = Guide("proposals");
        Assert.Contains("**Open Proposals**", text, StringComparison.Ordinal);
        Assert.Contains("Switching documents keeps your typing", text, StringComparison.Ordinal);
        Assert.Contains("**Close Proposal**", text, StringComparison.Ordinal);
        Assert.Contains("Check two Proposals", text, StringComparison.Ordinal);
        Assert.Contains("Checking a third cannot silently replace", text, StringComparison.Ordinal);
        Assert.Contains("**Show Closed**", text, StringComparison.Ordinal);
        Assert.Contains("**Will Fill In**", text, StringComparison.Ordinal);
        Assert.Contains("**Will Change**", text, StringComparison.Ordinal);
        Assert.Contains("never means zero", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Inspector", text, StringComparison.Ordinal);
    }

    [Fact]
    public void TheOldReviewGuideLeadsToTheDocumentAndApplyGuidance()
    {
        var text = Guide("review-changes");
        Assert.Contains("**Proposals**", text, StringComparison.Ordinal);
        Assert.Contains("guide:review-a-proposal", text, StringComparison.Ordinal);
        Assert.Contains("guide:apply-to-fieldworks", text, StringComparison.Ordinal);
    }
}
