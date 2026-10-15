using SIL.Motif.Commands.Catalog;

namespace SIL.Motif.Commands;

/// <summary>Where and how a composer definition is offered to people and to AI agents.</summary>
/// <param name="Surface">The user choice that makes the composer available.</param>
/// <param name="Agent">What an AI agent may do with the composer.</param>
/// <param name="OnByDefault">Whether the composer's AI tool is registered without the user naming it.</param>
/// <param name="InBatch">Whether a batch may name the composer's verb.</param>
/// <param name="ToolName">The AI tool's name when it is not the one the naming rule gives.</param>
/// <param name="AgentTool">
/// Whether AI agents are offered the composer at all. False keeps it CLI-first: no MCP tool and no place in the MCP batch.
/// </param>
internal sealed record ComposerExposure(
    CommandSurface Surface,
    AgentClass Agent = AgentClass.Draft,
    bool OnByDefault = false,
    bool InBatch = true,
    string? ToolName = null,
    bool AgentTool = true);
