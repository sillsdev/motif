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

/// <summary>The Settings group to reopen after the person opens the popup.</summary>
public enum SettingsGroup
{
    Display,
    Shortcuts,
    Parsing,
    WritingSystems,
    Help,
    About,
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
        new(1, ZoomPolicy.DefaultPercent, UserTheme.System, SettingsGroup.Display, null, TracePresentation.Plain, false);
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
}

/// <summary>The validated file-backed store shared by all App presentation preferences.</summary>
public sealed class FileUserPreferencesStore : IUserPreferencesStore
{
    private const int CurrentFormatVersion = 1;
    private static readonly TimeSpan LockTimeout = TimeSpan.FromMilliseconds(750);
    private static readonly string[] RequiredProperties =
    [
        nameof(UserPreferences.FormatVersion),
        nameof(UserPreferences.ZoomPercent),
        nameof(UserPreferences.Theme),
        nameof(UserPreferences.LastSettingsGroup),
        nameof(UserPreferences.Window),
        nameof(UserPreferences.Trace),
        nameof(UserPreferences.HasSeenTechDemoNotice),
    ];
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();
    private readonly string _path;
    private readonly object _gate = new();
    private UserPreferences _current = UserPreferences.Defaults;
    private PreferenceState _state;
    private PreferenceProblem? _problem;

    /// <summary>Creates a store for the supplied preferences file path.</summary>
    public FileUserPreferencesStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        var read = ReadFile();
        _current = read.Preferences;
        _state = read.State;
        _problem = read.Problem;
    }

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
            else
            {
                result = UpdateUnderLock(edit, prior);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return result;
    }

    /// <inheritdoc />
    public PreferenceWriteResult Reset()
    {
        PreferenceWriteResult result;
        lock (_gate)
        {
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
            if (!HasExactShape(json)) return RefusedRead();
            var preferences = JsonSerializer.Deserialize<UserPreferences>(json, JsonOptions);
            if (preferences is null) return RefusedRead();
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

    private void WriteFile(UserPreferences preferences)
    {
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

    private static bool HasExactShape(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object) return false;
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        return names.Length == RequiredProperties.Length &&
            RequiredProperties.All(name => names.Contains(name, StringComparer.Ordinal));
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
        return value;
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
    public PreferenceWriteResult Reset()
    {
        lock (_gate) _current = UserPreferences.Defaults;
        Changed?.Invoke(this, EventArgs.Empty);
        return new PreferenceWriteResult(PreferenceWriteState.Saved, null);
    }
}
