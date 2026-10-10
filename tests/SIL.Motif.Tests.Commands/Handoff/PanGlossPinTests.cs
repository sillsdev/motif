using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SIL.Motif.Commands.Handoff;
using SIL.Motif.Generator;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Handoff;

/// <summary>
/// Checks the PanGloss release asset pin and ties Handoff links to the source tag defining its formats.
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
        var expectedAssets = new Dictionary<string, string>
        {
            ["win-x64"] = "pangloss-win-x64.exe",
            ["linux-x64"] = "pangloss-linux-x64",
            ["osx-arm64"] = "pangloss-osx-arm64",
            ["osx-x64"] = "pangloss-osx-x64",
        };

        Assert.Matches(@"^\d+\.\d+\.\d+$", pin.Version);
        Assert.Equal($"v{pin.Version}", pin.Tag);
        Assert.Equal(new[] { "linux-x64", "osx-arm64", "osx-x64", "win-x64" },
            pin.Assets.Keys.OrderBy(rid => rid, StringComparer.Ordinal));
        foreach (var (rid, fileName) in expectedAssets)
        {
            Assert.Equal($"https://github.com/sillsdev/PanGloss/releases/download/{pin.Tag}/{fileName}", pin.Assets[rid].Url);
            Assert.Matches("^[0-9a-f]{64}$", pin.Assets[rid].Sha256);
        }
    }

    [Fact]
    public void TheHandoffLinksPanGlossDocumentsAtTheInterfaceSourceTag()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(PinPath));
        Assert.Equal(HandoffWriter.PanGlossRef,
            document.RootElement.GetProperty("interfaces").GetProperty("sourceTag").GetString());
    }

    private sealed record Asset(string Url, string Sha256);

    private sealed record Pin(string Version, string Tag, Dictionary<string, Asset> Assets);

    private static Pin ReadPin()
    {
        var path = PinPath;
        var pin = JsonSerializer.Deserialize<Pin>(
            File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return pin ?? throw new InvalidDataException($"{path} is empty.");
    }

    private static string PinPath => Path.Combine(RepoPaths.FindRepoRoot(), "pangloss-release.json");
}
