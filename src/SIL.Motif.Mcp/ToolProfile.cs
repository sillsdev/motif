using System.Text.Json;
using System.Text.Json.Serialization;

namespace SIL.Motif.Mcp;

/// <summary>
/// One experiment's view of the tool surface: which tools exist, what they are called and say, how much
/// they return by default, and what the server tells the model on connection. Two profile files are enough to
/// compare two surfaces with no code change, which is what the A/B harness relies on.
/// </summary>
/// <param name="Name">A label recorded in the activity log, so a result can be traced to its surface.</param>
/// <param name="Description">What this profile is for, for the person choosing between profiles.</param>
/// <param name="Instructions">Server instructions sent to the model at connection, or none.</param>
/// <param name="DefaultDetail">Either <c>concise</c> or <c>detailed</c>; a tool's own selection overrides it.</param>
/// <param name="Tools">The tools to expose, in the order given; ignored when <paramref name="AllTools"/> is set.</param>
/// <param name="AllTools">Whether every tool the server can offer is exposed, under its own name and text.</param>
public sealed record ToolProfile(
    string Name,
    string? Description,
    string? Instructions,
    string DefaultDetail,
    IReadOnlyList<ToolSelection> Tools,
    bool AllTools = false)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>The profile of a server started with no <c>--profile</c>: the embedded default tool list, built-in text.</summary>
    public static ToolProfile Builtin { get; } = LoadEmbeddedDefault();

    /// <summary>Loads a profile from a path, or from the shipped <c>profiles</c> folder when given a bare name.</summary>
    public static ToolProfile Load(string pathOrName)
    {
        var path = File.Exists(pathOrName) ? pathOrName : ShippedPath(pathOrName);
        if (!File.Exists(path))
            throw new ProfileException($"Profile '{pathOrName}' was not found as a file or as a shipped profile.");
        try
        {
            return Parse(File.ReadAllText(path), path, Path.GetDirectoryName(Path.GetFullPath(path))!);
        }
        catch (JsonException exception)
        {
            throw new ProfileException($"Profile '{path}' is not valid: {exception.Message}");
        }
    }

    /// <summary>The profile's selection for a tool, or <see langword="null"/> when the profile hides it.</summary>
    internal ToolSelection? SelectionFor(string tool) =>
        AllTools ? new ToolSelection(tool, null, null, null) : Tools.FirstOrDefault(selection => selection.Tool == tool);

    private static ToolProfile Parse(string json, string source, string folder)
    {
        var raw = JsonSerializer.Deserialize<RawProfile>(json, Options)
            ?? throw new ProfileException($"Profile '{source}' is empty.");
        var detail = raw.DefaultDetail ?? "concise";
        if (detail is not ("concise" or "detailed"))
            throw new ProfileException($"Profile '{source}': defaultDetail must be 'concise' or 'detailed'.");
        if (raw.Tools is null && !raw.AllTools)
            throw new ProfileException($"Profile '{source}' lists no tools; list them under \"tools\" or set \"allTools\": true.");
        return new ToolProfile(raw.Name ?? Path.GetFileNameWithoutExtension(source), raw.Description,
            raw.InstructionsFile is { } file
                ? File.ReadAllText(Path.Combine(folder, file))
                : raw.Instructions ?? (raw.NoInstructions ? null : DefaultInstructions.Text),
            detail, raw.Tools ?? [], raw.AllTools);
    }

    // The default ships inside the assembly, so a packaged build cannot lose it.
    private static ToolProfile LoadEmbeddedDefault()
    {
        using var stream = typeof(ToolProfile).Assembly.GetManifestResourceStream(EmbeddedDefault)
            ?? throw new InvalidOperationException($"The default profile '{EmbeddedDefault}' is not embedded in the MCP server.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd(), EmbeddedDefault, AppContext.BaseDirectory) with { Name = "builtin" };
    }

    internal const string EmbeddedDefault = "motif-profiles/default.json";

    private static string ShippedPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "profiles", name.EndsWith(".json", StringComparison.Ordinal) ? name : name + ".json");

    private sealed record RawProfile(string? Name, string? Description, string? Instructions,
        string? InstructionsFile, bool NoInstructions, string? DefaultDetail, List<ToolSelection>? Tools,
        bool AllTools);
}

/// <summary>One tool a profile exposes, with the profile's changes to how the model sees it.</summary>
/// <param name="Tool">The built-in tool name this selects.</param>
/// <param name="As">The name the model sees instead, or <see langword="null"/> to keep <paramref name="Tool"/>.</param>
/// <param name="Description">Replacement description text, or <see langword="null"/> to keep the built-in one.</param>
/// <param name="Detail">This tool's default detail level, or <see langword="null"/> for the profile's.</param>
/// <param name="AlwaysLoad">Whether the client should load the tool up front rather than on demand; unset keeps its default.</param>
public sealed record ToolSelection(string Tool, string? As, string? Description, string? Detail, bool? AlwaysLoad = null);

/// <summary>A profile that cannot be used; the message says what to change.</summary>
public sealed class ProfileException(string message) : Exception(message);
