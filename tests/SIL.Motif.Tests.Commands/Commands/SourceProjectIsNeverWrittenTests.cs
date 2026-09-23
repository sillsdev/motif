using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using SIL.Motif.Commands.Baselines;
using SIL.Motif.Tests.TestFixtures;
using Xunit;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// Pins the promise the whole capture design rests on: Motif reads a project FieldWorks may be holding
/// open, and never writes a byte of it back.
/// </summary>
/// <remarks>
/// <para>
/// Everything else about capture is arranged around this — the delete-sharing open, the copy-then-load,
/// the refusal to read a half-written file. Those are pinned individually by
/// <see cref="SIL.Motif.Tests.Host.SavedProjectFileCopierTests"/>. What was not pinned anywhere is the
/// outcome they exist to produce, so it is pinned here directly: hash the source tree before and after,
/// and require it to be identical.
/// </para>
/// <para>
/// The <c>.lock</c> and <c>.bak</c> files are FieldWorks' own, and corrupting either is worse than
/// failing to capture: one is how FieldWorks knows the project is in use, the other is the user's
/// recovery copy. They are seeded here so that "untouched" covers them and not only the <c>.fwdata</c>.
/// </para>
/// <para>
/// The fingerprint skips Motif's own <c>.motif.db</c>, which deliberately lives beside the project
/// (ADR 0041 decision 1) and is created on first open. That is the one file a capture may add, so the
/// success case asserts it by name rather than letting the filter quietly hide it.
/// </para>
/// </remarks>
[Collection(LcmCacheTestCollection.Name)]
public sealed class SourceProjectIsNeverWrittenTests : IDisposable
{
    private readonly PristineProjectFixture _pristine;
    private readonly string _managedRootsParent = Path.Combine(
        Path.GetTempPath(), "SIL.Motif.SourceProjectIsNeverWrittenTests", Guid.NewGuid().ToString("N"));

    public SourceProjectIsNeverWrittenTests(PristineProjectFixture pristine)
    {
        _pristine = pristine;
        Directory.CreateDirectory(_managedRootsParent);
    }

    public void Dispose()
    {
        try { Directory.Delete(_managedRootsParent, recursive: true); }
        catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    [Fact]
    public void CapturingLeavesTheProjectItsLockAndItsBakByteForByteUnchanged()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var projectFolder = Path.GetDirectoryName(fwDataPath)!;
        File.WriteAllText(fwDataPath + ".lock", "held by FieldWorks");
        File.WriteAllText(Path.ChangeExtension(fwDataPath, ".bak"), "the user's recovery copy");
        var before = Fingerprint(projectFolder);

        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());

        Assert.True(outcome.Succeeded);
        Assert.Equal(before, Fingerprint(projectFolder));
        // The one file Motif does add beside the project is its own store (ADR 0041 decision 1).
        Assert.True(File.Exists(Path.ChangeExtension(fwDataPath, ".motif.db")));
    }

    [Fact]
    public void ARefusedCaptureAlsoLeavesTheProjectUnchanged()
    {
        var fwDataPath = _pristine.CopyProjectFile();
        var projectFolder = Path.GetDirectoryName(fwDataPath)!;
        // A half-written project: the bytes FieldWorks would leave behind mid-save.
        File.WriteAllText(fwDataPath, "<languageproject><incomplete>");
        var before = Fingerprint(projectFolder);

        var outcome = BaselineCaptureCommand.Capture(new BaselineCaptureRequest(fwDataPath), NewManagedRoot());

        Assert.False(outcome.Succeeded);
        Assert.Equal(before, Fingerprint(projectFolder));
    }

    /// Every FieldWorks-owned file under the project, by relative path, length, and content hash.
    private static IReadOnlyList<string> Fingerprint(string projectFolder)
    {
        using var sha = SHA256.Create();
        return Directory.EnumerateFiles(projectFolder, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).Contains(".motif.db", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path =>
            {
                using var stream = File.OpenRead(path);
                return string.Join(
                    '|',
                    Path.GetRelativePath(projectFolder, path),
                    stream.Length.ToString(),
                    Convert.ToHexString(sha.ComputeHash(stream)));
            })
            .ToList();
    }

    private string NewManagedRoot()
    {
        var root = Path.Combine(_managedRootsParent, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
