using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SIL.Motif.Tests.Mcp;

/// <summary>
/// The tool-list golden: one file per tool per profile, <c>Golden/tools/&lt;profile&gt;/&lt;tool&gt;.json</c>, plus
/// <c>_index.json</c> naming the profile's tools in server order, so two branches editing different tools never touch the
/// same file. Set MOTIF_UPDATE_GOLDEN=1 to rewrite the folder.
/// </summary>
internal static class ToolsListGolden
{
    private const string Index = "_index.json";

    public static string Directory([CallerFilePath] string source = "") =>
        Path.Combine(Path.GetDirectoryName(source)!, "Golden", "tools");

    public static string ProfileDirectory(string profile) => Path.Combine(Directory(), profile == "builtin" ? "default" : profile);

    public static bool Exists(string profile) => File.Exists(Path.Combine(ProfileDirectory(profile), Index));

    public static IEnumerable<string> Profiles() =>
        System.IO.Directory.GetDirectories(Directory()).Select(Path.GetFileName).Order(StringComparer.Ordinal)!;

    /// <summary>The golden's tools as JSON nodes, in the order the index lists them.</summary>
    public static List<JsonNode> Read(string profile)
    {
        var directory = ProfileDirectory(profile);
        var names = JsonNode.Parse(File.ReadAllText(Path.Combine(directory, Index)))!.AsArray();
        return [.. names.Select(name => JsonNode.Parse(File.ReadAllText(Path.Combine(directory, name!.GetValue<string>() + ".json")))!)];
    }

    /// <summary>The files a profile's golden holds, by relative path.</summary>
    public static Dictionary<string, string> Render(IReadOnlyList<JsonNode> tools)
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Index] = new JsonArray(tools.Select(tool => (JsonNode)tool["name"]!.GetValue<string>()).ToArray()).ToJsonString(options) + "\n",
        };
        foreach (var tool in tools) files[tool["name"]!.GetValue<string>() + ".json"] = tool.ToJsonString(options) + "\n";
        return files;
    }

    /// <summary>Rewrites the profile's folder when MOTIF_UPDATE_GOLDEN=1, then returns the files now on disk.</summary>
    public static Dictionary<string, string> Sync(string profile, Dictionary<string, string> rendered)
    {
        var directory = ProfileDirectory(profile);
        if (Environment.GetEnvironmentVariable("MOTIF_UPDATE_GOLDEN") == "1")
        {
            if (System.IO.Directory.Exists(directory)) System.IO.Directory.Delete(directory, recursive: true);
            System.IO.Directory.CreateDirectory(directory);
            foreach (var (name, text) in rendered) File.WriteAllText(Path.Combine(directory, name), text);
        }
        return System.IO.Directory.Exists(directory)
            ? System.IO.Directory.GetFiles(directory).ToDictionary(file => Path.GetFileName(file)!, file => File.ReadAllText(file).ReplaceLineEndings("\n"), StringComparer.Ordinal)
            : [];
    }
}
