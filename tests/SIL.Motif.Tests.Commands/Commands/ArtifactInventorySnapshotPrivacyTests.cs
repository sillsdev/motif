using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;
using SIL.Motif.Commands.Diagnostics;
using Xunit;

namespace SIL.Motif.Tests.Commands;

[Trait("MotifTestLevel", "Unit")]
public sealed class ArtifactInventorySnapshotPrivacyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "motif-artifact-snapshot-privacy-" + Guid.NewGuid().ToString("N"));

    public ArtifactInventorySnapshotPrivacyTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void SnapshotDirectoryIsAccessibleOnlyToItsOwner()
    {
        using var snapshot = new ArtifactRetentionCommands.PrivateSnapshotDirectory();

        AssertOwnerOnly(snapshot.Path, isDirectory: true);
    }

    [Fact]
    public void CopiedSnapshotFilesAreAccessibleOnlyToTheirOwner()
    {
        var source = Path.Combine(_root, "source.db");
        File.WriteAllText(source, "synthetic database bytes");
        using var snapshot = new ArtifactRetentionCommands.PrivateSnapshotDirectory();
        var destination = Path.Combine(snapshot.Path, "copy.db");

        ArtifactRetentionCommands.CopyStable(source, destination,
            new List<ArtifactRetentionCommands.SnapshotFile>(), new ArtifactRetentionCommands.SnapshotBudget());

        AssertOwnerOnly(destination, isDirectory: false);
    }

    [Fact]
    public void SnapshotDirectoryIsRemovedAfterNormalAndExceptionalUse()
    {
        var normalPath = CreateAndDisposeSnapshot(throwAfterUse: false);
        var failedPath = CreateAndDisposeSnapshot(throwAfterUse: true);

        Assert.False(Directory.Exists(normalPath));
        Assert.False(Directory.Exists(failedPath));
    }

    [Fact]
    public void DirectoryNormalizationPreservesTheFilesystemRootWithoutScanningIt()
    {
        var filesystemRoot = Path.GetPathRoot(Path.GetTempPath())!;

        var normalized = ArtifactRetentionCommands.RequireDirectoryPath(filesystemRoot, "root");

        Assert.Equal(filesystemRoot, normalized);
        if (OperatingSystem.IsWindows())
            Assert.Equal(Path.DirectorySeparatorChar, filesystemRoot[^1]);
        else
            Assert.Equal(Path.DirectorySeparatorChar.ToString(), filesystemRoot);
    }

    private string CreateAndDisposeSnapshot(bool throwAfterUse)
    {
        var snapshot = new ArtifactRetentionCommands.PrivateSnapshotDirectory();
        var path = snapshot.Path;
        try
        {
            using (snapshot)
            {
                File.WriteAllText(Path.Combine(path, "synthetic-copy.db"), "private bytes");
                if (throwAfterUse) throw new InvalidDataException("synthetic snapshot failure");
            }
        }
        catch (InvalidDataException) when (throwAfterUse)
        {
        }
        return path;
    }

    private static void AssertOwnerOnly(string path, bool isDirectory)
    {
        if (OperatingSystem.IsWindows())
        {
            AssertWindowsOwnerOnly(path, isDirectory);
            return;
        }

        var mode = File.GetUnixFileMode(path);
        var permissions = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                          UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
        Assert.Equal(UnixFileMode.None, mode & permissions);
        var ownerPermissions = mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Assert.Equal(isDirectory
            ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            : UnixFileMode.UserRead | UnixFileMode.UserWrite, ownerPermissions);
    }

    [SupportedOSPlatform("windows")]
    private static void AssertWindowsOwnerOnly(string path, bool isDirectory)
    {
        var owner = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException();
        CommonObjectSecurity security = isDirectory
            ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access | AccessControlSections.Owner);
        if (isDirectory) Assert.True(security.AreAccessRulesProtected);
        Assert.Equal(owner, security.GetOwner(typeof(SecurityIdentifier)));
        var allows = security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Where(rule => rule.AccessControlType == AccessControlType.Allow)
            .ToArray();
        Assert.NotEmpty(allows);
        Assert.All(allows, rule => Assert.Equal(owner, rule.IdentityReference));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
