using Xunit;

namespace SIL.Motif.Tests.Parsimony;

public sealed class MorphotacticAcceptanceRootCleanupTests
{
    [Fact]
    public void DisposingAcceptanceTestsRemovesTheirWorkerDatabaseRoot()
    {
        var acceptance = new MorphotacticRecipeAcceptanceTests(null!, null!);
        var root = acceptance.TemporaryRoot;
        var neighboringProject = root + "-user-project";
        var database = Path.Combine(root, "worker", "p", "evidence", "grammar-facts.sqlite");
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        Directory.CreateDirectory(neighboringProject);
        File.WriteAllBytes(database, new byte[8 * 1024 * 1024]);
        var project = Path.Combine(neighboringProject, "project.fwdata");
        File.WriteAllText(project, "preserved project data");

        try
        {
            Assert.Equal(8 * 1024 * 1024, new FileInfo(database).Length);
            Assert.IsAssignableFrom<IDisposable>(acceptance).Dispose();
            Assert.False(Directory.Exists(root));
            Assert.True(File.Exists(project));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            if (Directory.Exists(neighboringProject)) Directory.Delete(neighboringProject, recursive: true);
        }
    }
}
