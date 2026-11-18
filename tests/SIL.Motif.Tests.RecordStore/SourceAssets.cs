using SIL.Motif.RecordStore;
using Xunit;

namespace SIL.Motif.Tests.RecordStore;

internal sealed class SourceAssets
{
    private readonly Dictionary<string, byte[]> files;
    private readonly string[] tags;

    public SourceAssets(string directory, IEnumerable<string> writingSystems)
    {
        tags = writingSystems.ToArray();
        files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is not (".fwdata" or ".bak" or ".lock" or ".fwss"))
            .Where(path => !Path.GetFileName(path).Contains(".motif.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(path => Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllBytes, StringComparer.Ordinal);
    }

    public void AssertImported(IReadOnlyList<GenericRecord> records, string database)
    {
        var systems = records.Where(r => r.Kind == "writing-system").ToDictionary(r => r.Values["tag"].GetString()!);
        foreach (var tag in tags) Assert.True(systems.ContainsKey(tag), "Missing writing system " + tag);
        var assets = records.Where(r => r.Kind is "writing-system" or "resource")
            .ToDictionary(r => r.Values["path"].GetString()!, r => r.Kind == "writing-system" ? Convert.FromBase64String(r.Values["ldml"].GetString()!)
                : new ResourceContentStore(database).Read(r.Values["hash"].GetString()!));
        foreach (var (relative, bytes) in files)
        {
            Assert.True(assets.TryGetValue(relative, out var imported), "Missing imported project resource " + relative);
            Assert.Equal(bytes, imported);
        }
    }

    public void AssertExported(string directory)
    {
        foreach (var (relative, bytes) in files)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(directory, relative)));
    }
}
