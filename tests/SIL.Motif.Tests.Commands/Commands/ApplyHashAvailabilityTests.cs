using System.Globalization;
using SIL.Motif.Help;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ApplyHashAvailabilityTests
{
    [Theory]
    [InlineData("apply")]
    [InlineData("apply --proposal")]
    [InlineData("apply --all-pending")]
    public void ApplyHelpExplainsThatSaveVerificationDoesNotSupplyProjectHashes(string command)
    {
        var page = HelpCatalog.Load(CultureInfo.GetCultureInfo("en")).GetHelpPage(HelpEntryKind.Command, command);

        Assert.Contains("Apply does not report before/after project hashes.", page);
        Assert.Contains("Its save check verifies the applied-log entry", page);
        Assert.Contains("the effect digest identifies the effects list", page);
    }
}
