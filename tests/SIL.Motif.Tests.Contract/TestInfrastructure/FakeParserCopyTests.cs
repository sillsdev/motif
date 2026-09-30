using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.TestInfrastructure;

public sealed class FakeParserCopyTests
{
    [Fact]
    public void CopyingAgainIntoOneDirectoryLeavesARunningCopyAlone()
    {
        var directory = Path.Combine(Path.GetTempPath(), "motif-fake-copy-" + Guid.NewGuid().ToString("N"));
        try
        {
            var executable = FakeParser.Copy(directory);
            // Held the way a running executable is held: others may read it, nobody may replace it.
            using (new FileStream(executable, FileMode.Open, FileAccess.Read, FileShare.Read))
                Assert.Equal(executable, FakeParser.Copy(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
