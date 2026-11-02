using SIL.Motif.App.Services;
using Xunit;

namespace SIL.Motif.Tests.App;

/// <summary>Choices whose write failed are kept until a write succeeds, through the delayed save the installed app uses and the immediate one.</summary>
public sealed class PreferenceStoreRecoveryTests
{
    private static readonly TimeSpan NeverFires = TimeSpan.FromMinutes(5);

    // A directory where the file belongs makes every write fail until it is removed.
    private sealed class Blocked : IDisposable
    {
        private readonly string _folder = Directory.CreateTempSubdirectory("motif-preference-recovery-").FullName;
        private readonly List<FileUserPreferencesStore> _stores = [];

        public Blocked() => Directory.CreateDirectory(Path);

        public string Path => System.IO.Path.Combine(_folder, "user-preferences.json");

        public FileUserPreferencesStore Store(TimeSpan? delay)
        {
            var store = new FileUserPreferencesStore(Path, delay);
            _stores.Add(store);
            return store;
        }

        public void Unblock() => Directory.Delete(Path);

        public void Dispose()
        {
            foreach (var store in _stores) store.Dispose();
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void AFailedFlushKeepsTheEditAndALaterGoodFlushWritesItBeforeTheNewOne()
    {
        using var blocked = new Blocked();
        var store = blocked.Store(NeverFires);
        store.Update(p => p with { Theme = UserTheme.Dark });
        store.Update(p => p with { ZoomPercent = 125 });

        store.Flush();

        Assert.Equal(PreferenceState.Unavailable, store.State);
        Assert.Equal(UserTheme.Dark, store.Current.Theme);

        blocked.Unblock();
        store.Update(p => p with { ZoomPercent = 150 });
        store.Flush();

        Assert.Equal(PreferenceState.Ready, store.State);
        var saved = new FileUserPreferencesStore(blocked.Path).Current;
        Assert.Equal(UserTheme.Dark, saved.Theme);
        Assert.Equal(150, saved.ZoomPercent);
        Assert.Equal(saved, store.Current);
    }

    [Fact]
    public void EditsFromTwoFailedFlushesReplayInTheOrderTheyWereMade()
    {
        using var blocked = new Blocked();
        var store = blocked.Store(NeverFires);
        store.Update(p => p with { ZoomPercent = 125 });
        store.Flush();
        store.Update(p => p with { ZoomPercent = 150 });
        store.Flush();
        Assert.Equal(PreferenceState.Unavailable, store.State);

        blocked.Unblock();
        store.Update(p => p with { Theme = UserTheme.Light });
        store.Flush();

        var saved = new FileUserPreferencesStore(blocked.Path).Current;
        Assert.Equal(150, saved.ZoomPercent);
        Assert.Equal(UserTheme.Light, saved.Theme);
    }

    [Fact]
    public void ResetAfterAFailedFlushDropsTheKeptEdits()
    {
        using var blocked = new Blocked();
        var store = blocked.Store(NeverFires);
        store.Update(p => p with { Theme = UserTheme.Dark });
        store.Flush();

        blocked.Unblock();
        store.Reset();
        store.Update(p => p with { ZoomPercent = 125 });
        store.Flush();

        var saved = new FileUserPreferencesStore(blocked.Path).Current;
        Assert.Equal(UserTheme.System, saved.Theme);
        Assert.Equal(125, saved.ZoomPercent);
    }

    // Fails once another process has set Trace to Expert, as a replay against a changed file can.
    private static UserPreferences DarkUnlessExpert(UserPreferences p) => p.Trace == TracePresentation.Expert
        ? throw new InvalidDataException("The other process changed the file under this edit.")
        : p with { Theme = UserTheme.Dark };

    [Fact]
    public void AKeptEditThatNowFailsValidationIsDroppedAndDoesNotBlockLaterDelayedSaves()
    {
        using var blocked = new Blocked();
        var store = blocked.Store(NeverFires);
        store.Update(DarkUnlessExpert);
        store.Flush();
        Assert.Equal(PreferenceState.Unavailable, store.State);

        blocked.Unblock();
        new FileUserPreferencesStore(blocked.Path).Update(p => p with { Trace = TracePresentation.Expert });
        store.Update(p => p with { ZoomPercent = 125 });
        Assert.Null(Record.Exception(store.Flush));

        Assert.Equal(PreferenceState.Ready, store.State);
        var saved = new FileUserPreferencesStore(blocked.Path).Current;
        Assert.Equal(125, saved.ZoomPercent);
        Assert.Equal(UserTheme.System, saved.Theme);

        store.Update(p => p with { ZoomPercent = 150 });
        store.Flush();
        Assert.Equal(150, new FileUserPreferencesStore(blocked.Path).Current.ZoomPercent);
    }

    [Fact]
    public void AKeptEditThatNowFailsValidationIsDroppedAndDoesNotBlockLaterImmediateSaves()
    {
        using var blocked = new Blocked();
        var store = blocked.Store(null);
        Assert.Equal(PreferenceWriteState.SessionOnly, store.Update(DarkUnlessExpert).State);

        blocked.Unblock();
        new FileUserPreferencesStore(blocked.Path).Update(p => p with { Trace = TracePresentation.Expert });

        var result = Record.Exception(() => store.Update(p => p with { ZoomPercent = 125 }));
        Assert.Null(result);
        Assert.Equal(PreferenceState.Ready, store.State);
        Assert.Equal(125, new FileUserPreferencesStore(blocked.Path).Current.ZoomPercent);

        Assert.Equal(PreferenceWriteState.Saved, store.Update(p => p with { ZoomPercent = 150 }).State);
        Assert.Equal(150, new FileUserPreferencesStore(blocked.Path).Current.ZoomPercent);
    }
}
