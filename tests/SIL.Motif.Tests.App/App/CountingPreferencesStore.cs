using SIL.Motif.App.Services;

namespace SIL.Motif.Tests.App;

/// <summary>Wraps a preferences store and counts the writes made through it.</summary>
internal sealed class CountingPreferencesStore(IUserPreferencesStore inner) : IUserPreferencesStore
{
    public int Updates { get; private set; }
    public UserPreferences Current => inner.Current;
    public PreferenceState State => inner.State;
    public PreferenceProblem? Problem => inner.Problem;
    public event EventHandler? Changed { add => inner.Changed += value; remove => inner.Changed -= value; }
    public PreferenceWriteResult Update(Func<UserPreferences, UserPreferences> edit) { Updates++; return inner.Update(edit); }
    public PreferenceWriteResult Reset() => inner.Reset();
    public void Flush() => inner.Flush();
}
