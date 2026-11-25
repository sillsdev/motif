using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SIL.Motif.Commands;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;
using SIL.Motif.Host.Store;
using SIL.Motif.Worker.Store;
using Xunit;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.Commands;

/// <summary>
/// <c>store backup</c> copies the store consistently while it is in use, and <c>store restore</c> puts it back
/// only when the store is free, the backup is intact and no newer than this build, saving what it replaces.
/// </summary>
public sealed class StoreBackupCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "motif-backup-" + Guid.NewGuid().ToString("N"));

    public StoreBackupCommandTests()
    {
        Directory.CreateDirectory(_root);
        File.WriteAllText(Project, "<languageproject />");
        Assert.True(ProjectStoreCommand.Run(Project, MotifProductVersion.CurrentText,
            (_, _) => CommandOutcome<string>.Success("created")).Succeeded);
        SqliteConnection.ClearAllPools();
    }

    private string Project => Path.Combine(_root, "Koro.fwdata");

    private string Store => Path.Combine(_root, "Koro.motif.db");

    private string Folder(string name) => Path.Combine(_root, name);

    private string Made(string name) => Directory.CreateDirectory(Folder(name)).FullName;

    [Fact]
    public void ABackupWritesTheStoreAndAReceiptWithSizesShasAndTheSchema()
    {
        AddMarker(Store, "first");
        File.WriteAllText(Path.Combine(_root, "Koro.motif.toml"), "collect = []");

        var result = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b1")));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        var receipt = result.Value!;
        Assert.Equal(MotifSchema.CurrentSchema, receipt.SchemaVersion);
        Assert.False(receipt.FwDataIncluded);
        Assert.Contains("not part of this backup", receipt.FwDataNote, StringComparison.Ordinal);
        Assert.Equal(["store", "config"], receipt.Files.Select(file => file.Role));
        foreach (var file in receipt.Files)
        {
            Assert.Equal(new FileInfo(file.Path).Length, file.Bytes);
            Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file.Path))), file.Sha256);
        }
        Assert.Equal(["first"], Markers(Path.Combine(Folder("b1"), "store.sqlite")));
        Assert.Equal(MotifSchema.CurrentSchema, ReadSchema(Path.Combine(Folder("b1"), "store.sqlite")));
        Assert.Empty(Directory.GetFiles(Folder("b1"), "*-wal"));
        using var document = JsonDocument.Parse(File.ReadAllText(receipt.ReceiptPath));
        Assert.False(document.RootElement.GetProperty("fwDataIncluded").GetBoolean());
    }

    [Fact]
    public void WithoutToTheBackupGoesIntoATimestampedFolderBesideTheStore()
    {
        var at = new DateTimeOffset(2026, 11, 25, 8, 30, 15, 123, TimeSpan.Zero);

        var result = StoreBackupCommand.Backup(new StoreBackupRequest(Project), new FixedClock(at));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(Path.Combine(_root, "Koro.motif-backup-20261125T083015123Z"), result.Value!.BackupDirectory);
        Assert.True(File.Exists(Path.Combine(result.Value.BackupDirectory, StoreBackupCommand.ReceiptName)));
    }

    [Fact]
    public void ABackupTakenWhileAnotherConnectionReadsAndWritesIsOneConsistentSnapshot()
    {
        AddMarker(Store, "before");
        using var database = MotifDatabase.OpenOwned(Store,
            new SIL.Motif.Contract.Projects.ProjectLocator(Project, "Koro"), MotifSchema.CurrentSchema,
            MotifProductVersion.Current);
        using var reader = database.OpenConnection();
        using var readTransaction = reader.BeginTransaction(deferred: true);
        using (var command = reader.CreateCommand())
        {
            command.Transaction = readTransaction;
            command.CommandText = "SELECT COUNT(*) FROM StoreRelocations;";
            Assert.Equal(1L, command.ExecuteScalar());
        }
        using var writer = database.OpenConnection();
        using (var command = writer.CreateCommand())
        {
            command.CommandText = "INSERT INTO StoreRelocations (OldFwDataPath, NewFwDataPath, RelocatedUtc, Basis) " +
                "VALUES ('during', 'x', 't', 'name');";
            command.ExecuteNonQuery();
        }

        var result = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("live")));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        var copy = Path.Combine(Folder("live"), "store.sqlite");
        Assert.Equal(["before", "during"], Markers(copy));
        Assert.Equal(["ok"], Scalars(copy, "PRAGMA integrity_check;"));
    }

    [Fact]
    public void ARestoreReturnsTheStoreToTheBackupAndSavesTheStoreItReplaced()
    {
        AddMarker(Store, "kept");
        File.WriteAllText(Path.Combine(_root, "Koro.motif.toml"), "collect = [\"A\"]");
        var backup = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b1"))).Value!;
        AddMarker(Store, "added-later");
        File.WriteAllText(Path.Combine(_root, "Koro.motif.toml"), "collect = [\"B\"]");

        var result = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal(["kept"], Markers(Store));
        Assert.Equal("collect = [\"A\"]", File.ReadAllText(Path.Combine(_root, "Koro.motif.toml")));
        Assert.Equal(MotifSchema.CurrentSchema, result.Value!.SchemaVersion);
        var saved = result.Value.PreRestoreDirectory!;
        Assert.StartsWith(Path.Combine(_root, "Koro.motif-backups", "pre-restore-"), saved, StringComparison.Ordinal);
        Assert.Equal(["kept", "added-later"], Markers(Path.Combine(saved, "moved", "store.sqlite")));
        // The old store is kept once: no online backup and no receipt beside the moved files.
        Assert.False(File.Exists(Path.Combine(saved, "store.sqlite")));
        Assert.False(File.Exists(Path.Combine(saved, StoreBackupCommand.ReceiptName)));
        Assert.Single(Directory.GetFiles(saved, "store.sqlite", SearchOption.AllDirectories));
        Assert.Empty(Directory.GetFiles(_root, "*.restore-*"));
        var reopened = ProjectStoreCommand.Run(Project, MotifProductVersion.CurrentText,
            (_, _) => CommandOutcome<string>.Success("opened"));
        Assert.True(reopened.Succeeded, reopened.Refusal?.Message);
    }

    [Fact]
    public void ARestoreWhileTheStoreIsOpenIsRefusedAndTheStoreIsLeftAsItWas()
    {
        var backup = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b1"))).Value!;
        AddMarker(Store, "later");
        var before = File.ReadAllBytes(Store);
        using var lease = new FileStream(Store + ".use.lock", FileMode.OpenOrCreate, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.None);

        var result = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory));

        Assert.False(result.Succeeded);
        Assert.Equal(RefusalCodes.ProjectBusy, result.Refusal!.Code);
        lease.Dispose();
        Assert.Equal(before, File.ReadAllBytes(Store));
        Assert.Empty(Directory.GetFiles(_root, "*.restore-*"));
    }

    [Fact]
    public void ABackupWhoseSchemaIsNewerThanThisBuildIsRefusedAndTheStoreIsLeftAsItWas()
    {
        var backup = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b1"))).Value!;
        SetSchema(Path.Combine(backup.BackupDirectory, "store.sqlite"), MotifSchema.CurrentSchema + 1);
        RewriteReceipt(backup, schema: MotifSchema.CurrentSchema);
        AddMarker(Store, "later");
        var before = File.ReadAllBytes(Store);

        var byFile = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory));

        Assert.Equal(RefusalCodes.StoreRestoreNewerSchema, byFile.Refusal?.Code);
        RewriteReceipt(backup, schema: MotifSchema.CurrentSchema + 1);
        var byReceipt = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory));
        Assert.Equal(RefusalCodes.StoreRestoreNewerSchema, byReceipt.Refusal?.Code);
        Assert.Equal(before, File.ReadAllBytes(Store));
        Assert.Empty(Directory.GetFiles(_root, "*.restore-*"));
    }

    [Fact]
    public void AFileThatDoesNotMatchItsShaIsRefusedAndTheStoreIsLeftAsItWas()
    {
        var backup = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b1"))).Value!;
        var copy = Path.Combine(backup.BackupDirectory, "store.sqlite");
        var bytes = File.ReadAllBytes(copy);
        bytes[^1] ^= 0xff;
        File.WriteAllBytes(copy, bytes);
        AddMarker(Store, "later");
        var before = File.ReadAllBytes(Store);

        var result = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory));

        Assert.Equal(RefusalCodes.StoreRestoreChecksumMismatch, result.Refusal?.Code);
        Assert.Equal("store.sqlite", result.Refusal!.Facts["file"]);
        Assert.Equal(before, File.ReadAllBytes(Store));
        Assert.Empty(Directory.GetFiles(_root, "*.restore-*"));
        Assert.False(Directory.Exists(Path.Combine(_root, "Koro.motif-backups")));
    }

    [Fact]
    public void AFolderWithoutAReceiptAndABackupOfAnotherProjectAreRefused()
    {
        Directory.CreateDirectory(Folder("empty"));
        var none = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, Folder("empty")));
        Assert.Equal(RefusalCodes.StoreRestoreInvalid, none.Refusal?.Code);

        var backup = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b1"))).Value!;
        File.WriteAllText(backup.ReceiptPath, JsonSerializer.Serialize(backup with { ProjectIdentity = "Other" }, ReceiptOptions));
        var other = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory));
        Assert.Equal(RefusalCodes.StoreRestoreOtherProject, other.Refusal?.Code);
    }

    [Fact]
    public void ABackupOfAProjectWithNoStoreOrToAFullFolderIsRefused()
    {
        Directory.CreateDirectory(Folder("full"));
        File.WriteAllText(Path.Combine(Folder("full"), "x.txt"), "x");
        var full = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("full")));
        Assert.Equal(RefusalCodes.StoreBackupDestinationExists, full.Refusal?.Code);

        var lonely = Path.Combine(_root, "Lonely.fwdata");
        File.WriteAllText(lonely, "<languageproject />");
        var none = StoreBackupCommand.Backup(new StoreBackupRequest(lonely));
        Assert.Equal(RefusalCodes.StoreBackupNoStore, none.Refusal?.Code);
    }

    [Fact]
    public void ABackupWhileTheStoreIsHeldExclusivelyIsRefusedAsBusy()
    {
        using var exclusive = new FileStream(Store + ".use.lock", FileMode.OpenOrCreate, FileAccess.Read,
            FileShare.None, 1, FileOptions.None);

        var result = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b1")));

        Assert.Equal(RefusalCodes.ProjectBusy, result.Refusal?.Code);
        Assert.False(Directory.Exists(Folder("b1")));
    }

    private const string GuidA = "11111111-1111-1111-1111-111111111111";
    private const string GuidB = "22222222-2222-2222-2222-222222222222";

    private string TrialRoot => Store + ".trial-exports";

    private string Config => Path.Combine(_root, "Koro.motif.toml");

    private void WriteTrial(string name, string text)
    {
        var path = Path.Combine(TrialRoot, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    private static string Backed(StoreRestoreResponse response, string relative) =>
        Path.Combine(response.PreRestoreDirectory!, "moved", relative.Replace('/', Path.DirectorySeparatorChar));

    [Fact]
    public void ARestoreMovesTrialExportsConfigAndReportsTheBackupLacksIntoThePreRestoreFolderAndListsThem()
    {
        AddMarker(Store, "kept");
        WriteTrial("a.json", "A1");
        var backup = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b1"))).Value!;
        Assert.Contains(backup.Files, file => file.Role == "trial-export" && file.Name == "trial-exports/a.json");
        WriteTrial("a.json", "A2");
        WriteTrial("sub/c.json", "C");
        File.WriteAllText(Config, "collect = []");
        File.WriteAllText(Store + ".upgrade.json", "{}");

        var result = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        var response = result.Value!;
        Assert.Equal("A1", File.ReadAllText(Path.Combine(TrialRoot, "a.json")));
        Assert.False(File.Exists(Path.Combine(TrialRoot, "sub", "c.json")));
        Assert.False(File.Exists(Config));
        Assert.False(File.Exists(Store + ".upgrade.json"));
        Assert.Equal("A2", File.ReadAllText(Backed(response, "trial-exports/a.json")));
        Assert.Equal("C", File.ReadAllText(Backed(response, "trial-exports/sub/c.json")));
        Assert.Equal("collect = []", File.ReadAllText(Backed(response, "Config.motif.toml")));
        Assert.Equal("{}", File.ReadAllText(Backed(response, "store.upgrade.json")));
        var reasons = response.Quarantined.ToDictionary(file => Path.GetFileName(file.OriginalPath), file => file.Reason);
        Assert.Equal("replaced", reasons["a.json"]);
        Assert.Equal("not-in-backup", reasons["c.json"]);
        Assert.Equal("not-in-backup", reasons["Koro.motif.toml"]);
        Assert.Equal("not-in-backup", reasons["Koro.motif.db.upgrade.json"]);
        Assert.Equal(["kept"], Markers(Backed(response, "store.sqlite")));
    }

    [Fact]
    public void ARestoreMovesTheOldStoreItsWalAndShmTogetherAndNeverDeletesThem()
    {
        AddMarker(Store, "kept");
        var backup = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b1"))).Value!;
        CommitOnlyToTheWal("later");

        var result = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.True(File.Exists(Backed(result.Value!, "store.sqlite-wal")));
        Assert.True(File.Exists(Backed(result.Value!, "store.sqlite-shm")));
        Assert.Equal(["kept", "later"], Markers(Backed(result.Value!, "store.sqlite")));
        Assert.False(File.Exists(Store + "-wal"));
        Assert.Equal(["kept"], Markers(Store));
    }

    [Fact]
    public void AStoreTooDamagedToBackUpIsMovedAsideAndAGoodBackupReplacesIt()
    {
        AddMarker(Store, "kept");
        var backup = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b1"))).Value!;
        File.WriteAllText(Store, "this is not a database");

        var result = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.Equal("this is not a database", File.ReadAllText(Backed(result.Value!, "store.sqlite")));
        Assert.Equal(["kept"], Markers(Store));
    }

    [RequiresSymbolicLinkFact]
    public void ABackupToAFolderReachedThroughASymlinkIntoAnOwnedFolderIsRefusedAndWritesNothing()
    {
        Directory.CreateDirectory(TrialRoot);
        var backups = Path.Combine(_root, "Koro.motif-backups");
        Directory.CreateDirectory(backups);
        var toTrial = Path.Combine(Made("links"), "to-trial");
        var toBackups = Path.Combine(Made("links"), "to-backups");
        var toStoreFolder = Path.Combine(Made("links"), "to-root");
        Directory.CreateSymbolicLink(toTrial, TrialRoot);
        Directory.CreateSymbolicLink(toBackups, backups);
        Directory.CreateSymbolicLink(toStoreFolder, _root);
        // A link to a link, and a link at the end of a --to that does not exist yet.
        var chained = Path.Combine(Made("links"), "chained");
        Directory.CreateSymbolicLink(chained, toTrial);

        foreach (var destination in new[]
            { Path.Combine(toTrial, "x"), toTrial, Path.Combine(toBackups, "mine"), Path.Combine(chained, "deep", "y"),
              Path.Combine(toStoreFolder, "Koro.motif.db.trial-exports", "z") })
        {
            var result = StoreBackupCommand.Backup(new StoreBackupRequest(Project, destination));
            Assert.Equal(RefusalCodes.StoreBackupDestinationOwned, result.Refusal?.Code);
        }
        Assert.Empty(Directory.GetFileSystemEntries(TrialRoot));
        Assert.Empty(Directory.GetFileSystemEntries(backups));
    }

    [RequiresSymbolicLinkFact]
    public void ABackupToAFolderBesideTheOwnedOnesThroughASymlinkIsStillAllowed()
    {
        var outside = Made("outside");
        var link = Path.Combine(Made("links"), "to-outside");
        Directory.CreateSymbolicLink(link, outside);

        var result = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Path.Combine(link, "b")));

        Assert.True(result.Succeeded, result.Refusal?.Message);
        Assert.True(File.Exists(Path.Combine(outside, "b", "store.sqlite")));
    }

    [Theory]
    [InlineData("trial")]
    [InlineData("backups")]
    [InlineData("trial-nested")]
    public void ABackupToAFolderInsideAnOwnedFolderIsRefusedAndWritesNothing(string where)
    {
        var destination = where switch
        {
            "trial" => TrialRoot,
            "backups" => Path.Combine(_root, "Koro.motif-backups", "mine"),
            _ => Path.Combine(TrialRoot, "deep", "x"),
        };

        var result = StoreBackupCommand.Backup(new StoreBackupRequest(Project, destination));

        Assert.Equal(RefusalCodes.StoreBackupDestinationOwned, result.Refusal?.Code);
        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public void ABackupBesideTheOwnedFoldersAndALaterBackupNeverContainAnEarlierOne()
    {
        WriteTrial("a.json", "A");
        var first = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b1")));
        var second = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b2")));

        Assert.True(first.Succeeded && second.Succeeded);
        Assert.Equal(first.Value!.Files.Select(file => file.Name), second.Value!.Files.Select(file => file.Name));
        Assert.DoesNotContain(second.Value.Files, file => file.Name.Contains("motif-backup", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(@"D:\Projects\Koro.motif.db.trial-exports", @"D:\Projects\Koro.motif.db.trial-exports\x", true, true)]
    [InlineData(@"D:\Projects\Koro.motif.db.trial-exports", @"d:\projects\KORO.motif.db.TRIAL-EXPORTS\x\y", true, true)]
    [InlineData(@"D:\Projects\Koro.motif.db.trial-exports", @"D:\Projects\Koro.motif.db.trial-exports-other", true, false)]
    [InlineData("/p/Koro.motif.db.trial-exports", "/p/koro.motif.db.trial-exports/x", false, false)]
    [InlineData("/p/Koro.motif.db.trial-exports", "/p/Koro.motif.db.trial-exports/x", false, true)]
    public void TheOwnedFolderCheckIsBySegmentAndFollowsTheFileSystemCaseRule(string owned, string path, bool windows, bool inside) =>
        Assert.Equal(inside, StoreBackupCommand.IsInside(path, owned, windows));

    [Theory]
    [InlineData("store.sqlite", true)]
    [InlineData("trial-exports/sub/a.json", true)]
    [InlineData("../evil", false)]
    [InlineData("trial-exports/../../evil", false)]
    [InlineData("/etc/passwd", false)]
    [InlineData("C:evil.txt", false)]
    [InlineData(@"C:\evil.txt", false)]
    [InlineData(@"\\server\share\x", false)]
    [InlineData(@"trial-exports\..\x", false)]
    [InlineData("CON:", false)]
    [InlineData("a//b", false)]
    [InlineData("name.", false)]
    [InlineData("a|b", false)]
    public void AReceiptNameIsAPlainRelativeNameOnEveryOs(string name, bool plain) =>
        Assert.Equal(plain, StoreBackupCommand.IsPlainRelativeName(name));

    [Theory]
    [InlineData("../evil")]
    [InlineData("C:evil.txt")]
    [InlineData(@"\\server\share\x")]
    [InlineData("trial-exports/a/../../../x")]
    public void AReceiptNamingAFileOutsideTheBackupIsInvalid(string name)
    {
        var backup = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b1"))).Value!;
        var tampered = backup with { Files = [.. backup.Files, new StoreBackupFile("trial-export", name, "", 1, "00")] };
        File.WriteAllText(backup.ReceiptPath, JsonSerializer.Serialize(tampered, ReceiptOptions));

        var result = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory));

        Assert.Equal(RefusalCodes.StoreRestoreInvalid, result.Refusal?.Code);
    }

    [Fact]
    public void ADuplicatedOrRenamedOrMissingOrGarbledReceiptIsInvalidAndTheStoreIsLeftAsItWas()
    {
        AddMarker(Store, "kept");
        var backup = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b1"))).Value!;
        var receiptText = File.ReadAllText(backup.ReceiptPath);
        AddMarker(Store, "later");

        void Refused(string why)
        {
            var result = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory));
            Assert.True(RefusalCodes.StoreRestoreInvalid == result.Refusal?.Code, why + ": " + result.Refusal?.Code);
            Assert.Equal(["kept", "later"], Markers(Store));
            Assert.Empty(Directory.GetFiles(_root, "*.restore-*"));
        }

        File.WriteAllText(backup.ReceiptPath, JsonSerializer.Serialize(
            backup with { Files = [.. backup.Files, backup.Files[0]] }, ReceiptOptions));
        Refused("duplicate");

        File.WriteAllText(backup.ReceiptPath, "{ not json");
        Refused("garbled");

        File.WriteAllText(backup.ReceiptPath, JsonSerializer.Serialize(backup with { ProjectIdentity = " " }, ReceiptOptions));
        Refused("no identity");

        File.WriteAllText(backup.ReceiptPath, receiptText);
        var renamedReceipt = Path.Combine(backup.BackupDirectory, "renamed.json");
        File.Move(backup.ReceiptPath, renamedReceipt);
        Refused("receipt renamed");
        var byRenamed = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, renamedReceipt));
        Assert.Equal(RefusalCodes.StoreRestoreInvalid, byRenamed.Refusal?.Code);
        File.Move(renamedReceipt, backup.ReceiptPath);

        var store = Path.Combine(backup.BackupDirectory, "store.sqlite");
        File.Move(store, store + ".old");
        Refused("store renamed");
        File.Move(store + ".old", store);

        Assert.True(StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory)).Succeeded);
    }

    [Fact]
    public void ABackupOfADifferentProjectGuidIsRefusedUnlessForcedButAMovedPathWithTheSameGuidIsAccepted()
    {
        File.WriteAllText(Project, $"<languageproject>\n<rt class=\"LangProject\" guid=\"{GuidA}\">\n</languageproject>");
        AddMarker(Store, "kept");
        var backup = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b1"))).Value!;
        Assert.Equal(GuidA, backup.ProjectGuid);
        Assert.Equal("Koro", backup.ProjectIdentity);
        AddMarker(Store, "later");

        // Same project, different recorded path: the path is only secondary.
        RewriteReceipt(backup, backup.SchemaVersion, projectPath: Path.Combine(_root, "Elsewhere", "Koro.fwdata"));
        var moved = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory));
        Assert.True(moved.Succeeded, moved.Refusal?.Message);
        Assert.Equal(["kept"], Markers(Store));

        // A different project: refused and untouched, then restored with force.
        AddMarker(Store, "again");
        File.WriteAllText(Project, $"<languageproject>\n<rt class=\"LangProject\" guid=\"{GuidB}\">\n</languageproject>");
        var refused = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory));
        Assert.Equal(RefusalCodes.StoreRestoreOtherProject, refused.Refusal?.Code);
        Assert.Contains(GuidA, refused.Refusal!.Message, StringComparison.Ordinal);
        Assert.Equal(["kept", "again"], Markers(Store));

        var forced = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory, Force: true));
        Assert.True(forced.Succeeded, forced.Refusal?.Message);
        Assert.Equal(["kept"], Markers(Store));
    }

    [Fact]
    public void WithoutAGuidTheProjectIdentityDecidesAndForceOverridesIt()
    {
        AddMarker(Store, "kept");
        var backup = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b1"))).Value!;
        Assert.Null(backup.ProjectGuid);
        File.WriteAllText(backup.ReceiptPath, JsonSerializer.Serialize(backup with { ProjectIdentity = "Other" }, ReceiptOptions));

        var refused = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory));
        Assert.Equal(RefusalCodes.StoreRestoreOtherProject, refused.Refusal?.Code);
        Assert.True(StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory, true)).Succeeded);
    }

    private sealed class SimulatedCrash : Exception;

    private static readonly string[] RestoreSteps =
        ["staged", "checkpointed", "owned-quarantined", "owned-swapped", "sidecars-moved", "old-store-moved", "store-swapped"];

    public static TheoryData<string> Steps() => new(RestoreSteps);

    private SqliteConnection? _walHolder;

    /// <summary>Commits a row held only in the WAL: auto-checkpoint off, connection kept open.</summary>
    private void CommitOnlyToTheWal(string marker)
    {
        _walHolder = Open(Store, SqliteOpenMode.ReadWrite);
        using (var pragma = _walHolder.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA wal_autocheckpoint = 0;";
            pragma.ExecuteNonQuery();
        }
        using var command = _walHolder.CreateCommand();
        command.CommandText = "INSERT INTO StoreRelocations (OldFwDataPath, NewFwDataPath, RelocatedUtc, Basis) " +
            "VALUES ($marker, 'x', 't', 'name');";
        command.Parameters.AddWithValue("$marker", marker);
        command.ExecuteNonQuery();
        Assert.True(new FileInfo(Store + "-wal").Length > 0, "the committed row must be only in the WAL");
    }

    private StoreBackupResponse BackupThenDiverge()
    {
        AddMarker(Store, "kept");
        File.WriteAllText(Config, "old-config");
        WriteTrial("a.json", "A1");
        var backup = StoreBackupCommand.Backup(new StoreBackupRequest(Project, Folder("b1"))).Value!;
        File.WriteAllText(Config, "new-config");
        WriteTrial("a.json", "A2");
        WriteTrial("c.json", "C");
        CommitOnlyToTheWal("later");
        return backup;
    }

    private static CommandOutcome<StoreRestoreResponse> RestoreStoppingAt(string step, Func<Exception> fail, ProjectLocatorArgs args)
    {
        StoreBackupCommand.StepHook = reached => { if (reached == step) throw fail(); };
        try { return StoreBackupCommand.Restore(new StoreRestoreRequest(args.Project, args.Backup)); }
        finally { StoreBackupCommand.StepHook = null; }
    }

    private sealed record ProjectLocatorArgs(string Project, string Backup);

    [Theory]
    [MemberData(nameof(Steps))]
    public void AProcessThatDiesAtAnyStepOfARestoreLeavesTheOldOrTheNewStoreCompleteAndARerunFinishesIt(string step)
    {
        var backup = BackupThenDiverge();

        Assert.Throws<SimulatedCrash>(() => RestoreStoppingAt(step, () => new SimulatedCrash(),
            new ProjectLocatorArgs(Project, backup.BackupDirectory)));

        var backups = Path.Combine(_root, "Koro.motif-backups");
        var moved = Directory.Exists(backups) ? Directory.GetDirectories(backups, "pre-restore-*") : [];
        // At no step is a sidecar left at the live path without a store beside it.
        if (!File.Exists(Store))
            Assert.All(new[] { "-wal", "-shm", "-journal" }, suffix => Assert.False(File.Exists(Store + suffix), suffix));
        switch (step)
        {
            case "sidecars-moved":
                // The WAL is aside and its committed row is in the live store: the checkpoint ran first.
                Assert.False(File.Exists(Store + "-wal"));
                Assert.True(File.Exists(Path.Combine(moved.Single(), "moved", "store.sqlite-wal")));
                Assert.Equal(["kept", "later"], Markers(Store));
                Assert.Equal(["ok"], Scalars(Store, "PRAGMA integrity_check;"));
                break;
            case "old-store-moved":
                // Between the two renames there is no store at the path: neither a half nor a mixed one.
                Assert.False(File.Exists(Store));
                Assert.Equal(["kept", "later"], Markers(Path.Combine(moved.Single(), "moved", "store.sqlite")));
                Assert.Equal(["kept"], Markers(Directory.GetFiles(_root, "Koro.motif.db.restore-*").Single()));
                break;
            case "store-swapped":
                Assert.Equal(["kept"], Markers(Store));
                break;
            default:
                Assert.Equal(["kept", "later"], Markers(Store));
                Assert.Equal(["ok"], Scalars(Store, "PRAGMA integrity_check;"));
                break;
        }

        var rerun = StoreBackupCommand.Restore(new StoreRestoreRequest(Project, backup.BackupDirectory));

        Assert.True(rerun.Succeeded, rerun.Refusal?.Message);
        // One pre-restore folder holds the whole old set, and nothing marks it unfinished.
        var folders = Directory.GetDirectories(backups, "pre-restore-*");
        Assert.Single(folders);
        Assert.False(File.Exists(Path.Combine(folders[0], "restore-incomplete")));
        Assert.Equal(folders[0], rerun.Value!.PreRestoreDirectory);
        Assert.Equal(["kept"], Markers(Store));
        Assert.Equal("old-config", File.ReadAllText(Config));
        Assert.Equal("A1", File.ReadAllText(Path.Combine(TrialRoot, "a.json")));
        Assert.False(File.Exists(Path.Combine(TrialRoot, "c.json")));
        Assert.Empty(Directory.GetFiles(_root, "*.restore-*"));
        Assert.Empty(Directory.GetFiles(TrialRoot, "*.restore-*"));
        Assert.True(ProjectStoreCommand.Run(Project, MotifProductVersion.CurrentText,
            (_, _) => CommandOutcome<string>.Success("opened")).Succeeded);
    }

    [Theory]
    [MemberData(nameof(Steps))]
    public void AFailureThatTheProcessSurvivesPutsEverythingBackAndNamesThePreRestoreFolder(string step)
    {
        var backup = BackupThenDiverge();

        var result = RestoreStoppingAt(step, () => new IOException("disk full"),
            new ProjectLocatorArgs(Project, backup.BackupDirectory));

        Assert.Equal(RefusalCodes.ProjectStoreIo, result.Refusal?.Code);
        Assert.Equal(["kept", "later"], Markers(Store));
        Assert.Equal(["ok"], Scalars(Store, "PRAGMA integrity_check;"));
        Assert.Equal("new-config", File.ReadAllText(Config));
        Assert.Equal("A2", File.ReadAllText(Path.Combine(TrialRoot, "a.json")));
        Assert.Equal("C", File.ReadAllText(Path.Combine(TrialRoot, "c.json")));
        Assert.True(File.Exists(Store + "-wal"));
        Assert.Empty(Directory.GetFiles(_root, "*.restore-*"));
        if (step is not ("staged" or "checkpointed")) Assert.Contains("pre-restore-", result.Refusal!.Message, StringComparison.Ordinal);
        Assert.True(ProjectStoreCommand.Run(Project, MotifProductVersion.CurrentText,
            (_, _) => CommandOutcome<string>.Success("opened")).Succeeded);
    }

    private static readonly JsonSerializerOptions ReceiptOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private static void RewriteReceipt(StoreBackupResponse backup, int schema, string? projectPath = null)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        var files = backup.Files.Select(file => file with
        {
            Bytes = new FileInfo(file.Path).Length,
            Sha256 = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file.Path))),
        }).ToList();
        File.WriteAllText(backup.ReceiptPath, JsonSerializer.Serialize(
            backup with { SchemaVersion = schema, ProjectPath = projectPath ?? backup.ProjectPath, Files = files }, options));
    }

    private static void AddMarker(string store, string marker)
    {
        using var connection = Open(store, SqliteOpenMode.ReadWrite);
        using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO StoreRelocations (OldFwDataPath, NewFwDataPath, RelocatedUtc, Basis) " +
            "VALUES ($marker, 'x', 't', 'name');";
        command.Parameters.AddWithValue("$marker", marker);
        command.ExecuteNonQuery();
    }

    private static void SetSchema(string store, int schema)
    {
        using var connection = Open(store, SqliteOpenMode.ReadWrite);
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA user_version = {schema};";
        command.ExecuteNonQuery();
    }

    private static int ReadSchema(string store) => int.Parse(Scalars(store, "PRAGMA user_version;").Single());

    private static List<string> Markers(string store) =>
        Scalars(store, "SELECT OldFwDataPath FROM StoreRelocations ORDER BY RelocationId;");

    private static List<string> Scalars(string store, string sql)
    {
        using var connection = Open(store, SqliteOpenMode.ReadOnly);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var values = new List<string>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) values.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!);
        return values;
    }

    private static SqliteConnection Open(string store, SqliteOpenMode mode)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = store, Mode = mode, Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    public void Dispose()
    {
        _walHolder?.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
