namespace SIL.Motif.Host;

/// <summary>
/// The per-user data folder every Motif store lives under. Off Windows it follows an absolute
/// <c>XDG_DATA_HOME</c>, from the environment a caller pushed with <see cref="AmbientEnvironment"/> or else the
/// process's own, and otherwise <c>~/.local/share</c>; a relative value is ignored, as the XDG specification says.
/// </summary>
/// <remarks>
/// The platform lookup returns an empty folder when <c>XDG_DATA_HOME</c> names a folder that does not exist yet,
/// which made a store path relative to the working directory, and it never sees a pushed environment. A process
/// whose <c>XDG_DATA_HOME</c> names an existing folder, or sets none, gets the folder it always had.
/// </remarks>
public static class UserDataHome
{
    /// <summary>The folder, or an empty string when the platform has none.</summary>
    public static string Path
    {
        get
        {
            if (OperatingSystem.IsWindows())
                return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var xdg = AmbientEnvironment.Get("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdg) && System.IO.Path.IsPathRooted(xdg))
                return System.IO.Path.GetFullPath(xdg);
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return string.IsNullOrEmpty(home) ? string.Empty : System.IO.Path.Combine(home, ".local", "share");
        }
    }
}
