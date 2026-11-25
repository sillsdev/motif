namespace SIL.Motif.Contract.Requests;

/// <summary>
/// Writes record files from the saved project, the files <c>import</c> reads back. It names one record by its id, or every
/// root record of a kind, and writes nothing to the project. Each value a file leaves out is counted in the response.
/// </summary>
/// <param name="ProjectPath">The FieldWorks project to export from.</param>
/// <param name="OutPath">The file to write for one record, or the folder to write one file per record into for all of them. A file that already exists is not replaced.</param>
/// <param name="RecordId">The GUID of the record to export; leave it out with <c>All</c>.</param>
/// <param name="Kind">The record kind: <c>lexicon/entry</c>, <c>notebook/record</c> or <c>lists/item</c>. Default <c>lexicon/entry</c>.</param>
/// <param name="All">Export every root record of the kind, one file each, named by the record's GUID.</param>
public sealed record RecordExportRequest(string ProjectPath, string OutPath, string? RecordId = null, string? Kind = null,
    bool All = false);
