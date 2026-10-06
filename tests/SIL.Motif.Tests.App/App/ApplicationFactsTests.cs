using SIL.Motif.App.Services;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class ApplicationFactsTests
{
    [Fact]
    public void CustomParserVersionAndAvailabilityAreReportedSeparately()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-application-facts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var parserPath = Path.Combine(root, "custom-parser");
        File.WriteAllText(parserPath, string.Empty);
        try
        {
            var available = ApplicationFacts.ForApp(root, parserPath);
            var missing = ApplicationFacts.ForApp(root, parserPath + ".missing");

            Assert.True(available.IsCustomParser);
            Assert.True(available.ParserAvailable);
            Assert.Equal("unknown (custom executable)", available.PanGlossVersion);
            Assert.True(missing.IsCustomParser);
            Assert.False(missing.ParserAvailable);
            Assert.Equal("unknown (custom executable)", missing.PanGlossVersion);
            Assert.Equal(Path.GetFullPath(root), available.ManagedDataRoot);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
