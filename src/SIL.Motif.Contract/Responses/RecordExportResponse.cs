namespace SIL.Motif.Contract.Responses;

/// <summary>One kind of value a record file leaves out, counted over every record written.</summary>
/// <param name="Class">The project class that holds the value.</param>
/// <param name="Field">The field name, or <c>(record)</c> when the whole record could not be written.</param>
/// <param name="Reason">Why the file does not hold it.</param>
/// <param name="Count">How many values share this class, field and reason.</param>
public sealed record RecordExportUnexported(string Class, string Field, string Reason, int Count);

/// <summary>
/// What a record export wrote. Project values never appear here: only counts and the reasons values were left out, so
/// a person who edits the file knows what the file does not carry. A record that could not be written is counted in
/// <see cref="NotWrittenCount"/> and listed under <c>(record)</c>; the others are still written.
/// </summary>
/// <param name="RecordKind">The record kind exported.</param>
/// <param name="RecordCount">The records the project has for the request.</param>
/// <param name="WrittenCount">The record files written.</param>
/// <param name="NotWrittenCount">The records that could not be written as a file.</param>
/// <param name="UnexportedCount">Every value the written and unwritten records leave out.</param>
/// <param name="Unexported">The left-out values grouped by class, field and reason, in a fixed order.</param>
/// <param name="OutPath">The file written, or the folder the files went into.</param>
/// <param name="Next">What to do with the file; an edited file is read back by import, and a project that changed since needs a new export.</param>
public sealed record RecordExportResponse(
    string RecordKind,
    int RecordCount,
    int WrittenCount,
    int NotWrittenCount,
    int UnexportedCount,
    IReadOnlyList<RecordExportUnexported> Unexported,
    string OutPath,
    string Next);
