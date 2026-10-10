using SIL.Motif.Generator.Emit;
using Xunit;

namespace SIL.Motif.Tests.Generator;

public sealed class RedundantZeroAffixDeleteCatalogWriterTests
{
    [Fact]
    public void WriterEmitsOnlyTheClosedZeroAffixDeleteKindAndIsIdempotent()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-zero-delete-emitter", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var first = RedundantZeroAffixDeleteCatalogWriter.WriteAll(root);
            var firstText = File.ReadAllText(Path.Combine(root, RedundantZeroAffixDeleteEmitter.OutputPath));
            var second = RedundantZeroAffixDeleteCatalogWriter.WriteAll(root);
            var secondText = File.ReadAllText(Path.Combine(root, RedundantZeroAffixDeleteEmitter.OutputPath));

            Assert.Equal([RedundantZeroAffixDeleteEmitter.OutputPath], first.Select(item => item.RelativePath));
            Assert.Equal(first.Select(item => item.RelativePath), second.Select(item => item.RelativePath));
            Assert.Equal(firstText, secondText);
            Assert.Contains("deleteRedundantZeroAffixGraph", firstText, StringComparison.Ordinal);
            Assert.DoesNotContain("deleteEntry", firstText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
