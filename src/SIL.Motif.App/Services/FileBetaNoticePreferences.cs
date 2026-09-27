using System.Text.Json;

namespace SIL.Motif.App.Services;

internal sealed class FileBetaNoticePreferences(string preferencesPath) : IBetaNoticePreferences
{
    private readonly string _preferencesPath = preferencesPath;

    public bool HasSeenBetaNotice
    {
        get
        {
            try
            {
                if (!File.Exists(_preferencesPath)) return false;
                return JsonSerializer.Deserialize<NoticePreference>(File.ReadAllText(_preferencesPath))
                    ?.HasSeenBetaNotice == true;
            }
            catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    public void MarkBetaNoticeSeen()
    {
        try
        {
            var directory = Path.GetDirectoryName(_preferencesPath);
            if (directory is not null) Directory.CreateDirectory(directory);
            File.WriteAllText(_preferencesPath, JsonSerializer.Serialize(new NoticePreference(true)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    public static FileBetaNoticePreferences ForInstallation()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Motif", "beta-notice.json");
        return new FileBetaNoticePreferences(path);
    }

    private sealed record NoticePreference(bool HasSeenBetaNotice);
}
