using SIL.Motif.Cli;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using Xunit;

namespace SIL.Motif.Tests.Cli;

public sealed class WritingSystemsRenderingTests
{
    [Fact]
    public void WritingSystemsHaveAReleasedCliPathAndRenderSettingsInPoints()
    {
        Assert.Equal("writing-systems", Assert.Single(CliVerbCatalog.All,
            verb => verb.CommandName == "writing-systems").Verb);
        var response = new WritingSystemsResponse(true,
        [new("ar", "Arabic", "Ar", WritingSystemKind.Vernacular, 0, true, "Missing font", "smcp=1",
            true, new Dictionary<string, double> { ["Normal"] = 18 })]);
        var rendered = CommandTextRenderer.Render(CommandOutcome<WritingSystemsResponse>.Success(response), false);
        Assert.Equal(0, rendered.ExitCode);
        Assert.Contains("Missing font", rendered.Output);
        Assert.Contains("right to left", rendered.Output);
        Assert.Contains("Normal: 18 pt", rendered.Output);
        Assert.Contains("FieldWorks", rendered.Output);
        var json = CommandTextRenderer.Render(CommandOutcome<WritingSystemsResponse>.Success(response), true);
        Assert.Contains("\"fontFeatures\": \"smcp=1\"", json.Output);
    }
}
