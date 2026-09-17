using System;

namespace SIL.Motif.Commands.Catalog;

/// <summary>Whether a command belongs to the public release or the developer-only surface.</summary>
public enum CommandSurface
{
    /// <summary>Shown by released help and dispatched by every front end without developer opt-in.</summary>
    Released,

    /// <summary>Shown and dispatched only when developer commands are explicitly enabled.</summary>
    Developer,
}

/// <summary>
/// One entry in <see cref="CommandCatalog.All"/>: a catalogued command's stable name and the typed
/// request/response pair its handler carries. Reflection-bearing (<see cref="Type"/>), so this lives
/// beside the handlers in <c>SIL.Motif.Commands</c> rather than in the wire-only Contract assembly
/// (ADR 0043).
/// </summary>
public sealed record CommandDescriptor(
    string Name, Type RequestType, Type ResponseType, CommandSurface Surface);
