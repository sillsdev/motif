using System.Text;
using System.Xml;
using SIL.LCModel.Core.WritingSystems;
using SIL.Motif.RecordStore;
using Xunit;

namespace SIL.Motif.Tests.RecordStore;

internal sealed class SourceAssets
{
    private readonly Dictionary<string, byte[]> files;
    private readonly Dictionary<string, byte[]> definitions;
    private readonly string[] liveTags;

    public SourceAssets(string directory, IEnumerable<CoreWritingSystemDefinition> writingSystems)
    {
        var live = writingSystems.ToArray();
        liveTags = live.Select(definition => definition.Id).Order(StringComparer.Ordinal).ToArray();
        definitions = ReadDefinitions(directory);
        foreach (var definition in live)
        {
            if (definitions.ContainsKey(definition.Id)) continue;
            using var buffer = new MemoryStream();
            using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
                definition.WriteLdml(writer);
            definitions.Add(definition.Id, buffer.ToArray());
        }

        var writingSystemDirectory = Path.Combine(directory, "WritingSystemStore");
        files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .Where(path => Path.GetExtension(path) is not (".fwdata" or ".bak" or ".lock" or ".fwss"))
            .Where(path => Path.GetDirectoryName(path) != writingSystemDirectory || Path.GetExtension(path) != ".ldml")
            .Where(path => !Path.GetFileName(path).Contains(".motif.", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(path => Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllBytes, StringComparer.Ordinal);
    }

    public int WritingSystemCount => definitions.Count;

    public void AssertImported(IReadOnlyList<GenericRecord> records, string database)
    {
        var systems = records.Where(r => r.Kind == "writing-system").ToDictionary(r => r.Values["tag"].GetString()!,
            r => Convert.FromBase64String(r.Values["ldml"].GetString()!), StringComparer.Ordinal);
        AssertDefinitions(systems, "imported");
        var content = new ResourceContentStore(database);
        var assets = records.Where(r => r.Kind == "resource").ToDictionary(r => r.Values["path"].GetString()!,
            r => content.Read(r.Values["hash"].GetString()!), StringComparer.Ordinal);
        foreach (var (relative, bytes) in files)
        {
            Assert.True(assets.TryGetValue(relative, out var imported), "Missing imported project resource " + relative);
            Assert.Equal(bytes, imported);
        }
    }

    public void AssertExported(string directory)
    {
        AssertDefinitions(ReadDefinitions(directory), "exported");
        foreach (var (relative, bytes) in files)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(directory, relative)));
    }

    public void AssertLiveWritingSystems(IEnumerable<CoreWritingSystemDefinition> definitions) =>
        Assert.Equal(liveTags, definitions.Select(definition => definition.Id).Order(StringComparer.Ordinal));

    private static Dictionary<string, byte[]> ReadDefinitions(string projectDirectory)
    {
        var directory = Path.Combine(projectDirectory, "WritingSystemStore");
        var files = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.ldml") : [];
        return files.ToDictionary(path => Path.GetFileNameWithoutExtension(path), File.ReadAllBytes, StringComparer.Ordinal);
    }

    private void AssertDefinitions(Dictionary<string, byte[]> actual, string result)
    {
        foreach (var (tag, expected) in definitions)
        {
            Assert.True(actual.TryGetValue(tag, out var bytes), $"Missing {result} writing system {tag}.");
            Assert.True(expected.AsSpan().SequenceEqual(bytes), $"Changed {result} writing system {tag}.");
        }
        Assert.Equal(definitions.Count, actual.Count);
    }
}
