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
            ["win-x64"] = ("pangloss-win-x64.exe", "f2713f6f96e1ed4275e89b1124c9a7e023b2f72bddf15172ce9197210316375a"),
            ["linux-x64"] = ("pangloss-linux-x64", "de6a18dacaeeb0ecd2fc5fb52945276a87db4e79713f1891f9d3bf8037b65e15"),
            ["osx-arm64"] = ("pangloss-osx-arm64", "78c02c11a7705f23aeaa0cc47ad7184acc5b85ece862fe63a8b69c1342ac4cee"),
            ["osx-x64"] = ("pangloss-osx-x64", "078652f5d09465eaae2b40a84c8a203c80bed6aa912bd7c41ec3e8747d6c449c"),
        };

        Assert.Equal("0.5.1", pin.Version);
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
