using System.Collections.Generic;

namespace SIL.Motif.Contract.Commands;

/// <summary>A fatal grammar issue, preserving the parser's authored identity and repair advice.</summary>
public sealed record ParserCompileIssue(string Code, string Kind, string? ObjectGuid, string? Field,
    string Text, string Advice, string? ObjectKind = null);

/// <summary>A compile refusal with readable fatal issues and the complete original parser output.</summary>
public sealed record ParserCompileDiagnostic(string Summary, IReadOnlyList<ParserCompileIssue> Issues, string RawText);
