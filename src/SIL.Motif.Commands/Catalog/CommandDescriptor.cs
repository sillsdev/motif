using System;

namespace SIL.Motif.Commands.Catalog;

/// <summary>Which user choice makes a command available through Motif's front ends.</summary>
public enum CommandSurface
{
    /// <summary>Shown by released help and dispatched by every front end without developer opt-in.</summary>
    Released,

    /// <summary>Shown and dispatched only when developer commands are explicitly enabled.</summary>
    Developer,

    /// <summary>Shown and dispatched only when the user enables Advanced AI mode.</summary>
    AdvancedAi,
}

/// <summary>
/// What an AI agent may do with a command through the MCP server. A command is classified by what its
/// effect is, not by who usually runs it; <see cref="HumanOnly"/> is the default for anything an agent
/// has no business doing unattended.
/// </summary>
public enum AgentClass
{
    /// <summary>Reads Motif's stored state or the project and changes nothing.</summary>
    Read,

    /// <summary>Authors or revises a Draft or Proposal; nothing reaches the FieldWorks project.</summary>
    Draft,

    /// <summary>Starts or reads a job or parser run that produces evidence about a Proposal or grammar.</summary>
    Evaluate,

    /// <summary>
    /// Applies, discards, decides, deletes or changes a person's saved choices. Never registered as an MCP
    /// tool under any profile.
    /// </summary>
    HumanOnly,
}

/// <summary>
/// One entry in <see cref="CommandCatalog.All"/>: a catalogued command's stable name and the typed
/// request/response pair its handler carries. Reflection-bearing (<see cref="Type"/>), so this lives
/// beside the handlers in <c>SIL.Motif.Commands</c> rather than in the wire-only Contract assembly
/// (ADR 0043).
/// </summary>
/// <param name="Name">The command's literal CLI verb, or its space-separated nested form.</param>
/// <param name="RequestType">The typed request record the handler takes.</param>
/// <param name="ResponseType">The typed response record the handler returns on success.</param>
/// <param name="Surface">The user choice required to make the command available.</param>
/// <param name="Agent">Whether, and how, an AI agent may use the command.</param>
public sealed record CommandDescriptor(
    string Name, Type RequestType, Type ResponseType, CommandSurface Surface, AgentClass Agent)
{
    /// <summary>
    /// The MCP tool this command serves, or <see langword="null"/> when no tool is built over it. Several
    /// commands may serve one tool, such as a start and its wait.
    /// </summary>
    public string? AgentTool { get; init; }
}
