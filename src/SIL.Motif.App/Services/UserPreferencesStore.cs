using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.App.Services;

/// <summary>The theme choice stored for this Motif installation.</summary>
public enum UserTheme
{
    System,
    Light,
    Dark,
}

/// <summary>The Settings section selected when the person opens the page.</summary>
public enum SettingsGroup
{
    Display,
    Shortcuts,
    Parsing,
    ProposalChecks,
    WritingSystems,
    Help,
    About,
    Texts,
}

/// <summary>The trace detail choice stored for this Motif installation.</summary>
public enum TracePresentation
{
    Plain,
    Expert,
}

/// <summary>Describes the current per-user preferences file state.</summary>
public enum PreferenceState
{
    Missing,
    Ready,
    Refused,
    Unavailable,
}

/// <summary>Describes whether a preference update reached durable storage.</summary>
public enum PreferenceWriteState
{
    Saved,
    SessionOnly,
    Refused,
}

/// <summary>The saved outer window size in DIPs and optional native-pixel position.</summary>
public sealed record WindowPlacement(double Width, double Height, int? X, int? Y);

/// <summary>The sort and filter a person left on one view of one project; null parts mean the view's default.</summary>
public sealed record ViewState(string? SortColumn, bool SortDescending, string? Filter);

/// <summary>The complete user preference snapshot shared by the Motif window.</summary>
public sealed record UserPreferences(
    int FormatVersion,
    int ZoomPercent,
    UserTheme Theme,
    SettingsGroup LastSettingsGroup,
    WindowPlacement? Window,
    TracePresentation Trace,
    bool HasSeenTechDemoNotice)
{
    /// <summary>The defaults used when no usable preference file exists.</summary>
    public static UserPreferences Defaults { get; } =
        new(FileUserPreferencesStore.LatestFormatVersion, ZoomPolicy.DefaultPercent, UserTheme.System,
            SettingsGroup.Display, null, TracePresentation.Plain, false);

    /// <summary>Whether each "More options" section, keyed by window or section name, is expanded (format 2).</summary>
    public IReadOnlyDictionary<string, bool> ExpandedSections { get; init; } =
        ImmutableSortedDictionary<string, bool>.Empty.WithComparers(StringComparer.Ordinal);

    /// <summary>Per-project view state: project key, then view name (format 2). Lives only in user preferences.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, ViewState>> ProjectViews { get; init; } =
        ImmutableSortedDictionary<string, IReadOnlyDictionary<string, ViewState>>.Empty
            .WithComparers(StringComparer.Ordinal);

    /// <summary>Value equality, including the two maps, so a re-read file equals the snapshot that was saved.</summary>
    public bool Equals(UserPreferences? other) =>
        other is not null && FormatVersion == other.FormatVersion && ZoomPercent == other.ZoomPercent &&
        Theme == other.Theme && LastSettingsGroup == other.LastSettingsGroup && Equals(Window, other.Window) &&
        Trace == other.Trace && HasSeenTechDemoNotice == other.HasSeenTechDemoNotice &&
        ExpandedSections.Count == other.ExpandedSections.Count &&
        ExpandedSections.All(pair => other.ExpandedSections.TryGetValue(pair.Key, out var v) && v == pair.Value) &&
        ProjectViews.Count == other.ProjectViews.Count &&
        ProjectViews.All(project => other.ProjectViews.TryGetValue(project.Key, out var views) &&
            views.Count == project.Value.Count &&
            project.Value.All(view => views.TryGetValue(view.Key, out var state) && Equals(state, view.Value)));

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(FormatVersion, ZoomPercent, Theme, LastSettingsGroup, Window, Trace, HasSeenTechDemoNotice,
            ExpandedSections.Count + ProjectViews.Count);
}

/// <summary>A stable code and user-facing explanation for a preference-file problem.</summary>
public sealed record PreferenceProblem(string Code, string Message);

/// <summary>The result of saving or resetting preferences.</summary>
public sealed record PreferenceWriteResult(PreferenceWriteState State, PreferenceProblem? Problem);

/// <summary>Reads and updates one in-session snapshot of the person's App preferences.</summary>
public interface IUserPreferencesStore
{
    /// <summary>The accepted preference snapshot currently used by this App process.</summary>
    UserPreferences Current { get; }

    /// <summary>The durable file state seen by this store.</summary>
    PreferenceState State { get; }

    /// <summary>The current read or write problem, when preferences cannot be fully used.</summary>
    PreferenceProblem? Problem { get; }

    /// <summary>Raised after the current snapshot or file state changes.</summary>
    event EventHandler? Changed;

    /// <summary>Applies a field update against the latest snapshot and saves it when possible.</summary>
    PreferenceWriteResult Update(Func<UserPreferences, UserPreferences> edit);

    /// <summary>Deletes only this store's file and recreates it with the current defaults.</summary>
    PreferenceWriteResult Reset();

    /// <summary>Writes any change still waiting for its delayed save, and returns once it is on disk or has failed.</summary>
    void Flush();
}

/// <summary>The validated file-backed store shared by all App presentation preferences.</summary>
public sealed class FileUserPreferencesStore : IUserPreferencesStore, IDisposable
{
    /// <summary>The format written on every save. Format 1 files are read and upgraded in memory.</summary>
    public const int LatestFormatVersion = 2;

    private const int CurrentFormatVersion = LatestFormatVersion;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly string[] FormatOneProperties =
    [
        nameof(UserPreferences.FormatVersion),
        nameof(UserPreferences.ZoomPercent),
        nameof(UserPreferences.Theme),
        nameof(UserPreferences.LastSettingsGroup),
        nameof(UserPreferences.Window),
        nameof(UserPreferences.Trace),
        nameof(UserPreferences.HasSeenTechDemoNotice),
    ];
    private static readonly string[] RequiredProperties =
    [
        .. FormatOneProperties,
        nameof(UserPreferences.ExpandedSections),
        nameof(UserPreferences.ProjectViews),
    ];
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _path;
    private readonly object _gate = new();
    private readonly object _writeGate = new();
    private readonly TimeSpan? _saveDelay;
    private readonly Action? _beforeWriteProcessLock;
    private readonly List<Func<UserPreferences, UserPreferences>> _pending = [];
    private readonly Timer? _timer;
    private UserPreferences _current = UserPreferences.Defaults;
    private PreferenceState _state;
    private PreferenceProblem? _problem;

    /// <summary>
    /// Creates a store for the supplied preferences file path. With a <paramref name="saveDelay"/>, an update changes
    /// the in-session snapshot at once and the file write is coalesced: it runs on a thread-pool thread that long
    /// after the last update, or sooner through <see cref="Flush"/>. Without one, every update writes before it returns.
    /// </summary>
    public FileUserPreferencesStore(string path, TimeSpan? saveDelay = null)
        : this(path, saveDelay, beforeWriteProcessLock: null)
    {
    }

    /// <summary>
    /// Creates a store with an observer called after each pending batch drains, before acquiring the process mutex.
    /// </summary>
    internal FileUserPreferencesStore(string path, TimeSpan? saveDelay, Action? beforeWriteProcessLock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        _saveDelay = saveDelay;
        _beforeWriteProcessLock = beforeWriteProcessLock;
        if (saveDelay is not null)
        {
            _timer = new Timer(_ => FlushFromTimer(), null, Timeout.Infinite, Timeout.Infinite);
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
        }

        var read = ReadFile();
        _current = read.Preferences;
        _state = read.State;
        _problem = read.Problem;
    }

    /// <summary>How long after the last change the installed App writes preferences.</summary>
    public static TimeSpan InstallationSaveDelay { get; } = TimeSpan.FromMilliseconds(400);

    /// <summary>The installation's per-user preferences file.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Motif", "user-preferences.json");

    /// <summary>The snapshot accepted by this process.</summary>
    public UserPreferences Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>The current state of the preferences file.</summary>
    public PreferenceState State
    {
        get { lock (_gate) return _state; }
    }

    /// <summary>The current file problem, if one has been observed.</summary>
    public PreferenceProblem? Problem
    {
        get { lock (_gate) return _problem; }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public PreferenceWriteResult Update(Func<UserPreferences, UserPreferences> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        PreferenceWriteResult result;
        lock (_gate)
        {
            var prior = _current;
            if (_state == PreferenceState.Refused)
            {
                _current = Validate(edit(_current));
                result = new PreferenceWriteResult(PreferenceWriteState.Refused, _problem);
            }
            else if (_saveDelay is { } delay)
            {
                _current = Validate(edit(_current));
                _pending.Add(edit);
                _timer!.Change(delay, Timeout.InfiniteTimeSpan);
                result = _state == PreferenceState.Unavailable
                    ? new PreferenceWriteResult(PreferenceWriteState.SessionOnly, _problem)
                    : new PreferenceWriteResult(PreferenceWriteState.Saved, null);
            }
            else
            {
                result = UpdateUnderLock(edit, prior);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <inheritdoc />
    public void Flush()
    {
        // One writer at a time: a flush that arrives while a write is in flight waits, then drains what is left.
        lock (_writeGate)
        {
            List<Func<UserPreferences, UserPreferences>> edits;
            PreferenceState before;
            lock (_gate)
            {
                if (_pending.Count == 0) return;
                edits = [.. _pending];
                _pending.Clear();
                before = _state;
            }

            var changed = WriteEdits(edits, before);
            if (changed) Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool WriteEdits(List<Func<UserPreferences, UserPreferences>> edits, PreferenceState before)
    {
        UserPreferences Apply(UserPreferences start) =>
            edits.Aggregate(start, (value, edit) => Validate(edit(value)));
        try
        {
            _beforeWriteProcessLock?.Invoke();
            return WithProcessLock(() =>
            {
                var latest = ReadFile();
                lock (_gate)
                {
                    if (latest.State == PreferenceState.Refused)
                    {
                        _state = PreferenceState.Refused;
                        _problem = latest.Problem;
                        return true;
                    }
                }

                var next = Apply(latest.Preferences);
                WriteFile(next);
                lock (_gate)
                {
                    // Edits queued during the write are already in _current; the next flush merges them on top.
                    if (_pending.Count == 0) _current = next;
                    _state = PreferenceState.Ready;
                    _problem = null;
                }

                return before != PreferenceState.Ready;
            });
        }
        catch (Exception exception)
        {
            // Nothing may escape a background write: the snapshot stays as the session saw it, marked session-only.
            System.Diagnostics.Trace.TraceError("Motif could not save preferences to {0}: {1}", _path, exception);
            lock (_gate)
            {
                _state = PreferenceState.Unavailable;
                _problem = UnavailableProblem;
            }

            return true;
        }
    }

    /// <summary>Stops the delayed save without writing; call <see cref="Flush"/> first to keep waiting changes.</summary>
    public void Dispose()
    {
        if (_timer is null) return;
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
        _timer.Dispose();
    }

    private void FlushFromTimer()
    {
        try
        {
            Flush();
        }
        catch (Exception exception)
        {
            System.Diagnostics.Trace.TraceError("Motif preference flush failed: {0}", exception);
        }
    }

    private void OnProcessExit(object? sender, EventArgs e) => FlushFromTimer();

    /// <inheritdoc />
    public PreferenceWriteResult Reset()
    {
        PreferenceWriteResult result;
        lock (_writeGate)
        lock (_gate)
        {
            _pending.Clear();
            try
            {
                result = WithProcessLock(() =>
                {
                    if (File.Exists(_path)) File.Delete(_path);
                    WriteFile(UserPreferences.Defaults);
                    _current = UserPreferences.Defaults;
                    _state = PreferenceState.Ready;
                    _problem = null;
                    return new PreferenceWriteResult(PreferenceWriteState.Saved, null);
                });
            }
            catch (TimeoutException)
            {
                _current = UserPreferences.Defaults;
                _state = PreferenceState.Unavailable;
                _problem = UnavailableProblem;
                result = new PreferenceWriteResult(PreferenceWriteState.SessionOnly, _problem);
            }
            catch (Exception exception) when (IsStorageException(exception))
            {
                _current = UserPreferences.Defaults;
                _state = PreferenceState.Unavailable;
                _problem = UnavailableProblem;
                result = new PreferenceWriteResult(PreferenceWriteState.SessionOnly, _problem);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    private PreferenceWriteResult UpdateUnderLock(Func<UserPreferences, UserPreferences> edit,
        UserPreferences prior)
    {
        try
        {
            return WithProcessLock(() =>
            {
                var latest = ReadFile();
                if (latest.State == PreferenceState.Refused)
                {
                    _current = Validate(edit(latest.Preferences));
                    _state = PreferenceState.Refused;
                    _problem = latest.Problem;
                    return new PreferenceWriteResult(PreferenceWriteState.Refused, _problem);
                }

                var next = Validate(edit(latest.Preferences));
                WriteFile(next);
                _current = next;
                _state = PreferenceState.Ready;
                _problem = null;
                return new PreferenceWriteResult(PreferenceWriteState.Saved, null);
            });
        }
        catch (TimeoutException)
        {
            _current = Validate(edit(prior));
            _state = PreferenceState.Unavailable;
            _problem = UnavailableProblem;
            return new PreferenceWriteResult(PreferenceWriteState.SessionOnly, _problem);
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            _current = Validate(edit(prior));
            _state = PreferenceState.Unavailable;
            _problem = UnavailableProblem;
            return new PreferenceWriteResult(PreferenceWriteState.SessionOnly, _problem);
        }
    }

    private T WithProcessLock<T>(Func<T> action)
    {
        var fullPath = Path.GetFullPath(_path);
        var lockName = "Motif.UserPreferences." + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)));
        using var mutex = new Mutex(false, lockName);
        var ownsLock = false;
        try
        {
            try
            {
                ownsLock = mutex.WaitOne(LockTimeout);
            }
            catch (AbandonedMutexException)
            {
                ownsLock = true;
            }

            if (!ownsLock) throw new TimeoutException("The preferences lock was busy.");
            return action();
        }
        finally
        {
            if (ownsLock) mutex.ReleaseMutex();
        }
    }

    private (UserPreferences Preferences, PreferenceState State, PreferenceProblem? Problem) ReadFile()
    {
        try
        {
            if (!File.Exists(_path)) return (UserPreferences.Defaults, PreferenceState.Missing, null);
            var json = File.ReadAllText(_path);
            if (!HasExactShape(json, out var version)) return RefusedRead();
            var preferences = JsonSerializer.Deserialize<UserPreferences>(json, JsonOptions);
            if (preferences is null) return RefusedRead();
            // A format 1 file keeps every preference; it is upgraded here and written as format 2 on the next save.
            if (version == 1) preferences = preferences with { FormatVersion = LatestFormatVersion };
            return (Validate(preferences), PreferenceState.Ready, null);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or ArgumentException)
        {
            return RefusedRead();
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return (UserPreferences.Defaults, PreferenceState.Unavailable, UnavailableProblem);
        }
    }

    /// <summary>How many times this store has written the file; tests use it to pin coalescing.</summary>
    internal int WriteCount => Volatile.Read(ref _writeCount);

    private int _writeCount;

    private void WriteFile(UserPreferences preferences)
    {
        Interlocked.Increment(ref _writeCount);
        var directory = Path.GetDirectoryName(Path.GetFullPath(_path)) ??
            throw new IOException("The preferences file has no parent folder.");
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, ".user-preferences-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, preferences, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static bool HasExactShape(string json, out int version)
    {
        version = 0;
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
        if (!document.RootElement.TryGetProperty(nameof(UserPreferences.FormatVersion), out var versionElement) ||
            !versionElement.TryGetInt32(out version)) return false;
        // An unknown (for example newer) version has no known shape: it is refused, never rewritten.
        var required = version switch { 1 => FormatOneProperties, LatestFormatVersion => RequiredProperties, _ => null };
        if (required is null) return false;
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        return names.Length == required.Length && required.All(name => names.Contains(name, StringComparer.Ordinal));
    }

    internal static UserPreferences Validate(UserPreferences value)
    {
        if (value.FormatVersion != CurrentFormatVersion || !ZoomPolicy.Supports(value.ZoomPercent) ||
            !Enum.IsDefined(value.Theme) || !Enum.IsDefined(value.LastSettingsGroup) || !Enum.IsDefined(value.Trace))
            throw new InvalidDataException("The preferences file contains an unsupported value.");
        if (value.Window is { } window &&
            (!double.IsFinite(window.Width) || window.Width <= 0 ||
             !double.IsFinite(window.Height) || window.Height <= 0 ||
             (window.X is null) != (window.Y is null)))
            throw new InvalidDataException("The saved window placement is incomplete.");
        if (value.ExpandedSections is null || value.ProjectViews is null ||
            value.ExpandedSections.Keys.Any(string.IsNullOrWhiteSpace) ||
            value.ProjectViews.Any(project => string.IsNullOrWhiteSpace(project.Key) || project.Value is null ||
                project.Value.Any(view => string.IsNullOrWhiteSpace(view.Key) || view.Value is null)))
            throw new InvalidDataException("The saved view state is incomplete.");
        // Sorted ordinally so the file is stable and a re-read equals what was saved.
        return value with
        {
            ExpandedSections = value.ExpandedSections.ToImmutableSortedDictionary(StringComparer.Ordinal),
            ProjectViews = value.ProjectViews.ToImmutableSortedDictionary(project => project.Key,
                project => (IReadOnlyDictionary<string, ViewState>)project.Value.ToImmutableSortedDictionary(
                    StringComparer.Ordinal), StringComparer.Ordinal),
        };
    }

    private static (UserPreferences Preferences, PreferenceState State, PreferenceProblem? Problem) RefusedRead() =>
        (UserPreferences.Defaults, PreferenceState.Refused, RefusedProblem);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static bool IsStorageException(Exception exception) => exception is IOException or
        UnauthorizedAccessException or ArgumentException or NotSupportedException or
        System.Security.SecurityException or WaitHandleCannotBeOpenedException;

    private static PreferenceProblem RefusedProblem { get; } = new("preferences.refused",
        "Motif could not use the saved preferences. Reset them here or delete user-preferences.json and reopen Motif.");

    private static PreferenceProblem UnavailableProblem { get; } = new("preferences.unavailable",
        "Motif could not save preferences. Your choices are active for this session only.");
}

/// <summary>A process-local store for windows that do not use the installation's durable preferences.</summary>
public sealed class MemoryUserPreferencesStore : IUserPreferencesStore
{
    private readonly object _gate = new();
    private UserPreferences _current = UserPreferences.Defaults;

    /// <summary>The current in-memory snapshot.</summary>
    public UserPreferences Current
    {
        get { lock (_gate) return _current; }
    }

    /// <summary>This store has no file and is available for the lifetime of the process.</summary>
    public PreferenceState State => PreferenceState.Ready;

    /// <summary>This store has no file problem.</summary>
    public PreferenceProblem? Problem => null;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public PreferenceWriteResult Update(Func<UserPreferences, UserPreferences> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        lock (_gate) _current = FileUserPreferencesStore.Validate(edit(_current));
        Changed?.Invoke(this, EventArgs.Empty);
        return new PreferenceWriteResult(PreferenceWriteState.Saved, null);
    }

    /// <inheritdoc />
    public void Flush()
    {
    }

    /// <inheritdoc />
    public PreferenceWriteResult Reset()
    {
        lock (_gate) _current = UserPreferences.Defaults;
        Changed?.Invoke(this, EventArgs.Empty);
        return new PreferenceWriteResult(PreferenceWriteState.Saved, null);
    }
}
