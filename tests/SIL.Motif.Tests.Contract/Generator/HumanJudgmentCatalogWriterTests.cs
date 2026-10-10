using SIL.Motif.Generator;
using SIL.Motif.Generator.Emit;
using Xunit;

namespace SIL.Motif.Tests.Generator;

public sealed class HumanJudgmentCatalogWriterTests
{
    [Fact]
    public void EveryHumanJudgmentFieldFileMatchesTheCheckedManifestAndGenerator()
    {
        var root = RepoPaths.FindRepoRoot();
        var temporary = Path.Combine(Path.GetTempPath(), "motif-human-judgment-drift-" + Guid.NewGuid().ToString("N"));
        try
        {
            var written = HumanJudgmentCatalogWriter.WriteAll(MotifModelLoader.Load(), temporary);
            Assert.NotEmpty(written);
            foreach (var file in written)
                Assert.Equal(File.ReadAllText(Path.Combine(root, file.RelativePath)).ReplaceLineEndings("\n"),
                    File.ReadAllText(Path.Combine(temporary, file.RelativePath)).ReplaceLineEndings("\n"));
        }
        finally { if (Directory.Exists(temporary)) Directory.Delete(temporary, true); }
    }
}
