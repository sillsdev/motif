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
    public void TheReviewGuideSaysToRefreshAfterAnUnconfirmedApply()
    {
        var text = Guide("review-changes");

        Assert.Contains("FieldWorks may already have these changes", text, StringComparison.Ordinal);
        Assert.Contains("**Refresh**", text, StringComparison.Ordinal);
    }
}
