using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.App;

public sealed class PristineProjectFixtureTests
{
    [Fact]
    public void ProjectCopiesContainFieldWorksFilesAndSkipMotifState()
    {
        var root = Path.Combine(Path.GetTempPath(), "motif-pristine-copy-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        var destination = Path.Combine(root, "destination");
        var projectName = NewLangProjFixture.ProjectName;
        Directory.CreateDirectory(source);
        try
        {
            File.WriteAllText(Path.Combine(source, projectName + ".fwdata"), "FieldWorks project");
            File.WriteAllText(Path.Combine(source, projectName + ".bak"), "FieldWorks backup");
            File.WriteAllText(Path.Combine(source, projectName + ".motif.db"), "Motif store");
            File.WriteAllText(Path.Combine(source, projectName + ".motif.db.owner.lock"), "Motif lock");
            Write(source, "WritingSystemStore", "fr.ldml", "FieldWorks writing system");
            Write(source, "WritingSystemStore", "idchangelog.xml", "FieldWorks identity log");
            Write(source, "SharedSettings", "CodexSandboxOffline.ulsx", "FieldWorks user settings");
            Write(source, "SharedSettings", "LexiconSettings.plsx", "FieldWorks settings");
            Write(source, Path.Combine("baselines", new string('a', 64)), "MotifTestProj.fwdata", "published Baseline");
            Write(source, Path.Combine("captures", "capture"), "capture.zip", "capture bundle");
            Write(source, Path.Combine("pending-trial-worker", "owner"), "motif.db", "worker store");
            Write(source, "WritingSystemStore", "unrelated.txt", "unexpected sidecar");
            Write(source, Path.Combine("SharedSettings", "Nested"), "sidecar.bin", "nested state");

            PristineProjectFixture.CopyFieldWorksProjectFiles(source, destination);

            var entries = Directory.GetFileSystemEntries(destination, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(destination, path).Replace(Path.DirectorySeparatorChar, '/'))
                .Order(StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(
                [
                    "MotifTestProj.bak",
                    "MotifTestProj.fwdata",
                    "SharedSettings",
                    "SharedSettings/CodexSandboxOffline.ulsx",
                    "SharedSettings/LexiconSettings.plsx",
                    "WritingSystemStore",
                    "WritingSystemStore/fr.ldml",
                    "WritingSystemStore/idchangelog.xml",
                ],
                entries);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void Write(string root, string directory, string name, string contents)
    {
        var path = Path.Combine(root, directory, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
    }
}
