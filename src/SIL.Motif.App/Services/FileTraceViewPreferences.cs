using System.Text.Json;

namespace SIL.Motif.App.Services;

public interface ITraceViewPreferences
{
    bool IsExpert { get; set; }
}

/// <summary>Stores one person's trace presentation choice, independently of any language project.</summary>
public sealed class FileTraceViewPreferences(string path) : ITraceViewPreferences
{
    public bool IsExpert
    {
        get
        {
            try
            {
                return File.Exists(path) && JsonSerializer.Deserialize<Preference>(File.ReadAllText(path))?.Mode == "Expert";
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                return false;
            }
        }
        set
        {
            try
            {
                var directory = Path.GetDirectoryName(path);
                if (directory is not null) Directory.CreateDirectory(directory);
                File.WriteAllText(path, JsonSerializer.Serialize(new Preference(value ? "Expert" : "Plain")));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The in-memory choice remains usable when the preference cannot be saved.
            }
        }
    }

    public static FileTraceViewPreferences ForInstallation() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Motif", "trace-view.json"));

    private sealed record Preference(string Mode);
}
