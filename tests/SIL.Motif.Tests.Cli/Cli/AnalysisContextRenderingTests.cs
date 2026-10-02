using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class AnalysisContextRenderingTests
{
    [Theory]
    [InlineData("baseline", "Manually approved analyses from the captured Baseline:")]
    [InlineData("saved-project", "Manually approved analyses from the saved FieldWorks project:")]
    public void TextNamesTheSourceSupplyingManualFacts(string source, string expected)
    {
        var saved = DateTimeOffset.Parse("2026-09-05T10:00:00Z");
        var projection = new AnalysisAggregateProjection("Recorded Assessment cases", [])
        {
            AssessmentCases = [],
            ProjectContext = new AnalysisProjectContext(source, null, saved),
        };
        var text = SIL.Motif.Projection.Rendering.CommandTextRenderer.Render(projection);
        Assert.Contains(expected, text);
        Assert.Contains(saved.ToString("O"), text);
        Assert.DoesNotContain("Current project manually", text);
    }
}
