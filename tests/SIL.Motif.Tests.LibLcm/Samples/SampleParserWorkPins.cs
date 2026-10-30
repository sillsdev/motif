using System.Text.Json.Nodes;
using SIL.Motif.Host.PanGloss;
using Xunit;

namespace SIL.Motif.Tests.Samples;

internal static class SampleParserWorkPins
{
    internal static void ApplyBantu(JsonNode pinned)
    {
        Assert.Equal("v0.12.1", PanGlossInterfaceVersions.SourceTag);
        Set(pinned["fixed"]!, "work", 477, 288);
        Set(pinned["variants"]!["fixed"]!, "work", 477, 288);
        Set(pinned["variants"]!["duplicated-object-marker-slots"]!, "work", 30301, 29408);
    }

    internal static void ApplyTurkic(JsonNode pinned)
    {
        Assert.Equal("v0.12.1", PanGlossInterfaceVersions.SourceTag);
        Set(pinned["fixed"]!, "work", 3414, 775);
        Set(pinned["fixed"]!, "steps", 56, 50);
        Set(pinned["variants"]!["fixed"]!, "work", 3414, 775);
        Set(pinned["variants"]!["fixed"]!, "steps", 56, 50);
        Set(pinned["variants"]!["duplicated-active-noun-templates"]!, "work", 46734, 8815);
        Set(pinned["variants"]!["duplicated-active-noun-templates"]!, "steps", 776, 770);
    }

    private static void Set(JsonNode variant, string property, long original, long current)
    {
        Assert.Equal(original, variant[property]!.GetValue<long>());
        variant[property] = current;
    }
}
