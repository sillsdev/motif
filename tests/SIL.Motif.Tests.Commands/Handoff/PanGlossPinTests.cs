using System.Collections.Generic;
using System.Linq;
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
    public void ThePinNamesEveryPublishedBuildByRidWithItsVerifiedUrlAndHash()
    {
        var pin = ReadPin();
        var expected = new Dictionary<string, (string Asset, string Sha256)>
        {
            ["win-x64"] = ("pangloss-win-x64.exe", "825534136a70a245e970170ee1f41d08917e45e6ad86bd5161011c9d39f4e805"),
            ["linux-x64"] = ("pangloss-linux-x64", "59bd98b0b701377aa38357bdf3872c63776bb1c3aadcc3ead42a17effa08cd6e"),
            ["osx-arm64"] = ("pangloss-osx-arm64", "024d2c4f70c41cc5ca21b8b1ecdbee65d97024eaa071e411d395c4405dfad87f"),
            ["osx-x64"] = ("pangloss-osx-x64", "7eceb2e4bf3abb4d4d34b08b4ca045b98d87f62b8f28ffec53150040625f2753"),
        };

        Assert.Equal("0.5.2", pin.Version);
        Assert.Matches(@"^\d+\.\d+\.\d+$", pin.Version);
        Assert.Equal($"v{pin.Version}", pin.Tag);
        Assert.Equal(new[] { "linux-x64", "osx-arm64", "osx-x64", "win-x64" },
            pin.Assets.Keys.OrderBy(rid => rid, StringComparer.Ordinal));
        foreach (var (rid, asset) in expected)
        {
            Assert.Equal($"https://github.com/sillsdev/PanGloss/releases/download/{pin.Tag}/{asset.Asset}",
                pin.Assets[rid].Url);
            Assert.Equal(asset.Sha256, pin.Assets[rid].Sha256);
        }
    }

    [Fact]
    public void TheHandoffLinksPanGlossDocumentsAtThePinnedRelease()
    {
        Assert.Equal(HandoffWriter.PanGlossRef, ReadPin().Tag);
    }

    private sealed record Asset(string Url, string Sha256);

    private sealed record Pin(string Version, string Tag, Dictionary<string, Asset> Assets);

    private static Pin ReadPin()
    {
        var path = Path.Combine(RepoPaths.FindRepoRoot(), "pangloss-release.json");
        var pin = JsonSerializer.Deserialize<Pin>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return pin ?? throw new InvalidDataException($"{path} is empty.");
    }
}
