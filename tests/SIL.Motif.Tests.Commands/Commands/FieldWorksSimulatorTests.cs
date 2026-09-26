using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

public sealed class FieldWorksSimulatorTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "motif-fw-simulator-" + Guid.NewGuid().ToString("N"));

    public FieldWorksSimulatorTests() => Directory.CreateDirectory(_directory);

    private string ProjectPath => Path.Combine(_directory, "project.fwdata");

    [Fact]
    public void AHoldCreatesTheLockExclusivelyAndRemovesItOnRelease()
    {
        using (new FieldWorksSimulator(ProjectPath).Hold())
        {
            Assert.True(File.Exists(ProjectPath + ".lock"));
            Assert.Throws<IOException>(() => File.Open(ProjectPath + ".lock", FileMode.Open, FileAccess.Read));
        }

        Assert.False(File.Exists(ProjectPath + ".lock"));
    }

    [Fact]
    public void AHoldOnALockThatAlreadyExistedLeavesThatLockBehind()
    {
        File.WriteAllText(ProjectPath + ".lock", "left by an earlier FieldWorks");

        using (new FieldWorksSimulator(ProjectPath).Hold())
            Assert.True(File.Exists(ProjectPath + ".lock"));

        Assert.Equal("left by an earlier FieldWorks", File.ReadAllText(ProjectPath + ".lock"));
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
