using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Records matching, different, new, no-parse and stopped word strips together at four window sizes.</summary>
[Collection(AvaloniaHeadlessCollection.Name)]
[Trait("MotifTestLevel", "System")]
public sealed class AnalyzeTextOutcomeCueScreenshots
{
    [ScreenshotFact]
    public void CaptureAnalyzeTextOutcomeCuesAtFourSizes() => AnalyzeTextsLayoutTests.CheckOutcomeCues(true);
}
