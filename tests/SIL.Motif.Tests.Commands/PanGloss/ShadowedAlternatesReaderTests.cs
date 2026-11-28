using System.Text.Json.Nodes;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>Reads the shadowed_alternates section of a captured pangloss grammar-health --shadowed report.</summary>
public sealed class ShadowedAlternatesReaderTests
{
    private static string Captured() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestFixtures",
        "GrammarHealth", "pangloss-v0.35.2-shadowed.json"));

    [Fact]
    public void ACapturedReportGivesDefiniteRowsNamingTheEarlierAlternateAndDropsSelectableOnes()
    {
        var response = GrammarHealthReportReader.ReadResponse(Captured(), shadowedRequested: true);

        var report = response.ShadowedAlternates!;
        Assert.True(report.Complete);
        Assert.Equal(2, report.Alternates.Count);
        Assert.All(report.Alternates, row => Assert.Equal(ShadowedAlternateTiers.Definite, row.Tier));
        var entry = Assert.Single(report.Alternates, row => row.Kind == "entry");
        Assert.Equal("eA", entry.Owner);
        Assert.Equal("ba", entry.Form);
        Assert.Equal(["bal"], entry.ShadowedBy);
        Assert.Equal(["unconstrained_earlier"], entry.Reasons);
        Assert.Equal(["eA_ba"], entry.RemovableGuids);
        var rule = Assert.Single(report.Alternates, row => row.Kind == "rule");
        Assert.Equal("PASS", rule.Owner);
        Assert.Equal(["w"], rule.ShadowedBy);
        Assert.Equal(["liw"], rule.SourceAllomorphGuids);
    }

    [Fact]
    public void TheProvenRowsAreNotRepeatedAsFindingsSoATrialDoesNotCountThemAsNew()
    {
        var response = GrammarHealthReportReader.ReadResponse(Captured(), shadowedRequested: true);

        Assert.DoesNotContain(response.Findings, finding => finding.Code == PanGlossFindingCodes.AllomorphShadowed);
        Assert.DoesNotContain(response.Summary, row => row.Code == PanGlossFindingCodes.AllomorphShadowed);
        var unflagged = GrammarHealthReportReader.ReadResponse(Captured());
        Assert.Null(unflagged.ShadowedAlternates);
        Assert.Equal(2, unflagged.Findings.Count(finding => finding.Code == PanGlossFindingCodes.AllomorphShadowed));
    }

    [Fact]
    public void AMaybeRowIsTheSofterTierAndHasNoRemovableGuid()
    {
        var report = JsonNode.Parse(Captured())!;
        var row = report["shadowed_alternates"]!["alternates"]!.AsArray()
            .First(item => item!["verdict"]!.GetValue<string>() == "shadowed")!;
        row["verdict"] = "maybe_shadowed";
        row["reasons"] = new JsonArray("lhs_subsumption_undecided");
        row["removableGuids"] = new JsonArray();

        var read = GrammarHealthReportReader.ReadResponse(report.ToJsonString(), shadowedRequested: true).ShadowedAlternates!;

        var maybe = Assert.Single(read.Maybe);
        Assert.Equal(["lhs_subsumption_undecided"], maybe.Reasons);
        Assert.Empty(maybe.RemovableGuids);
        Assert.Single(read.Definite);
    }

    [Fact]
    public void DeadContinuationsKeepTheirTierAndWhatRejectsThem()
    {
        var read = GrammarHealthReportReader.ReadResponse(Captured(), shadowedRequested: true).ShadowedAlternates!;

        Assert.Contains(read.DeadContinuations, row => row.Tier == ShadowedAlternateTiers.Definite &&
            row.Form == "ba" && row.BlockedBy.Contains("required_mpr VC-B"));
        Assert.Contains(read.DeadContinuations, row => row.Tier == ShadowedAlternateTiers.Maybe && row.Form == "bal");
        Assert.DoesNotContain(read.DeadContinuations, row => row.Form == "tal");
    }

    [Fact]
    public void ARequestedReportWithoutTheSectionCarriesNoShadowedReport()
    {
        var report = JsonNode.Parse(Captured())!.AsObject();
        report.Remove("shadowed_alternates");

        var response = GrammarHealthReportReader.ReadResponse(report.ToJsonString(), shadowedRequested: true);

        Assert.Null(response.ShadowedAlternates);
        Assert.Equal(2, response.Findings.Count);
    }
}
