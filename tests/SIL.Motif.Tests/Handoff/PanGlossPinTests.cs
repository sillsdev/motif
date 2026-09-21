using System.Text.Json;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Generator;
using Xunit;

namespace SIL.Motif.Tests.Handoff;

/// <summary>
/// Pins <c>pangloss-release.json</c>, the one statement of which PanGloss release Motif ships, and ties the
/// Handoff's document links to it.
/// </summary>
/// <remarks>
/// A Handoff links PanGloss's format documents at a tag and the package bundles a binary from a release; if
/// the two named different versions, a reader would be handed documents for a parser other than the one that
/// produced the files beside them.
/// </remarks>
public sealed class PanGlossPinTests
{
    [Fact]
    public void ThePinNamesOneReleaseByTagUrlAndHash()
    {
        var pin = ReadPin();

        Assert.Matches(@"^\d+\.\d+\.\d+$", pin.Version);
        Assert.Equal($"v{pin.Version}", pin.Tag);
        Assert.Equal(
            $"https://github.com/sillsdev/PanGloss/releases/download/{pin.Tag}/pangloss.exe",
            pin.Url);
        Assert.Matches("^[0-9a-f]{64}$", pin.Sha256);
    }

    [Fact]
    public void TheHandoffLinksPanGlossDocumentsAtThePinnedRelease()
    {
        Assert.Equal(ReadPin().Tag, HandoffWriter.PanGlossRef);
    }

    private sealed record Pin(string Version, string Tag, string Url, string Sha256);

    private static Pin ReadPin()
    {
        var path = Path.Combine(RepoPaths.FindRepoRoot(), "pangloss-release.json");
        var pin = JsonSerializer.Deserialize<Pin>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return pin ?? throw new InvalidDataException($"{path} is empty.");
    }
}
