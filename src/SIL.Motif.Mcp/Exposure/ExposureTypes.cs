using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Mcp.Exposure;

/// <summary>What the person has switched on, and which tool profile the server was started with.</summary>
/// <param name="Surfaces">The command surfaces the person opted into, which includes Advanced AI mode.</param>
/// <param name="Profile">The tool profile that selects, renames and describes the tools.</param>
internal sealed record ExposureSettings(CommandSurfacePolicy Surfaces, ToolProfile Profile);

/// <summary>An argument value that picks one command out of a tool that serves several.</summary>
/// <param name="Argument">The argument's path in the tool's input, such as <c>steps[].verb</c>.</param>
/// <param name="Value">The value that selects the command.</param>
internal sealed record ToolSelector(string Argument, string Value);

/// <summary>One catalogued command a tool can carry out, keyed by the command's name.</summary>
/// <param name="Command">The catalog command name.</param>
/// <param name="Selector">What to pass to select the command, or <see langword="null"/> when the tool is the command.</param>
/// <param name="Class">The command's agent class, which a tool serving several classes cannot state on its own.</param>
/// <param name="Rank">Which route wins when two tools reach the command; the lower number wins.</param>
internal sealed record ToolRoute(string Command, ToolSelector? Selector, AgentClass Class, int Rank);

/// <summary>What an agent should be told to do to reach a command under the active settings.</summary>
internal abstract record CommandExposure
{
    /// <summary>The command is reached by calling an exposed tool.</summary>
    /// <param name="ToolName">The name the model sees.</param>
    /// <param name="Selector">The selecting argument value, when the tool serves several commands.</param>
    /// <param name="ArgNames">The argument names the tool's schema declares.</param>
    internal sealed record Tool(string ToolName, ToolSelector? Selector, IReadOnlyList<string> ArgNames)
        : CommandExposure;

    /// <summary>The command has no tool in this profile; the person runs it.</summary>
    /// <param name="CliForm">The command's CLI verb.</param>
    internal sealed record PersonStep(string CliForm) : CommandExposure;

    /// <summary>The command's surface is not switched on, so neither agent nor person can run it yet.</summary>
    /// <param name="Facts">The catalog facts naming the opt-in that is missing.</param>
    internal sealed record Unavailable(CommandAvailabilityFacts Facts) : CommandExposure;
}
