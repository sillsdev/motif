using SIL.LCModel;
using SIL.LCModel.DomainServices.BackupRestore;
using SIL.Motif.Host.LcmUtils;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests;

/// <summary>
/// A FieldWorks backup restores beside itself into a project Motif can open, a second restore takes the next free
/// folder name instead of touching the first, and a file that is not a backup is refused.
/// </summary>
[Collection(LcmCacheTestCollection.Name)]
public sealed class FieldWorksBackupTests
{
    [Fact]
    public void ABackupRestoresBesideItselfAndASecondRestoreTakesTheNextName()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            var loader = new FwDataProjectLoader();
            var backupFolder = Path.Combine(tempRoot, "backups");
            Directory.CreateDirectory(backupFolder);
            string backupPath;
            using (var cache = NewLangProjFixture.CreateCache(Path.Combine(tempRoot, "source")))
            {
                SeededProject.Seed(cache);
                loader.Save(cache);
                var settings = new BackupProjectSettings(cache, null, backupFolder, "9.0");
                Assert.True(new ProjectBackupService(cache, settings).BackupProject(new LcmThreadedProgress(),
                    out backupPath));
            }

            var first = FieldWorksBackup.Restore(backupPath);
            var second = FieldWorksBackup.Restore(backupPath);

            Assert.Equal(Path.Combine(backupFolder, NewLangProjFixture.ProjectName,
                NewLangProjFixture.ProjectName + ".fwdata"), first);
            var numbered = NewLangProjFixture.ProjectName + " 2";
            Assert.Equal(Path.Combine(backupFolder, numbered, numbered + ".fwdata"), second);
            Assert.True(File.Exists(backupPath));
            using var restored = loader.LoadCache(second);
            Assert.True(restored.ServiceLocator.GetInstance<ILexEntryRepository>().Count > 0);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void AFileThatIsNotABackupIsRefused()
    {
        var path = Path.Combine(Path.GetTempPath(), "SIL.Motif.Tests", Guid.NewGuid().ToString("N") + ".fwbackup");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "not a zip");
        try
        {
            Assert.Throws<InvalidDataException>(() => FieldWorksBackup.Restore(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
