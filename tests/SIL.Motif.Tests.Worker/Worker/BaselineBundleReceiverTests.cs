using System.Buffers.Binary;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SIL.Motif.Contract.Baselines;
using SIL.Motif.Tests.TestFixtures;
using SIL.Motif.Worker.Baselines;
using Xunit;

namespace SIL.Motif.Tests.Worker;

public sealed class BaselineBundleReceiverTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "SIL.Motif.BaselineReceiverTests",
        Guid.NewGuid().ToString("N"));

    public BaselineBundleReceiverTests() => Directory.CreateDirectory(_root);

    [Fact]
    public async Task PublishVerifiedAsync_PublishesAllowedLayoutAndDeletesTransport()
    {
        var transfer = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));
        var target = Target();
        var token = Token(transfer.Sha256);

        var publication = await new BaselineBundleReceiver().PublishVerifiedAsync(
            transfer, token, target, CancellationToken.None);

        Assert.False(File.Exists(transfer.TemporaryPath));
        Assert.True(Directory.Exists(publication.RootDirectory));
        Assert.Equal(Path.Combine(publication.RootDirectory, "project.fwdata"), publication.FwDataPath);
        Assert.Equal("model", File.ReadAllText(publication.FwDataPath));
        Assert.Equal("<ldml/>", File.ReadAllText(Path.Combine(
            publication.RootDirectory, "WritingSystemStore", "en.ldml")));
    }

    [Fact]
    public async Task PublishVerifiedAsync_IsIdempotentForTheSameBundleDigest()
    {
        var first = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));
        var target = Target();
        var receiver = new BaselineBundleReceiver();
        var token = Token(first.Sha256);
        var published = await receiver.PublishVerifiedAsync(first, token, target, CancellationToken.None);
        var second = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));

        var retried = await receiver.PublishVerifiedAsync(second, token, target, CancellationToken.None);

        Assert.Equal(published, retried);
        Assert.False(File.Exists(second.TemporaryPath));
    }

    [Theory]
    [InlineData("../escape.fwdata")]
    [InlineData("LinkedFiles/media.wav")]
    [InlineData("project.motif.db")]
    [InlineData("project.fwbackup")]
    [InlineData("unrelated.txt")]
    [InlineData("WritingSystemStore/nested/en.ldml")]
    [InlineData("/absolute.fwdata")]
    [InlineData("C:/absolute.fwdata")]
    [InlineData("WritingSystemStore\\en.ldml")]
    public async Task PublishVerifiedAsync_RejectsEntriesOutsideTheExactAllowlist(string entry)
    {
        var transfer = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"), (entry, "forbidden"));

        await Assert.ThrowsAsync<InvalidDataException>(() => new BaselineBundleReceiver().PublishVerifiedAsync(
            transfer, Token(transfer.Sha256), Target(),
            CancellationToken.None));

        Assert.False(File.Exists(transfer.TemporaryPath));
        Assert.False(File.Exists(Path.Combine(_root, "escape.fwdata")));
    }

    [Fact]
    public async Task PublishVerifiedAsync_RejectsCaseCollidingEntries()
    {
        var transfer = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "one"), ("WritingSystemStore/EN.LDML", "two"));

        await Assert.ThrowsAsync<InvalidDataException>(() => new BaselineBundleReceiver().PublishVerifiedAsync(
            transfer, Token(transfer.Sha256), Target(), CancellationToken.None));
    }

    [Fact]
    public async Task PublishVerifiedAsync_RejectsArchiveLinkMetadata()
    {
        var transfer = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));
        using (var stream = new FileStream(transfer.TemporaryPath, FileMode.Open, FileAccess.ReadWrite))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Update))
            archive.GetEntry("WritingSystemStore/en.ldml")!.ExternalAttributes = 0xA1FF << 16;
        transfer = VerifiedTransfer(transfer.TemporaryPath);

        await Assert.ThrowsAsync<InvalidDataException>(() => new BaselineBundleReceiver().PublishVerifiedAsync(
            transfer, Token(transfer.Sha256), Target(), CancellationToken.None));
    }

    [Fact]
    public async Task PublishVerifiedAsync_EnforcesEntryAndExpansionBounds()
    {
        var tooMany = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "one"), ("WritingSystemStore/fr.ldml", "two"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new BaselineBundleReceiver(2).PublishVerifiedAsync(
            tooMany, Token(tooMany.Sha256), Target(), CancellationToken.None));

        var tooLarge = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new BaselineBundleReceiver(
            maximumExtractedBytes: 8).PublishVerifiedAsync(
            tooLarge, Token(tooLarge.Sha256), Target(), CancellationToken.None));
    }

    [Theory]
    [InlineData(4097)]
    [InlineData(ushort.MaxValue)]
    public async Task PublishVerifiedAsync_RejectsHighOrZip64EocdCountBeforeArchiveMaterialization(
        int declaredCount)
    {
        var transfer = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));
        var bytes = File.ReadAllBytes(transfer.TemporaryPath);
        var eocd = bytes.Length - 22;
        Assert.Equal(0x06054b50u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(eocd, 4)));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(eocd + 8, 2), (ushort)declaredCount);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(eocd + 10, 2), (ushort)declaredCount);
        File.WriteAllBytes(transfer.TemporaryPath, bytes);
        transfer = VerifiedTransfer(transfer.TemporaryPath);
        var materialized = false;
        var receiver = new BaselineBundleReceiver(
            beforeArchiveMaterialization: () => materialized = true);

        await Assert.ThrowsAsync<InvalidDataException>(() => receiver.PublishVerifiedAsync(
            transfer, Token(transfer.Sha256), Target(), CancellationToken.None));

        Assert.False(materialized);
    }

    [Fact]
    public async Task PublishVerifiedAsync_RejectsActualHighCountHiddenByForgedEocdBeforeMaterialization()
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".ready");
        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("project.fwdata").Open()))
                writer.Write("model");
            for (var index = 0; index < 4096; index++)
            {
                using var writer = new StreamWriter(archive.CreateEntry(
                    $"WritingSystemStore/ws-{index:D4}.ldml").Open());
                writer.Write("<ldml/>");
            }
        }
        var bytes = File.ReadAllBytes(path);
        var eocd = bytes.Length - 22;
        Assert.Equal(0x06054b50u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(eocd, 4)));
        Assert.Equal(4097, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(eocd + 8, 2)));
        Assert.Equal(4097, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(eocd + 10, 2)));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(eocd + 8, 2), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(eocd + 10, 2), 2);
        File.WriteAllBytes(path, bytes);
        var transfer = VerifiedTransfer(path);
        var materialized = false;
        var receiver = new BaselineBundleReceiver(
            beforeArchiveMaterialization: () => materialized = true);

        await Assert.ThrowsAsync<InvalidDataException>(() => receiver.PublishVerifiedAsync(
            transfer, Token(transfer.Sha256), Target(), CancellationToken.None));

        Assert.False(materialized);
    }

    [Fact]
    public void ExtractedLengthValidationRejectsHostileOverflowAsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() =>
            BaselineBundleReceiver.AddExtractedLength(long.MaxValue, 1, long.MaxValue));
    }

    [Fact]
    public async Task PublishVerifiedAsync_RejectsAChangedExistingPublication()
    {
        var first = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));
        var receiver = new BaselineBundleReceiver();
        var token = Token(first.Sha256);
        var publication = await receiver.PublishVerifiedAsync(first, token, Target(), CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(publication.RootDirectory, "WritingSystemStore", "nested"));
        var retry = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => receiver.PublishVerifiedAsync(
            retry, token, Target(), CancellationToken.None));

        Assert.Contains("WritingSystemStore", exception.Message, StringComparison.Ordinal);
        Assert.Contains("nested", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(publication.RootDirectory, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishVerifiedAsync_ReportsAnUnexpectedExistingRootEntryByNameOnly()
    {
        var first = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));
        var receiver = new BaselineBundleReceiver();
        var token = Token(first.Sha256);
        var publication = await receiver.PublishVerifiedAsync(first, token, Target(), CancellationToken.None);
        const string extraEntry = "unrelated.txt";
        File.WriteAllText(Path.Combine(publication.RootDirectory, extraEntry), "held");
        var retry = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => receiver.PublishVerifiedAsync(
            retry, token, Target(), CancellationToken.None));

        Assert.Contains("root allowlist", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(extraEntry, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(publication.RootDirectory, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PublishVerifiedAsync_RejectsAStaleFwDataLockMarker()
    {
        var first = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));
        var receiver = new BaselineBundleReceiver();
        var token = Token(first.Sha256);
        var publication = await receiver.PublishVerifiedAsync(first, token, Target(), CancellationToken.None);
        const string lockName = "project.fwdata.lock";
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var staleMarker = System.Text.Json.JsonSerializer.Serialize(new
        {
            __type = "FileLockContent:#Palaso.IO.FileLock",
            PID = process.Id,
            ProcessName = process.ProcessName,
            Timestamp = process.StartTime.Ticks - 1
        });
        File.WriteAllText(Path.Combine(publication.RootDirectory, lockName), staleMarker);
        var retry = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => receiver.PublishVerifiedAsync(
            retry, token, Target(), CancellationToken.None));

        Assert.Contains(".fwdata.lock", exception.Message, StringComparison.Ordinal);
        Assert.Contains("live LibLCM owner", exception.Message, StringComparison.Ordinal);
        Assert.Contains(lockName, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(publication.RootDirectory, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [RequiresSymbolicLinkFact]
    public async Task PublishVerifiedAsync_RejectsAReparsePointInAnExistingPublication()
    {
        var first = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));
        var receiver = new BaselineBundleReceiver();
        var token = Token(first.Sha256);
        var publication = await receiver.PublishVerifiedAsync(first, token, Target(), CancellationToken.None);
        var writingSystems = Path.Combine(publication.RootDirectory, "WritingSystemStore");
        Directory.Delete(writingSystems, true);
        var outside = Path.Combine(_root, "outside-writing-systems");
        Directory.CreateDirectory(outside);
        File.WriteAllText(Path.Combine(outside, "en.ldml"), "outside");
        Directory.CreateSymbolicLink(writingSystems, outside);
        var retry = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));

        await Assert.ThrowsAsync<InvalidDataException>(() => receiver.PublishVerifiedAsync(
            retry, token, Target(), CancellationToken.None));
        Assert.True(File.Exists(Path.Combine(outside, "en.ldml")));
    }

    [RequiresSymbolicLinkFact]
    public async Task PublishVerifiedAsync_RejectsAReparsePointFwDataInAnExistingPublication()
    {
        var first = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));
        var receiver = new BaselineBundleReceiver();
        var token = Token(first.Sha256);
        var publication = await receiver.PublishVerifiedAsync(first, token, Target(), CancellationToken.None);
        File.Delete(publication.FwDataPath);
        var outside = Path.Combine(_root, "outside.fwdata");
        File.WriteAllText(outside, "outside");
        File.CreateSymbolicLink(publication.FwDataPath, outside);
        var retry = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));

        await Assert.ThrowsAsync<InvalidDataException>(() => receiver.PublishVerifiedAsync(
            retry, token, Target(), CancellationToken.None));
        Assert.Equal("outside", File.ReadAllText(outside));
    }

    [RequiresSymbolicLinkFact]
    public async Task PublishVerifiedAsync_RejectsAReparsePointInSharedSettingsOfAnExistingPublication()
    {
        var first = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));
        var receiver = new BaselineBundleReceiver();
        var token = Token(first.Sha256);
        var publication = await receiver.PublishVerifiedAsync(first, token, Target(), CancellationToken.None);
        var sharedSettings = Path.Combine(publication.RootDirectory, "SharedSettings");
        var outside = Path.Combine(_root, "outside-shared-settings.txt");
        File.WriteAllText(outside, "outside");
        File.CreateSymbolicLink(Path.Combine(sharedSettings, "link.txt"), outside);
        var retry = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));

        await Assert.ThrowsAsync<InvalidDataException>(() => receiver.PublishVerifiedAsync(
            retry, token, Target(), CancellationToken.None));
        Assert.Equal("outside", File.ReadAllText(outside));
    }

    [Fact]
    public async Task PublishVerifiedAsync_RejectsASubdirectoryInSharedSettingsOfAnExistingPublication()
    {
        var first = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));
        var receiver = new BaselineBundleReceiver();
        var token = Token(first.Sha256);
        var publication = await receiver.PublishVerifiedAsync(first, token, Target(), CancellationToken.None);
        Directory.CreateDirectory(Path.Combine(publication.RootDirectory, "SharedSettings", "nested"));
        var retry = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));

        await Assert.ThrowsAsync<InvalidDataException>(() => receiver.PublishVerifiedAsync(
            retry, token, Target(), CancellationToken.None));
    }

    [Fact]
    public async Task PublishVerifiedAsync_ValidatesAnEmptySharedSettingsInAnExistingPublication()
    {
        var first = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));
        var receiver = new BaselineBundleReceiver();
        var token = Token(first.Sha256);
        var publication = await receiver.PublishVerifiedAsync(first, token, Target(), CancellationToken.None);
        var sharedSettings = Path.Combine(publication.RootDirectory, "SharedSettings");
        Assert.True(Directory.Exists(sharedSettings));
        Assert.Empty(Directory.GetFileSystemEntries(sharedSettings));
        var retry = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));

        var retried = await receiver.PublishVerifiedAsync(retry, token, Target(), CancellationToken.None);

        Assert.Equal(publication, retried);
    }

    [Fact]
    public async Task PublishVerifiedAsync_RejectsAnotherProjectIdentity()
    {
        var transfer = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));

        await Assert.ThrowsAsync<InvalidDataException>(() => new BaselineBundleReceiver().PublishVerifiedAsync(
            transfer, Token(transfer.Sha256),
            new BaselinePublicationTarget(Path.Combine(_root, "managed"), "other-project"),
            CancellationToken.None));
    }

    [Fact]
    public async Task PublishVerifiedAsync_RequiresOneFwDataAndAtLeastOneWritingSystem()
    {
        var noWritingSystem = CreateTransfer(("project.fwdata", "model"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new BaselineBundleReceiver().PublishVerifiedAsync(
            noWritingSystem, Token(noWritingSystem.Sha256),
            Target(), CancellationToken.None));

        var twoProjects = CreateTransfer(("first.fwdata", "one"), ("second.fwdata", "two"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));
        await Assert.ThrowsAsync<InvalidDataException>(() => new BaselineBundleReceiver().PublishVerifiedAsync(
            twoProjects, Token(twoProjects.Sha256),
            Target(), CancellationToken.None));
    }

    [Fact]
    public async Task PublishVerifiedAsync_RejectsDeclaredBundleDigestMismatch()
    {
        var transfer = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));

        await Assert.ThrowsAsync<InvalidDataException>(() => new BaselineBundleReceiver().PublishVerifiedAsync(
            transfer, Token(new string('0', 64)),
            Target(), CancellationToken.None));

        Assert.False(File.Exists(transfer.TemporaryPath));
    }

    [Fact]
    public async Task PublishVerifiedAsync_ReclaimsOnlyExactCrashLeftoverIncomingDirectories()
    {
        var target = Target();
        Directory.CreateDirectory(target.BaselineRoot);
        var leftover = Path.Combine(target.BaselineRoot, ".incoming-" + new string('a', 32));
        Directory.CreateDirectory(Path.Combine(leftover, "WritingSystemStore"));
        File.WriteAllText(Path.Combine(leftover, "project.fwdata"), "partial");
        File.WriteAllText(Path.Combine(leftover, "WritingSystemStore", "en.ldml"), "partial");
        var unrelated = Path.Combine(target.BaselineRoot, ".incoming-not-owned");
        Directory.CreateDirectory(unrelated);
        File.WriteAllText(Path.Combine(unrelated, "keep.txt"), "keep");
        var transfer = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));

        await new BaselineBundleReceiver().PublishVerifiedAsync(
            transfer, Token(transfer.Sha256), target, CancellationToken.None);

        Assert.False(Directory.Exists(leftover));
        Assert.Equal("keep", File.ReadAllText(Path.Combine(unrelated, "keep.txt")));
    }

    [Fact]
    public async Task PublishVerifiedAsync_DoesNotReclaimAnActivePublicationForTheSameRoot()
    {
        var target = Target();
        var first = CreateTransfer(("project.fwdata", "first"),
            ("WritingSystemStore/en.ldml", "<first/>"));
        var second = CreateTransfer(("project.fwdata", "second"),
            ("WritingSystemStore/en.ldml", "<second/>"));
        Assert.NotEqual(first.Sha256, second.Sha256);
        var firstIncomingReady = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirst = new ManualResetEventSlim();
        var firstReceiver = new BaselineBundleReceiver(beforePublicationMove: _ =>
        {
            firstIncomingReady.TrySetResult(Directory.GetDirectories(
                target.BaselineRoot, ".incoming-*", SearchOption.TopDirectoryOnly).Single());
            if (!releaseFirst.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The held Baseline publication was not released.");
        });
        var firstTask = Task.Run(() => firstReceiver.PublishVerifiedAsync(
            first, Token(first.Sha256), target, CancellationToken.None));
        Task<BaselinePublication>? secondTask = null;
        string? firstIncoming = null;
        Exception? firstFailure = null;
        Exception? secondFailure = null;

        try
        {
            firstIncoming = await firstIncomingReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
            secondTask = new BaselineBundleReceiver().PublishVerifiedAsync(
                second, Token(second.Sha256), target, CancellationToken.None);

            Assert.True(Directory.Exists(firstIncoming), "The second receiver reclaimed the active incoming directory.");
            Assert.False(secondTask.IsCompleted, "A same-root publication completed while the first was held.");
        }
        finally
        {
            releaseFirst.Set();
            firstFailure = await ObserveAsync(firstTask);
            if (secondTask is not null)
                secondFailure = await ObserveAsync(secondTask);
        }

        Assert.Null(firstFailure);
        Assert.Null(secondFailure);
        Assert.NotNull(firstIncoming);
        var firstPublication = await firstTask;
        var secondPublication = await secondTask!;
        Assert.Equal("first", File.ReadAllText(firstPublication.FwDataPath));
        Assert.Equal("second", File.ReadAllText(secondPublication.FwDataPath));
        Assert.False(File.Exists(first.TemporaryPath));
        Assert.False(File.Exists(second.TemporaryPath));
        Assert.Empty(Directory.GetDirectories(target.BaselineRoot, ".incoming-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task PublishVerifiedAsync_CancelsAWaiterWithoutRemovingTheOwnerIncomingDirectory()
    {
        var target = Target();
        var first = CreateTransfer(("project.fwdata", "first"),
            ("WritingSystemStore/en.ldml", "<first/>"));
        var waiting = CreateTransfer(("project.fwdata", "waiting"),
            ("WritingSystemStore/en.ldml", "<waiting/>"));
        var firstIncomingReady = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirst = new ManualResetEventSlim();
        using var cancelWaiter = new CancellationTokenSource();
        var firstReceiver = new BaselineBundleReceiver(beforePublicationMove: _ =>
        {
            firstIncomingReady.TrySetResult(Directory.GetDirectories(
                target.BaselineRoot, ".incoming-*", SearchOption.TopDirectoryOnly).Single());
            if (!releaseFirst.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The held Baseline publication was not released.");
        });
        var firstTask = Task.Run(() => firstReceiver.PublishVerifiedAsync(
            first, Token(first.Sha256), target, CancellationToken.None));
        Task<BaselinePublication>? waiterTask = null;
        string? firstIncoming = null;
        Exception? firstFailure = null;
        Exception? waiterFailure = null;

        try
        {
            firstIncoming = await firstIncomingReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
            waiterTask = new BaselineBundleReceiver().PublishVerifiedAsync(
                waiting, Token(waiting.Sha256), target, cancelWaiter.Token);
            Assert.False(waiterTask.IsCompleted, "A publication waiter should remain pending while its root is owned.");
            cancelWaiter.Cancel();
            waiterFailure = await ObserveAsync(waiterTask);
            Assert.True(Directory.Exists(firstIncoming), "Cancelling a waiter removed the active incoming directory.");
        }
        finally
        {
            releaseFirst.Set();
            firstFailure = await ObserveAsync(firstTask);
            if (waiterTask is not null && waiterFailure is null)
                waiterFailure = await ObserveAsync(waiterTask);
        }

        Assert.Null(firstFailure);
        Assert.IsAssignableFrom<OperationCanceledException>(waiterFailure);
        Assert.NotNull(firstIncoming);
        var publication = await firstTask;
        Assert.Equal("first", File.ReadAllText(publication.FwDataPath));
        Assert.False(File.Exists(first.TemporaryPath));
        Assert.False(File.Exists(waiting.TemporaryPath));
        Assert.Empty(Directory.GetDirectories(target.BaselineRoot, ".incoming-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task PublishVerifiedAsync_AllowsDifferentRootsToPublishIndependently()
    {
        var firstTarget = Target();
        var secondTarget = new BaselinePublicationTarget(Path.Combine(_root, "other-managed", "baselines"),
            "project-id");
        var first = CreateTransfer(("project.fwdata", "first"),
            ("WritingSystemStore/en.ldml", "<first/>"));
        var second = CreateTransfer(("project.fwdata", "second"),
            ("WritingSystemStore/en.ldml", "<second/>"));
        var firstIncomingReady = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondReachedMove = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseFirst = new ManualResetEventSlim();
        using var releaseSecond = new ManualResetEventSlim();
        var firstReceiver = new BaselineBundleReceiver(beforePublicationMove: _ =>
        {
            firstIncomingReady.TrySetResult(Directory.GetDirectories(
                firstTarget.BaselineRoot, ".incoming-*", SearchOption.TopDirectoryOnly).Single());
            if (!releaseFirst.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The held Baseline publication was not released.");
        });
        var firstTask = Task.Run(() => firstReceiver.PublishVerifiedAsync(
            first, Token(first.Sha256), firstTarget, CancellationToken.None));
        Task<BaselinePublication>? secondTask = null;
        string? firstIncoming = null;
        Exception? firstFailure = null;
        Exception? secondFailure = null;

        try
        {
            firstIncoming = await firstIncomingReady.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var secondReceiver = new BaselineBundleReceiver(beforePublicationMove: _ =>
            {
                secondReachedMove.TrySetResult();
                if (!releaseSecond.Wait(TimeSpan.FromSeconds(10)))
                    throw new TimeoutException("The second Baseline publication was not released.");
            });
            secondTask = Task.Run(() => secondReceiver.PublishVerifiedAsync(
                second, Token(second.Sha256), secondTarget, CancellationToken.None));
            await secondReachedMove.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(secondTask.IsCompleted, "The second publication hook should precede its directory move.");
            Assert.True(Directory.Exists(firstIncoming));
        }
        finally
        {
            releaseFirst.Set();
            releaseSecond.Set();
            firstFailure = await ObserveAsync(firstTask);
            if (secondTask is not null)
                secondFailure = await ObserveAsync(secondTask);
        }

        Assert.Null(firstFailure);
        Assert.Null(secondFailure);
        var firstPublication = await firstTask;
        var secondPublication = await secondTask!;
        Assert.Equal("first", File.ReadAllText(firstPublication.FwDataPath));
        Assert.Equal("second", File.ReadAllText(secondPublication.FwDataPath));
        Assert.False(File.Exists(first.TemporaryPath));
        Assert.False(File.Exists(second.TemporaryPath));
        Assert.Empty(Directory.GetDirectories(firstTarget.BaselineRoot, ".incoming-*", SearchOption.TopDirectoryOnly));
        Assert.Empty(Directory.GetDirectories(secondTarget.BaselineRoot, ".incoming-*", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public async Task PublishVerifiedAsync_PreservesPublicationHookExceptionAndRecordsItsPhase()
    {
        var transfer = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));
        const int expectedHResult = unchecked((int)0x80070020);
        var expected = new IOException("controlled publication failure", expectedHResult);
        var receiver = new BaselineBundleReceiver(beforePublicationMove: _ => throw expected);

        var actual = await Assert.ThrowsAsync<IOException>(() => receiver.PublishVerifiedAsync(
            transfer, Token(transfer.Sha256), Target(), CancellationToken.None));

        Assert.Same(expected, actual);
        Assert.Equal(expectedHResult, actual.HResult);
        Assert.Equal("publication-move", actual.Data["baselinePublicationPhase"]);
        Assert.False(File.Exists(transfer.TemporaryPath));
    }

    [RequiresSymbolicLinkFact]
    public async Task PublishVerifiedAsync_RefusesAReparseIncomingDirectoryWithoutLeavingItsRoot()
    {
        var target = Target();
        Directory.CreateDirectory(target.BaselineRoot);
        var outside = Path.Combine(_root, "outside-incoming");
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "sentinel.fwdata");
        File.WriteAllText(sentinel, "keep");
        var link = Path.Combine(target.BaselineRoot, ".incoming-" + new string('b', 32));
        Directory.CreateSymbolicLink(link, outside);
        var transfer = CreateTransfer(("project.fwdata", "model"),
            ("WritingSystemStore/en.ldml", "<ldml/>"));

        await Assert.ThrowsAsync<InvalidDataException>(() => new BaselineBundleReceiver().PublishVerifiedAsync(
            transfer, Token(transfer.Sha256), target, CancellationToken.None));

        Assert.Equal("keep", File.ReadAllText(sentinel));
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, true); }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    // Zip stamps have two-second granularity, so a retry built moments later must not hash differently.
    private static readonly DateTimeOffset FixedEntryStamp = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private VerifiedBinaryTransfer CreateTransfer(params (string Name, string Content)[] entries)
    {
        var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".ready");
        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Name);
                entry.LastWriteTime = FixedEntryStamp;
                using var writer = new StreamWriter(entry.Open());
                writer.Write(item.Content);
            }
        }
        return VerifiedTransfer(path);
    }

    private static VerifiedBinaryTransfer VerifiedTransfer(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new VerifiedBinaryTransfer(Path.GetFileNameWithoutExtension(path), path, bytes.Length, sha256);
    }

    private static BaselineToken Token(string bundleDigest) => new(
        "project-id", "sha256:" + new string('1', 64), "projection-v1", "2026-08-23T00:00:00Z",
        "sha256:" + bundleDigest);

    private BaselinePublicationTarget Target() => new(Path.Combine(_root, "managed"), "project-id");

    private static async Task<Exception?> ObserveAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(10));
            return null;
        }
        catch (Exception exception)
        {
            if (exception is TimeoutException)
                _ = task.ContinueWith(static completed => _ = completed.Exception, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            return exception;
        }
    }
}
