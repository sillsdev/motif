namespace SIL.Motif.Contract.Responses;

/// <summary>One operation a record file lowers to.</summary>
/// <param name="OperationId">The operation's canonical id, derived from the file, so importing it twice names the same operations.</param>
/// <param name="Kind">The operation kind, for example <c>lexical/lexSense/setGloss</c>.</param>
/// <param name="Target">The canonical id of the object it changes.</param>
public sealed record RecordImportOperation(string OperationId, string Kind, string? Target);

/// <summary>One change the file makes: the field, and the value it had and will have, as the file spells them.</summary>
/// <param name="Kind">The change kind, for example <c>Set</c> or <c>AddReference</c>.</param>
/// <param name="Class">The record schema's object type the field belongs to.</param>
/// <param name="Field">The field path of the change.</param>
/// <param name="Object">The canonical id of the object the field belongs to, when known.</param>
/// <param name="WritingSystem">The writing system of an alternative, when the change is to one.</param>
/// <param name="Old">The value before, or null when there was none.</param>
/// <param name="New">The value after, or null when it is cleared.</param>
public sealed record RecordImportChange(string Kind, string? Class, string Field, string? Object, string? WritingSystem,
    string? Old, string? New);

/// <summary>One change the import will not make, named by a registered refusal code.</summary>
/// <param name="Code">A <c>record-import.*</c> refusal code.</param>
/// <param name="Class">The record schema's object type the change is about.</param>
/// <param name="Field">The field path of the change, empty for the object itself.</param>
/// <param name="Object">The canonical id of the object, when known.</param>
/// <param name="Reason">What stopped the change, shortened to <see cref="Requests.RecordImportPaging.TextLength"/>.</param>
public sealed record RecordImportRefusal(string Code, string? Class, string Field, string? Object, string Reason);

/// <summary>
/// What a record-file import found. The CLI <c>import --file --json</c> and the MCP tool return exactly this. Nothing is
/// ever applied: a dry run writes nothing, and a staged import only adds a Draft. A file refused whole is not this
/// response but a refusal outcome.
/// </summary>
/// <param name="Mode"><c>dryRun</c> or <c>draft</c>.</param>
/// <param name="RecordKind">The record kind the file names.</param>
/// <param name="RecordId">The canonical id of the project record the file is about.</param>
/// <param name="BaseVerdict">Whether the file's base matches a fresh export: <c>fresh</c> for any file that gets this far.</param>
/// <param name="ChangeCount">The changes the file makes.</param>
/// <param name="OperationCount">The operations those changes lowered to.</param>
/// <param name="RefusalCount">The changes the import declines: the importer's, the lowering's, and those a scratch copy of the project refused when the operations were tried on it. A staged Draft holds none of them.</param>
/// <param name="Page">The page listed, from 1.</param>
/// <param name="PageCount">How many pages the longest of the three lists has.</param>
/// <param name="Operations">This page of the operations, in the order to apply them.</param>
/// <param name="Refusals">This page of the refusals.</param>
/// <param name="DraftName">The Draft the operations were staged in; null for a dry run.</param>
/// <param name="ProposalId">The Draft's canonical id; null for a dry run.</param>
/// <param name="Changes">This page of the changes the file makes, with the field and the old and new value of each.</param>
public sealed record RecordImportResponse(
    string Mode,
    string? RecordKind,
    string? RecordId,
    string BaseVerdict,
    int ChangeCount,
    int OperationCount,
    int RefusalCount,
    int Page,
    int PageCount,
    IReadOnlyList<RecordImportOperation> Operations,
    IReadOnlyList<RecordImportRefusal> Refusals,
    string? DraftName = null,
    string? ProposalId = null,
    IReadOnlyList<RecordImportChange>? Changes = null);
