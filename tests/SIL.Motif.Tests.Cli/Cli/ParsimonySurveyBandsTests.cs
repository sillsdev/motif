using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class ParsimonySurveyBandsTests
{
    private const string Bands = """
        { "samples": { "Demo": {
            "P-one": { "status": "computed", "eligibleMin": 10, "findingsMin": 2, "findingsMax": 9 },
            "R-two": { "status": "inconclusive" }
        } } }
        """;

    private static ObservedMeasure One(string status = "computed", long? eligible = 40, long? findings = 4) =>
        new("P-one", status, eligible, findings);

    private static ObservedMeasure Two => new("R-two", "inconclusive", null, null);

    [Fact]
    public void ARunInsideEveryBandPasses()
    {
        var bands = ParsimonySurveyBands.Parse(Bands);

        Assert.Empty(bands.Violations("Demo", [One(), Two]));
        Assert.Empty(bands.Violations("Demo", [One(findings: 2), Two]));
        Assert.Empty(bands.Violations("Demo", [One(findings: 9), Two]));
    }

    [Fact]
    public void AMeasureThatDropsToZeroFindingsFails()
    {
        var violations = ParsimonySurveyBands.Parse(Bands).Violations("Demo", [One(findings: 0), Two]);

        Assert.Contains("P-one: 0 findings, expected at least 2.", Assert.Single(violations));
    }

    [Fact]
    public void AMeasureThatTurnsInconclusiveFails()
    {
        var violations = ParsimonySurveyBands.Parse(Bands).Violations("Demo",
            [One(status: "inconclusive", eligible: null, findings: null), Two]);

        Assert.Contains(violations, item => item.Contains("status inconclusive, expected computed"));
    }

    [Fact]
    public void AMeasureWhoseFindingsExplodeOrWhoseEligibilityCollapsesFails()
    {
        var bands = ParsimonySurveyBands.Parse(Bands);

        Assert.Contains("at most 9", Assert.Single(bands.Violations("Demo", [One(findings: 10), Two])));
        Assert.Contains("at least 10", Assert.Single(bands.Violations("Demo", [One(eligible: 3), Two])));
    }

    [Fact]
    public void AMeasureOrSampleMissingFromEitherSideFails()
    {
        var bands = ParsimonySurveyBands.Parse(Bands);

        Assert.Contains("did not run this measure", Assert.Single(bands.Violations("Demo", [One()])));
        Assert.Contains("no entry for this measure", Assert.Single(bands.Violations("Demo",
            [One(), Two, new ObservedMeasure("P-new", "computed", 1, 1)])));
        Assert.Contains("no entry for this sample grammar", Assert.Single(bands.Violations("Other", [One(), Two])));
    }

    [Fact]
    public void TheCheckedInBandsCoverTheFiveSamplesAndEveryMeasureTheSurveyRuns()
    {
        var bands = ParsimonySurveyBands.Load();
        var measures = SIL.Motif.Contract.Parsimony.MeasureCatalog.All
            .Where(measure => SIL.Motif.Host.Parsimony.MeasureRunner.Supports(measure.Id))
            .Select(measure => measure.Id).Order(StringComparer.Ordinal).ToArray();

        Assert.Equal(["Amharic", "Awetí", "Indonesian", "Mbugwe", "Sena"], bands.SampleNames.Order(StringComparer.Ordinal));
        foreach (var sample in bands.SampleNames)
            Assert.Equal(measures, bands.MeasureIds(sample).Order(StringComparer.Ordinal));
    }
}
