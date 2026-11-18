using System.Text.Json;
using SIL.Motif.Contract.Ids;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Model.AppliedLog;
using SIL.Motif.Model.Effects;
using SIL.Motif.Model.Receipts;
using SIL.Motif.Projection;
using SIL.Motif.Projection.Rendering;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class ApplyEffectSummaryTests
{
    [Fact]
    public void ApplyCarriesItsWarningEffectsIntoTheBoundedSummaryAndKeepsTheCompleteEffects()
    {
        var id = CanonicalId.FromGuid(Guid.NewGuid());
        var warnings = Enumerable.Range(0, 25).Select(index => "warning " + index + ": " + new string('w', 500)).ToArray();
        var effects = warnings.Select(warning => new ExpectedEffect(id, "dryRunWarning",
            new Dictionary<string, string>(), new Dictionary<string, string> { ["warning"] = warning },
            JsonSerializer.SerializeToElement(new { warning }))).ToArray();
        var receipt = new Receipt(id, "intent", false, "baseline", "Applied.", effects, "digest",
            new AppliedLogEntry(id.ToGuid(), 1, "now", "person", "intent", "Applied."));

        var projection = ApplyProjectionBuilder.Build(id.Value, receipt);

        Assert.Equal(warnings, projection.Warnings);
        Assert.Equal(25, projection.EffectsSummary.WarningCount);
        Assert.Equal(15, projection.EffectsSummary.WarningsOmitted);
        Assert.Equal(10, projection.EffectsSummary.Warnings.Count);
        Assert.Contains("warnings: 25 (15 omitted)", CommandTextRenderer.Render(projection));
        Assert.DoesNotContain("warnings: none", CommandTextRenderer.Render(projection));
        using var json = JsonDocument.Parse(ProjectionJson.Serialize(projection));
        Assert.Equal(25, json.RootElement.GetProperty("effects").GetArrayLength());
        Assert.Equal(warnings[^1], json.RootElement.GetProperty("effects")[24].GetProperty("preview")
            .GetProperty("warning").GetString());
    }
}
