using System.Globalization;
using SIL.Motif.Help;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class OverviewGuideTests
{
    [Fact]
    public void ProgressAndHealthGuidesDescribeRecordedFactsAndLinkToEachOther()
    {
        var catalog = HelpCatalog.Load(CultureInfo.GetCultureInfo("en"));
        var progress = Assert.IsType<string>(catalog.GetHelpPage(HelpEntryKind.Guide, "progress"));
        var overview = Assert.IsType<string>(catalog.GetHelpPage(HelpEntryKind.Guide, "overview"));
        var reading = Assert.IsType<string>(catalog.GetHelpPage(HelpEntryKind.Guide, "reading-the-overview"));

        Assert.Contains("waiting for a decision", progress);
        Assert.Contains("does not start a parse", progress);
        Assert.Contains("[Progress and project health](guide:overview)", progress);
        Assert.Contains("recorded median parser-work", overview);
        Assert.Contains("[Reading project health](guide:reading-the-overview)", overview);
        Assert.Contains("missing or stale measurement", reading);
        Assert.Contains("[Progress and project health](guide:overview)", reading);
    }
}
