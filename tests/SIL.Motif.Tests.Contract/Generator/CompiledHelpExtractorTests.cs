using SIL.Motif.Generator;
using SIL.Motif.Generator.Descriptions.Harvest;
using Xunit;

namespace SIL.Motif.Tests.Generator;

public sealed class CompiledHelpExtractorTests
{
    [NonWindowsFact]
    public void DecompileExplainsTheWindowsOnlyToolBeforeCheckingForAFile()
    {
        var exception = Assert.Throws<GeneratorException>(() =>
            CompiledHelpExtractor.Decompile("missing.chm", Path.Combine(Path.GetTempPath(), "motif-help")));

        Assert.Contains("Windows-only", exception.Message, StringComparison.Ordinal);
        Assert.Contains("extract the help file elsewhere", exception.Message, StringComparison.Ordinal);
    }
}

internal sealed class NonWindowsFactAttribute : FactAttribute
{
    public NonWindowsFactAttribute()
    {
        if (OperatingSystem.IsWindows()) Skip = "The assertion covers the non-Windows extraction path.";
    }
}
