using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using SIL.LCModel;

namespace SIL.Motif.RecordStore.Lcm;

public static class LcmRecordBridge
{
    public static readonly Guid ManifestId = RecordStore.AuxiliaryId("manifest", "project");

    public static async Task ImportAsync(LcmCache cache, string sourcePath, string storePath)
    {
        if (Path.GetFullPath(sourcePath) != Path.GetFullPath(cache.ProjectId.Path))
            throw new ArgumentException("Resource source must match the loaded project.", nameof(sourcePath));
        var graph = new LcmGraphReader(cache);
        var fieldTypes = graph.FieldTypes;
        var imports = graph.Read().Select(record => new RecordImport(record, fieldTypes)).ToList();
        var manifest = Auxiliary("manifest", "project");
        manifest.Values["schema"] = graph.Schema();
        manifest.Values["nativeProject"] = JsonSerializer.SerializeToElement(cache.ProjectId.Name);
        manifest.Values["nativeImport"] = JsonSerializer.SerializeToElement(new
        {
            traversalVersion = LcmGraphReader.TraversalVersion, objects = imports.Count,
            classes = imports.GroupBy(r => r.Record.ClassId).OrderBy(g => g.Key).Select(g => new { classId = g.Key, count = g.Count() }),
            vectorMembers = imports.Sum(r => r.Record.Provenance!.Vectors.Values.Sum(v => v.Length))
        });
        manifest.Values["formatVersion"] = JsonSerializer.SerializeToElement(1);
        imports.Add(new RecordImport(manifest, new Dictionary<int, int>()));
        var directory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var systems = cache.ServiceLocator.WritingSystemManager.WritingSystems.ToDictionary(w => w.Id, StringComparer.Ordinal);
        foreach (var system in systems.Values)
            if (system.IsChanged && File.Exists(Path.Combine(directory, "WritingSystemStore", system.Id + ".ldml")))
                throw new InvalidOperationException("Save writing-system changes before import: " + system.Id);
        var wsDirectory = Path.Combine(directory, "WritingSystemStore");
        foreach (var path in Directory.Exists(wsDirectory) ? Directory.EnumerateFiles(wsDirectory, "*.ldml") : [])
        {
            var tag = Path.GetFileNameWithoutExtension(path);
            cache.ServiceLocator.WritingSystemManager.GetOrSet(tag, out var definition);
            systems.TryAdd(definition.Id, definition);
        }
        foreach (var (tag, system) in systems.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var path = Path.Combine(wsDirectory, tag + ".ldml");
            byte[] ldml;
            if (File.Exists(path)) ldml = File.ReadAllBytes(path);
            else
            {
                using var buffer = new MemoryStream();
                using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = false }))
                    system.WriteLdml(writer);
                ldml = buffer.ToArray();
            }
            var record = Auxiliary("writing-system", tag);
            record.Values["tag"] = JsonSerializer.SerializeToElement(tag);
            record.Values["path"] = JsonSerializer.SerializeToElement("WritingSystemStore/" + tag + ".ldml");
            record.Values["ldml"] = JsonSerializer.SerializeToElement(Convert.ToBase64String(ldml));
            record.Values["collationBinding"] = JsonSerializer.SerializeToElement(CollationBinding(ldml));
            imports.Add(new RecordImport(record, new Dictionary<int, int>()));
        }
        var content = new ResourceContentStore(storePath);
        foreach (var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Where(path => IsResource(path, directory, wsDirectory) && !IsStoreArtifact(path, storePath)).ToArray())
        {
            var relative = Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/');
            var record = Auxiliary("resource", relative);
            record.Values["path"] = JsonSerializer.SerializeToElement(relative);
            record.Values["hash"] = JsonSerializer.SerializeToElement(content.Put(File.ReadAllBytes(path)));
            imports.Add(new RecordImport(record, new Dictionary<int, int>()));
        }
        await using var store = new RecordStore(storePath);
        await store.ImportAsync(imports, Guid.NewGuid(), graph.Coverage());
    }

    private static bool IsStoreArtifact(string path, string storePath)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var full = Path.GetFullPath(path);
        var database = Path.GetFullPath(storePath);
        return full.Equals(database, comparison) || full.Equals(database + "-wal", comparison) ||
            full.Equals(database + "-shm", comparison) || full.Equals(database + "-journal", comparison) ||
            full.StartsWith(database + ".resources" + Path.DirectorySeparatorChar, comparison);
    }

    private static bool IsResource(string path, string projectDirectory, string writingSystemDirectory)
    {
        var extension = Path.GetExtension(path);
        return extension is not (".fwdata" or ".bak" or ".lock" or ".fwss") &&
            !(extension == ".ldml" && Path.GetDirectoryName(path) == writingSystemDirectory) &&
            !Path.GetFileName(path).Contains(".motif.", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetRelativePath(projectDirectory, path).Split(Path.DirectorySeparatorChar).Any(p => p is "Temp" or "BackupSettings");
    }

    public static string CollationBinding(byte[] ldml)
    {
        using var input = new MemoryStream(ldml);
        var xml = XDocument.Load(input, LoadOptions.PreserveWhitespace);
        var collations = string.Join("\n", xml.Descendants().Where(e => e.Name.LocalName == "collations").Select(e => e.ToString(SaveOptions.DisableFormatting)));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(collations)));
    }

    public static string ResourcePath(string destination, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') || Path.IsPathRooted(relative) ||
            relative.Split('/').Any(segment => segment is "" or "." or ".."))
            throw new InvalidDataException("Unsafe project resource path: " + relative);
        var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(destination, relative));
        if (!result.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidDataException("Project resource escapes its destination.");
        return result;
    }

    public static async Task<string> ExportAsync(string storePath, string destination)
    {
        if (Directory.Exists(destination) || File.Exists(destination)) throw new IOException("Export requires a fresh directory.");
        IReadOnlyList<GenericRecord> records;
        await using (var store = new RecordStore(storePath)) records = await store.ReadAsync();
        if (records.Any(r => r.Kind is not ("lcm" or "manifest" or "writing-system" or "resource" or "native-coverage")))
            throw new NotSupportedException("Unsupported record kind in project export.");
        var manifest = records.Single(r => r.Id == ManifestId && r.Kind == "manifest");
        if (manifest.Values["formatVersion"].GetInt32() != 1) throw new NotSupportedException("Unsupported record-store format.");
        var resources = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        var content = new ResourceContentStore(storePath);
        foreach (var record in records.Where(r => r.Kind is "resource" or "writing-system"))
        {
            var relative = record.Values["path"].GetString()!;
            ResourcePath(destination, relative);
            var bytes = record.Kind == "writing-system"
                ? Convert.FromBase64String(record.Values["ldml"].GetString()!) : content.Read(record.Values["hash"].GetString()!);
            if (record.Kind == "writing-system" &&
                (record.Id != RecordStore.AuxiliaryId(record.Kind, record.Values["tag"].GetString()!) ||
                 record.Values["collationBinding"].GetString() != CollationBinding(bytes)))
                throw new InvalidDataException("Writing-system identity or collation binding mismatch.");
            if (!resources.TryAdd(relative, bytes)) throw new InvalidDataException("Duplicate project resource path.");
        }
        Directory.CreateDirectory(destination);
        WriteResources(destination, resources);
        var path = Path.Combine(destination, Path.GetFileName(destination) + ".fwdata");
        LcmGraphWriter.Restore(path, manifest.Values["schema"], records.Where(r => r.Kind == "lcm").ToArray());
        foreach (var file in Directory.EnumerateFiles(destination, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(destination, file).Replace(Path.DirectorySeparatorChar, '/');
            if (file != path && !resources.ContainsKey(relative)) File.Delete(file);
        }
        WriteResources(destination, resources);
        return path;
    }

    private static GenericRecord Auxiliary(string kind, string identity) => new()
    {
        Id = RecordStore.AuxiliaryId(kind, identity), Kind = kind
    };

    private static void WriteResources(string destination, Dictionary<string, byte[]> resources)
    {
        foreach (var (relative, bytes) in resources)
        {
            var target = ResourcePath(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
    }
}
