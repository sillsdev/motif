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
/// <param name="Tools">The tools to expose; <see langword="null"/> exposes every tool that is on by default.</param>
public sealed record ToolProfile(
    string Name,
    string? Description,
    string? Instructions,
    string DefaultDetail,
    IReadOnlyList<ToolSelection>? Tools)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>The profile of a server started with no <c>--profile</c>: every default tool, built-in text.</summary>
    public static ToolProfile Builtin { get; } = new("builtin", null, DefaultInstructions.Text, "concise", null);

    /// <summary>Loads a profile from a path, or from the shipped <c>profiles</c> folder when given a bare name.</summary>
    public static ToolProfile Load(string pathOrName)
    {
        var path = File.Exists(pathOrName) ? pathOrName : ShippedPath(pathOrName);
        if (!File.Exists(path))
            throw new ProfileException($"Profile '{pathOrName}' was not found as a file or as a shipped profile.");
        try
        {
            var raw = JsonSerializer.Deserialize<RawProfile>(File.ReadAllText(path), Options)
                ?? throw new ProfileException($"Profile '{path}' is empty.");
            var detail = raw.DefaultDetail ?? "concise";
            if (detail is not ("concise" or "detailed"))
                throw new ProfileException($"Profile '{path}': defaultDetail must be 'concise' or 'detailed'.");
            return new ToolProfile(raw.Name ?? Path.GetFileNameWithoutExtension(path), raw.Description,
                raw.InstructionsFile is { } file
                    ? File.ReadAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, file))
                    : raw.Instructions ?? (raw.NoInstructions ? null : DefaultInstructions.Text),
                detail, raw.Tools);
        }
        catch (JsonException exception)
        {
            throw new ProfileException($"Profile '{path}' is not valid: {exception.Message}");
        }
    }

    /// <summary>The profile's selection for a tool, or <see langword="null"/> when the profile hides it.</summary>
    internal ToolSelection? SelectionFor(string tool, bool onByDefault)
    {
        if (Tools is null) return onByDefault ? new ToolSelection(tool, null, null, null) : null;
        return Tools.FirstOrDefault(selection => selection.Tool == tool);
    }

    private static string ShippedPath(string name) =>
        Path.Combine(AppContext.BaseDirectory, "profiles", name.EndsWith(".json", StringComparison.Ordinal) ? name : name + ".json");

    private sealed record RawProfile(string? Name, string? Description, string? Instructions,
        string? InstructionsFile, bool NoInstructions, string? DefaultDetail, List<ToolSelection>? Tools);
}

/// <summary>One tool a profile exposes, with the profile's changes to how the model sees it.</summary>
/// <param name="Tool">The built-in tool name this selects.</param>
/// <param name="As">The name the model sees instead, or <see langword="null"/> to keep <paramref name="Tool"/>.</param>
/// <param name="Description">Replacement description text, or <see langword="null"/> to keep the built-in one.</param>
/// <param name="Detail">This tool's default detail level, or <see langword="null"/> for the profile's.</param>
public sealed record ToolSelection(string Tool, string? As, string? Description, string? Detail);

/// <summary>A profile that cannot be used; the message says what to change.</summary>
public sealed class ProfileException(string message) : Exception(message);
