using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SIL.Motif.Contract.Responses;

/// <summary>The identity of the batch shapes, so a reader can refuse a version it does not know.</summary>
public static class GenerateBatchSchema
{
    /// <summary>The schema id every <see cref="GenerateBatchResponse"/> carries.</summary>
    public const string Id = "motif.generate-batch.v1";

    /// <summary>The schema id of each <see cref="GenerateBatchRow"/> in the artifact file.</summary>
    public const string RowId = "motif.generate-batch-row.v1";
}

/// <summary>Why a row has no surface: a PanGloss cause or the reason Motif could not send the row.</summary>
/// <param name="Code">The cause code, such as <c>generated_empty</c>, <c>cap</c>, <c>unknown_entry</c> or <c>bad_row</c>.</param>
/// <param name="Message">A sentence saying what happened, or null when the code says it.</param>
public sealed record GenerateBatchCause(string Code, string? Message);

/// <summary>The lexicon objects one row generated from, by FieldWorks GUID.</summary>
/// <param name="Entry">The entry's GUID, lowercase, or null when the input row named none that could be read.</param>
/// <param name="Sense">The sense's GUID, or null when the row named no sense.</param>
/// <param name="Msa">The stem analysis used, or null when the row failed before one was chosen.</param>
public sealed record GenerateBatchTarget(string? Entry, string? Sense, string? Msa);

/// <summary>
/// One line of the artifact: one root analysis in one cell combination, with every surface PanGloss generated for it and
/// the IDs and rule path that made each, or <c>none</c> with its cause. An input row gives one output row per stem
/// analysis of its entry and per combination of its cell.
/// </summary>
/// <param name="Schema"><see cref="GenerateBatchSchema.RowId"/>.</param>
/// <param name="Line">The 1-based input line, blank lines counted; null for a row PanGloss wrote without one.</param>
/// <param name="Id">The input row's id, or its line number when it named none.</param>
/// <param name="Status"><c>ok</c> (at least one surface), <c>none</c> (nothing generated; see <paramref name="Cause"/>) or <c>error</c> (the row could not run).</param>
/// <param name="Cause">Null when <c>ok</c>; otherwise why there is no surface.</param>
/// <param name="Target">The entry, sense and stem analysis the row generated from.</param>
/// <param name="Cell">The affix morphemes of this combination, in generation order; null when the row named no cell.</param>
/// <param name="ComboKey">PanGloss's key for the combination: its analysis GUIDs joined by <c>+</c>, <c>-</c> for none; null without a cell.</param>
/// <param name="CellIndex">The ordinal of the input row's cell among the batch's rows that carry one; shared by every row of one cell.</param>
/// <param name="Complete">False when a cap or deadline stopped generation, so the surfaces are only those found first; null for an error row.</param>
/// <param name="StopReason">Why it stopped early, such as <c>work_limit</c>, or null when complete.</param>
/// <param name="Surfaces">Every surface with its allomorph IDs and rule steps, sorted by surface and then by seed; empty unless <c>ok</c>.</param>
public sealed record GenerateBatchRow(
    string Schema, int? Line, string? Id, string Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] GenerateBatchCause? Cause,
    GenerateBatchTarget Target, IReadOnlyList<FormsExplainMorpheme>? Cell, string? ComboKey, int? CellIndex, bool? Complete,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? StopReason,
    IReadOnlyList<FormsExplainSurface> Surfaces);

/// <summary>A row that has no surface, quoted in the summary.</summary>
/// <param name="Line">The input line.</param>
/// <param name="Id">The input row's id.</param>
/// <param name="Target">The lexicon objects the row generated from.</param>
/// <param name="ComboKey">The combination, or null without a cell.</param>
/// <param name="Cause">Why there is no surface.</param>
public sealed record GenerateBatchNone(int? Line, string? Id, GenerateBatchTarget Target, string? ComboKey, GenerateBatchCause Cause);

/// <summary>
/// The bounded summary of a batch exact synthesis; the full rows are in the file at <paramref name="Out"/>.
/// </summary>
/// <param name="Schema"><see cref="GenerateBatchSchema.Id"/>.</param>
/// <param name="Out">The artifact path the rows were written to, one JSON <see cref="GenerateBatchRow"/> per line.</param>
/// <param name="Rows">How many rows the artifact holds, in PanGloss's order: sort by <c>line</c> to read in input order.</param>
/// <param name="Status">Rows per status: <c>ok</c>, <c>none</c> and <c>error</c>.</param>
/// <param name="Causes">Rows per cause code over the <c>none</c> and <c>error</c> rows.</param>
/// <param name="Surfaces">How many surfaces the artifact holds; a surface two seeds both reach counts twice.</param>
/// <param name="Incomplete">How many rows a cap or deadline stopped.</param>
/// <param name="FirstNone">At most five <c>none</c> rows, in input order.</param>
/// <param name="FirstErrors">At most five <c>error</c> rows, in input order.</param>
/// <param name="Complete">True only when no row stopped on a cap and no row is an error, so no missing surface is a gap in the run.</param>
/// <param name="GrammarHash">The SHA-256 of the grammar file PanGloss generated from, as <c>sha256:</c> and 64 hex digits.</param>
/// <param name="PanglossVersion">The PanGloss version that generated, or <c>unknown</c>.</param>
/// <param name="TracedAgainst"><c>baseline</c>, or <c>proposal &lt;id&gt;</c>.</param>
/// <param name="StopReason">Null for a finished run; <c>timed_out</c>, <c>memory_limit</c>, <c>parser_killed</c>, <c>parser_failed</c> or <c>incomplete</c> when PanGloss stopped early and the artifact holds only the rows written before. Lines with no row are <c>no-result</c> error rows.</param>
/// <param name="StopMessage">What stopped the run, truncated, or null for a finished run.</param>
public sealed record GenerateBatchResponse(
    string Schema, string Out, int Rows, IReadOnlyDictionary<string, int> Status, IReadOnlyDictionary<string, int> Causes,
    int Surfaces, int Incomplete, IReadOnlyList<GenerateBatchNone> FirstNone, IReadOnlyList<GenerateBatchNone> FirstErrors,
    bool Complete, string GrammarHash, string PanglossVersion, string TracedAgainst,
    string? StopReason = null, string? StopMessage = null);
