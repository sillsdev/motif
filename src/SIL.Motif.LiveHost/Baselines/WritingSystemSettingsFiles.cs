namespace SIL.Motif.LiveHost.Baselines;

/// <summary>Companion project and current-user files that supply writing-system names and font selection.</summary>
public static class WritingSystemSettingsFiles
{
    public static IEnumerable<string> Find(string projectFolder) =>
        new[] { "LexiconSettings.plsx", Environment.UserName + ".ulsx" }
            .Select(name => Path.Combine(projectFolder, "SharedSettings", name)).Where(File.Exists);

    public static bool IsAllowedName(string name) =>
        name == "LexiconSettings.plsx" || (name.Length > 5 && name.EndsWith(".ulsx", StringComparison.Ordinal));
}
