using SIL.LCModel;
using SIL.Motif.Host.Assess;
using SIL.Motif.Host.PanGloss;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Assess;
using SIL.Motif.Worker.Store;
using Xunit;
using Xunit.Abstractions;

namespace SIL.Motif.Tests.Parser;

[Collection(global::SIL.Motif.Tests.TestFixtures.LcmCacheParallelCollections.Group2)]
public sealed class FakeParserSeamIntegrationTests
{
    [Fact]
    public async Task MissingExportedDirectoryIsRefusedBeforeInvokingPanGloss()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-parser-missing-source-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new StatsCacheStore(WorkspaceOwnership.Bootstrap(root));
            var invoker = new FakeInvoker();
            var assessor = new PanGlossAssessor(paths, invoker);

            await Assert.ThrowsAsync<DirectoryNotFoundException>(() => assessor.ProduceAsync(
                new([SeededProject.FirstForm], [AssessmentKind.Correctness], TimeSpan.FromSeconds(5)),
                Path.Combine(root, "not-exported"), CancellationToken.None));

            Assert.Empty(invoker.Requests);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

}
