using System.Text.Json;

namespace SIL.Motif.App.Services;

internal sealed class FileTechDemoNoticePreferences(string preferencesPath) : ITechDemoNoticePreferences
{
    private readonly string _preferencesPath = preferencesPath;

    public bool HasSeenTechDemoNotice
    {
        get
        {
            try
            {
                if (!File.Exists(_preferencesPath)) return false;
                return JsonSerializer.Deserialize<NoticePreference>(File.ReadAllText(_preferencesPath))
                    ?.HasSeenTechDemoNotice == true;
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public void MarkTechDemoNoticeSeen()
    {
        try
        {
            var directory = Path.GetDirectoryName(_preferencesPath);
            if (directory is not null) Directory.CreateDirectory(directory);
            File.WriteAllText(_preferencesPath, JsonSerializer.Serialize(new NoticePreference(true)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A failed write only means the reminder may appear again next time.
        }
    }

    public static FileTechDemoNoticePreferences ForInstallation()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Motif", "techdemo-notice.json");
        return new FileTechDemoNoticePreferences(path);
    }

    private sealed record NoticePreference(bool HasSeenTechDemoNotice);
}
