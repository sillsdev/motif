using SIL.Motif.App.Services;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Pins the coalesced, off-UI-thread preference save that keeps zoom steps from fsyncing on the UI thread.</summary>
public sealed class DelayedPreferenceSaveTests
{
    private static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(150);

    [Fact]
    public void RapidChangesAreOneWriteAndTheSnapshotChangesAtOnce()
    {
        using var folder = new TemporaryFolder();
        var path = folder.File("user-preferences.json");
        var store = folder.Store("user-preferences.json", TimeSpan.FromMinutes(5));
        var steps = new[] { 90, 100, 110, 125, 150 };

        foreach (var step in steps)
        {
            var result = store.Update(p => p with { ZoomPercent = step });
            Assert.Equal(PreferenceWriteState.Saved, result.State);
            Assert.Equal(step, store.Current.ZoomPercent);
        }

        Assert.Equal(0, store.WriteCount);
        Assert.False(File.Exists(path));
        store.Flush();

        Assert.Equal(1, store.WriteCount);
        Assert.Equal(150, new FileUserPreferencesStore(path).Current.ZoomPercent);
    }

    [Fact]
    public void FlushWritesPendingChangesBeforeTheDelayExpires()
    {
        using var folder = new TemporaryFolder();
        var path = folder.File("user-preferences.json");
        var store = folder.Store("user-preferences.json", TimeSpan.FromMinutes(5));
        store.Update(p => p with { Theme = UserTheme.Dark });
        store.Update(p => p with { ZoomPercent = 125 });

        store.Flush();

        var saved = new FileUserPreferencesStore(path).Current;
        Assert.Equal(UserTheme.Dark, saved.Theme);
        Assert.Equal(125, saved.ZoomPercent);
        Assert.Equal(1, store.WriteCount);
        store.Flush();
        Assert.Equal(1, store.WriteCount);
    }

    [Fact]
    public async Task LastValueWinsWhenChangesArriveDuringAnInFlightWrite()
    {
        using var folder = new TemporaryFolder();
        var path = folder.File("user-preferences.json");
        var store = folder.Store("user-preferences.json", TimeSpan.FromMilliseconds(1));

        for (var round = 0; round < 40; round++)
        {
            var zoom = round % 2 == 0 ? 90 : 150;
            store.Update(p => p with { ZoomPercent = zoom });
            if (round % 7 == 0) await Task.Delay(3);
        }

        store.Flush();
        await Task.Delay(50);
        store.Flush();

        Assert.Equal(150, store.Current.ZoomPercent);
        Assert.Equal(150, new FileUserPreferencesStore(path).Current.ZoomPercent);
    }

    [Fact]
    public void DelayedChangesMergeWithAnotherProcessesFieldsAtWriteTime()
    {
        using var folder = new TemporaryFolder();
        var path = folder.File("user-preferences.json");
        var ours = folder.Store("user-preferences.json", TimeSpan.FromMinutes(5));
        var other = new FileUserPreferencesStore(path);
        ours.Update(p => p with { Theme = UserTheme.Dark });
        other.Update(p => p with { Trace = TracePresentation.Expert });

        ours.Flush();

        var saved = new FileUserPreferencesStore(path).Current;
        Assert.Equal(UserTheme.Dark, saved.Theme);
        Assert.Equal(TracePresentation.Expert, saved.Trace);
    }

    [Fact]
    public void RefusedFileKeepsEditsAcceptedAfterTheWriterDrainsItsBatch()
    {
        using var folder = new TemporaryFolder();
        var path = folder.File("user-preferences.json");
        using var batchDrained = new ManualResetEventSlim();
        using var resumeWriter = new ManualResetEventSlim();
        using var store = new FileUserPreferencesStore(path, TimeSpan.FromMinutes(5), () =>
        {
            batchDrained.Set();
            if (!resumeWriter.Wait(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("The test did not release the drained preference batch.");
        });
        store.Update(p => p with { Theme = UserTheme.Dark });
        const string incompatible = "{\"FormatVersion\":2}";
        File.WriteAllText(path, incompatible);
        Exception? failure = null;
        var writer = new Thread(() => failure = Record.Exception(store.Flush)) { IsBackground = true };
        writer.Start();
        try
        {
            Assert.True(batchDrained.Wait(TimeSpan.FromSeconds(10)));
            store.Update(p => p with { ZoomPercent = 150 });
            Assert.Equal(150, store.Current.ZoomPercent);
        }
        finally
        {
            resumeWriter.Set();
            Assert.True(writer.Join(TimeSpan.FromSeconds(10)));
        }

        Assert.Null(failure);
        var accepted = UserPreferences.Defaults with { Theme = UserTheme.Dark, ZoomPercent = 150 };
        Assert.Equal(accepted, store.Current);
        Assert.Equal(PreferenceState.Refused, store.State);
        Assert.Equal("preferences.refused", store.Problem?.Code);
        Assert.Equal(incompatible, File.ReadAllText(path));
        store.Flush();
        Assert.Equal(accepted, store.Current);
        Assert.Equal(PreferenceState.Refused, store.State);
        Assert.Equal("preferences.refused", store.Problem?.Code);
        Assert.Equal(0, store.WriteCount);
        Assert.Equal(incompatible, File.ReadAllText(path));
    }

    [Fact]
    public void AReplayThatFailsValidationIsReportedNotThrownAndKeepsTheSessionValue()
    {
        using var folder = new TemporaryFolder();
        var path = folder.File("user-preferences.json");
        var ours = folder.Store("user-preferences.json", TimeSpan.FromMinutes(5));
        var changes = 0;
        ours.Changed += (_, _) => changes++;
        ours.Update(p => p.Trace == TracePresentation.Expert
            ? throw new InvalidDataException("The other process changed the file under this edit.")
            : p with { Theme = UserTheme.Dark });
        var before = changes;
        folder.Store("other.json").Update(p => p);
        new FileUserPreferencesStore(path).Update(p => p with { Trace = TracePresentation.Expert });

        var failure = Record.Exception(ours.Flush);

        Assert.Null(failure);
        Assert.Equal(PreferenceState.Unavailable, ours.State);
        Assert.NotNull(ours.Problem);
        Assert.Equal(UserTheme.Dark, ours.Current.Theme);
        Assert.Equal(before + 1, changes);
        Assert.Equal(TracePresentation.Expert, new FileUserPreferencesStore(path).Current.Trace);
        Assert.Equal(UserTheme.System, new FileUserPreferencesStore(path).Current.Theme);
    }

    [Fact]
    public void ResetDropsChangesStillWaiting()
    {
        using var folder = new TemporaryFolder();
        var path = folder.File("user-preferences.json");
        var store = folder.Store("user-preferences.json", TimeSpan.FromMinutes(5));
        store.Update(p => p with { Theme = UserTheme.Dark });

        store.Reset();
        store.Flush();

        Assert.Equal(UserPreferences.Defaults, new FileUserPreferencesStore(path).Current);
    }

    private sealed class TemporaryFolder : IDisposable
    {
        private readonly string _path = Directory.CreateTempSubdirectory("motif-delayed-preferences-").FullName;
        private readonly List<FileUserPreferencesStore> _stores = [];

        public string File(string name) => Path.Combine(_path, name);

        public FileUserPreferencesStore Store(string name, TimeSpan? delay = null)
        {
            var store = new FileUserPreferencesStore(File(name), delay);
            _stores.Add(store);
            return store;
        }

        public void Dispose()
        {
            foreach (var store in _stores) store.Dispose();
            Directory.Delete(_path, recursive: true);
        }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition() && DateTime.UtcNow < until) await Task.Delay(10);
        Assert.True(condition());
    }
}
