using System;

namespace SIL.Motif.Commands.Catalog;

/// <summary>
/// One entry in <see cref="CommandCatalog.All"/>: a catalogued command's stable name and the typed
/// request/response pair its handler carries. Reflection-bearing (<see cref="Type"/>), so this lives
/// beside the handlers in <c>SIL.Motif.Commands</c> rather than in the wire-only Contract assembly
/// (ADR 0043).
/// </summary>
public sealed record CommandDescriptor(string Name, Type RequestType, Type ResponseType);
