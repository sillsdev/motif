using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Commands;

/// <summary>Where and how a composer definition is offered to people and to AI agents.</summary>
/// <param name="Surface">The user choice that makes the composer available.</param>
/// <param name="Agent">What an AI agent may do with the composer.</param>
/// <param name="InBatch">Whether the retiring MCP batch may name the composer's verb.</param>
/// <param name="ToolName">The AI tool's name when it is not the one the naming rule gives.</param>
/// <param name="AgentTool">
/// Whether AI agents are offered the composer at all. False keeps it CLI-first: no MCP tool and no place in the MCP batch.
/// </param>
/// <param name="InCompose">Whether a step of the MCP compose tool may name the composer's verb.</param>
/// <param name="HandWrittenTool">
/// Whether the composer's own MCP tool is written by hand with another argument shape, so the definition supplies
/// only the schema of a compose or batch step and no generated tool.
/// </param>
internal sealed record ComposerExposure(
    CommandSurface Surface,
    AgentClass Agent = AgentClass.Draft,
    bool InBatch = true,
    string? ToolName = null,
    bool AgentTool = true,
    bool InCompose = true,
    bool HandWrittenTool = false);
