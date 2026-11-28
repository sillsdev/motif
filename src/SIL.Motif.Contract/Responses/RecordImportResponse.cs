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
/// <param name="Old">The value before, as the file spells it, or null when there was none. It is project text.</param>
/// <param name="New">The value after, as the file spells it, not the value of the operation; null when it is cleared.</param>
/// <param name="Status"><c>staged</c> when the change has operations, or <c>refused</c> when it will not be made.</param>
/// <param name="RefusalCode">The <c>record-import.*</c> code of a refused change; null for a staged one.</param>
public sealed record RecordImportChange(string Kind, string? Class, string Field, string? Object, string? WritingSystem,
    string? Old, string? New, string Status = "staged", string? RefusalCode = null);

/// <summary>One change the import will not make, named by a registered refusal code.</summary>
/// <param name="Code">A <c>record-import.*</c> refusal code.</param>
/// <param name="Class">The record schema's object type the change is about.</param>
/// <param name="Field">The field path of the change, empty for the object itself.</param>
/// <param name="Object">The canonical id of the object, when known.</param>
/// <param name="Reason">What stopped the change, shortened to <see cref="Requests.RecordImportPaging.TextLength"/>.</param>
public sealed record RecordImportRefusal(string Code, string? Class, string Field, string? Object, string Reason);

/// <summary>One file of a folder import.</summary>
/// <param name="Path">The file's path relative to the folder, with <c>/</c> separators.</param>
/// <param name="Kind">The record kind the file names, when it could be read.</param>
/// <param name="Record">The canonical id of the project record the file is about, when known.</param>
/// <param name="Status"><c>created</c>, <c>updated</c>, <c>unchanged</c> or <c>refused</c>.</param>
/// <param name="BaseVerdict"><c>new</c>, <c>fresh</c>, <c>stale</c>, <c>missing</c>, <c>unchanged</c> or <c>not-checked</c>.</param>
/// <param name="Changes">The changes the file makes.</param>
/// <param name="Operations">The operations that will be staged for the file.</param>
/// <param name="Refused">The changes of the file that were refused.</param>
/// <param name="Code">The <c>record-import.*</c> code of the file's first refusal, whole-file or of a change.</param>
/// <param name="Reason">What stopped it, shortened.</param>
public sealed record RecordImportFileRow(string Path, string? Kind, string? Record, string Status, string BaseVerdict,
    int Changes, int Operations, int Refused, string? Code = null, string? Reason = null);

/// <summary>An existing entry whose homograph number a new entry of the folder changes.</summary>
/// <param name="Entry">The canonical id of the existing entry.</param>
/// <param name="Before">Its homograph number now; 0 when it has none.</param>
/// <param name="After">Its number once the new entries are in; 0 when it has none.</param>
public sealed record RecordImportHomographRow(string Entry, int Before, int After);

/// <summary>An existing entry that gains back-references from new entries of the folder.</summary>
/// <param name="Entry">The canonical id of the existing entry.</param>
/// <param name="Variants">The new variant links that name it as their main entry or sense.</param>
/// <param name="ComplexForms">The new complex-form links that name it as a component.</param>
public sealed record RecordImportBackReferenceRow(string Entry, int Variants, int ComplexForms);

/// <summary>
/// What a reviewer of a folder of new entries must see besides the files: existing entries renumbered or pointed at, found by
/// trying the operations on a scratch copy of the project. The rows are the first few, in canonical id order.
/// </summary>
/// <param name="HomographsRenumbered">The existing entries whose homograph number changes.</param>
/// <param name="BackReferenced">The existing entries that gain a variant or complex-form back-reference.</param>
/// <param name="BatchEdges">The links from a file of the folder to a new record of another file.</param>
/// <param name="FirstHomographs">At most <see cref="Requests.RecordImportPaging.SummaryReviewRows"/> renumbered entries.</param>
/// <param name="FirstBackReferences">At most <see cref="Requests.RecordImportPaging.SummaryReviewRows"/> back-referenced entries.</param>
public sealed record RecordImportReview(int HomographsRenumbered, int BackReferenced, int BatchEdges,
    IReadOnlyList<RecordImportHomographRow> FirstHomographs, IReadOnlyList<RecordImportBackReferenceRow> FirstBackReferences);

/// <summary>What a folder import found, in counts. The rows are listed by page.</summary>
/// <param name="Files">The <c>*.fw.md</c> files read.</param>
/// <param name="Created">Files that create a record.</param>
/// <param name="Updated">Files that change a record.</param>
/// <param name="Unchanged">Files that say what the project already holds.</param>
/// <param name="Refused">Files refused whole, with their code in the row.</param>
/// <param name="Ignored">Entries that are not <c>*.fw.md</c> files, or are links, and so were not read.</param>
/// <param name="Manifest">The sha256 of the per-file manifest (path, text digest, record, status), so a stage can tell the folder changed.</param>
/// <param name="FirstRefused">The first refused rows, at most <see cref="Requests.RecordImportPaging.SummaryRefusedRows"/>.</param>
/// <param name="Source">The source the options named.</param>
/// <param name="Revision">The revision the options named.</param>
/// <param name="Licence">The licence the options named.</param>
/// <param name="Review">The renumbered and back-referenced existing entries; null when the folder creates no entry.</param>
public sealed record RecordImportDirSummary(int Files, int Created, int Updated, int Unchanged, int Refused, int Ignored,
    string Manifest, IReadOnlyList<RecordImportFileRow> FirstRefused, string? Source = null, string? Revision = null,
    string? Licence = null, RecordImportReview? Review = null);

/// <summary>
/// What a record-file import found. The CLI <c>import --file --json</c> and the MCP tool return exactly this. Nothing is
/// ever applied: a dry run writes nothing, and a staged import only adds a Draft. A file refused whole is not this
/// response but a refusal outcome.
/// </summary>
/// <param name="Mode"><c>dryRun</c> or <c>draft</c>.</param>
/// <param name="RecordKind">The record kind the file names.</param>
/// <param name="RecordId">The canonical id of the project record the file is about.</param>
/// <param name="BaseVerdict">Whether the file's base matches a fresh export: <c>fresh</c>, <c>new</c> for a record the project does not have yet, or <c>unchanged</c>.</param>
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
/// <param name="Dir">For a folder import, the counts and the first refused files; the fields above then hold totals and <c>baseVerdict</c> is <c>not-checked</c>.</param>
/// <param name="Files">For a folder import asked for by page, that page of file rows.</param>
/// <param name="ReportRows">For a folder import given a report path, every file row, for the host to write to that file; never serialized.</param>
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
    IReadOnlyList<RecordImportChange>? Changes = null,
    RecordImportDirSummary? Dir = null,
    IReadOnlyList<RecordImportFileRow>? Files = null,
    [property: System.Text.Json.Serialization.JsonIgnore] IReadOnlyList<RecordImportFileRow>? ReportRows = null);
