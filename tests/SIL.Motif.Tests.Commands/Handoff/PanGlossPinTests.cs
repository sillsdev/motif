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
            ["win-x64"] = ("pangloss-win-x64.exe", "777dac0e9f0bb47a16da970c702eeb03be6d0da167a9370b9e1264561e639799"),
            ["linux-x64"] = ("pangloss-linux-x64", "ee955edee049e8710dce4748c0609a011002d5abeec20eb23d37edc033f75c2b"),
            ["osx-arm64"] = ("pangloss-osx-arm64", "f9411a24364ad44dd2d86fdb5b7f578c5c2bf4f40fc275e7c774f0f22255f480"),
            ["osx-x64"] = ("pangloss-osx-x64", "b2f85a4faffe52e4d7933c93378ff679a3c97ccbd3f5df599ea0b9c5836e51f4"),
        };

        Assert.Equal("0.6.2", pin.Version);
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
