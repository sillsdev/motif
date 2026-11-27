namespace SIL.Motif.Tests.TestFixtures;

/// <summary>Finds the store file inside a project store's newest pre-upgrade backup folder.</summary>
public static class PreUpgradeBackups
{
    /// <summary>The newest <c>pre-upgrade</c> backup's copy of the store, for the store at <paramref name="storePath"/>.</summary>
    public static string StoreOf(string storePath)
    {
        var full = Path.GetFullPath(storePath);
        var name = Path.GetFileName(full);
        var stem = name.EndsWith(".motif.db", StringComparison.Ordinal) ? name[..^".motif.db".Length] : Path.GetFileNameWithoutExtension(name);
        var root = Path.Combine(Path.GetDirectoryName(full)!, stem + ".motif-backups");
        var newest = Directory.GetDirectories(root, "pre-upgrade-*").Order(StringComparer.Ordinal).Last();
        return Path.Combine(newest, "store.sqlite");
    }
}
