using System.Text;

namespace SIL.Motif.Host.Installation;

/// <summary>
/// The desktop entry and icon a Linux AppImage registers for itself, so the dock and application menu show Motif
/// with its own icon. Desktops match a running window to this entry through <see cref="WindowClass"/>.
/// </summary>
public static class LinuxDesktopEntry
{
    /// <summary>The window class Motif's window carries on Linux, matched by the entry's <c>StartupWMClass</c>.</summary>
    public const string WindowClass = "sil-motif";

    /// <summary>The icon name and desktop entry file name, unique to Motif within the user's data folder.</summary>
    public const string Name = "sil-motif";

    /// <summary>The icon file the AppImage's payload carries beside the application.</summary>
    public const string IconFileName = "motif.png";

    /// <summary>The desktop entry for an AppImage at <paramref name="appImagePath"/>.</summary>
    public static string Build(string appImagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appImagePath);
        return "[Desktop Entry]\n" +
            "Type=Application\n" +
            "Name=Motif\n" +
            "Comment=Measure a FieldWorks grammar with the PanGloss parser\n" +
            "Exec=" + QuoteExec(appImagePath) + " %f\n" +
            "Icon=" + Name + "\n" +
            "Terminal=false\n" +
            "Categories=Education;Languages;\n" +
            "StartupWMClass=" + WindowClass + "\n";
    }

    /// <summary>Writes the entry and copies the icon into <paramref name="dataHome"/>, replacing earlier copies.</summary>
    public static void Install(string dataHome, string appImagePath, string iconSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataHome);
        ArgumentException.ThrowIfNullOrWhiteSpace(iconSource);
        var entryPath = EntryPath(dataHome);
        Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);
        File.WriteAllText(entryPath, Build(appImagePath), new UTF8Encoding(false));
        if (!File.Exists(iconSource)) return;
        var iconPath = IconPath(dataHome);
        Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
        File.Copy(iconSource, iconPath, overwrite: true);
    }

    /// <summary>Removes the entry and icon this module installed; returns whether either was there.</summary>
    public static bool Remove(string dataHome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataHome);
        var removed = false;
        foreach (var path in new[] { EntryPath(dataHome), IconPath(dataHome) })
        {
            if (!File.Exists(path)) continue;
            File.Delete(path);
            removed = true;
        }
        return removed;
    }

    /// <summary>The user's data folder: <c>XDG_DATA_HOME</c>, or <c>~/.local/share</c>.</summary>
    public static string DataHome(string home) =>
        Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } xdg
            ? Path.GetFullPath(xdg)
            : Path.Combine(home, ".local", "share");

    internal static string EntryPath(string dataHome) => Path.Combine(dataHome, "applications", Name + ".desktop");

    internal static string IconPath(string dataHome) =>
        Path.Combine(dataHome, "icons", "hicolor", "256x256", "apps", Name + ".png");

    // The Desktop Entry Specification quotes an Exec argument in double quotes, escaping ", `, $ and \.
    private static string QuoteExec(string path)
    {
        var escaped = new StringBuilder();
        foreach (var character in path)
        {
            if (character is '"' or '`' or '$' or '\\') escaped.Append('\\');
            escaped.Append(character);
        }
        return "\"" + escaped + "\"";
    }
}
