using SIL.Motif.Host.Parser;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.Parser;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.PanGloss;

/// <summary>Runs only against a real parser; it is a no-op until that parser declares the why-not capability.</summary>
[Trait("MotifTestLevel", "System")]
public sealed class WhyNotLiveTests
{
    [RealParserFact]
    public async Task ARealParserThatDeclaresWhyNotPassesMotifsDetection()
    {
        using var description = await FakePanGlossSurfaceTests.Describe(PanGlossExecutable.TryLocate()!);
        if (!description.RootElement.GetProperty("interfaces").TryGetProperty("whyNot", out _)) return;
        var capabilities = PanGlossSurface.ReadDescription(description.RootElement.GetRawText(), out _)!;
        Assert.Null(capabilities.WhyNotRefusal());
        Assert.NotEmpty(capabilities.WhyNot!.Kinds);
    }
}
