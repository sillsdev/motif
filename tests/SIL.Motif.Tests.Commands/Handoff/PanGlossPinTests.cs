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
            ["win-x64"] = ("pangloss-win-x64.exe", "08d3a76fbc8cbe402f2fc7c521b3f9e736104c31269bbfb73823934d4f7999ea"),
            ["linux-x64"] = ("pangloss-linux-x64", "0b2643d7d5bd8b63849772d442829e8e00e6fc7015c12db32e5c6e154ffe6794"),
            ["osx-arm64"] = ("pangloss-osx-arm64", "62be03b3dfa92e16daf0731a893808bb7c8785a5d93eeaa74cbedb6668089e08"),
            ["osx-x64"] = ("pangloss-osx-x64", "3804c14f3bd25e4327b835cc4efbc659b2621a7fb06b6e354ea0fa56bfc1b723"),
        };

        Assert.Equal("0.6.0", pin.Version);
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
