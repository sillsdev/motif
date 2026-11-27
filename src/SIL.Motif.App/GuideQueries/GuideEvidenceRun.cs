namespace SIL.Motif.App.GuideQueries;

/// <summary>How the app finds the folder that a corpus run line's relative paths resolve against: the project's own folder.</summary>
public static class GuideEvidenceRun
{
    /// <summary>The folder of the project file, or the project path itself when it is a folder.</summary>
    public static string BaseDirectory(string projectPath)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        return Directory.Exists(projectPath) ? projectPath : Path.GetDirectoryName(Path.GetFullPath(projectPath)) ?? ".";
    }
}
