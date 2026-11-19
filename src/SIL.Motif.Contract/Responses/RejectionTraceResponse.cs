using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// Where one candidate path of a parse or generation stopped, as the parser that ran reported it, with each
/// grammar object named by Motif's canonical ids where they resolve and by an explicit unknown where they do not.
/// </summary>
/// <remarks>
/// The response is bounded: at most <see cref="RejectionTraceBound.MaxPaths"/> paths and
/// <see cref="RejectionTraceLimits.OutputPreviewCap"/> output previews, whatever the parser kept. An attribution
/// of <c>unknown</c> is a finding, not a gap to fill: Motif never infers a cause the parser did not record.
/// </remarks>
/// <param name="Operation"><c>parse</c> or <c>generate</c>.</param>
/// <param name="Input">The spelling that was parsed, exactly as given.</param>
/// <param name="TracedAgainst"><c>baseline</c>, or <c>proposal &lt;id&gt;</c> for that Proposal's latest Trial copy.</param>
/// <param name="Producer">The parser build and grammar identity the evidence is bound to.</param>
/// <param name="Search">How the parser's search ended, independent of what evidence was kept.</param>
/// <param name="Capture">What the parser could not keep, with the accuracy of each count.</param>
/// <param name="Outputs">The parse outputs the parser returned, each with its morphs named by canonical id.</param>
/// <param name="Paths">The retained terminal paths, rejected paths first in the parser's emission order.</param>
/// <param name="Bound">The caps applied, and what they left out.</param>
public sealed record RejectionTraceResponse(
    string Operation,
    string Input,
    string TracedAgainst,
    RejectionTraceProducer Producer,
    RejectionTraceSearch Search,
    RejectionTraceCapture Capture,
    IReadOnlyList<RejectionTraceOutput> Outputs,
    IReadOnlyList<RejectionTracePath> Paths,
    RejectionTraceBound Bound)
{
    /// <summary>Sentences about what this evidence does and does not establish.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>The fixed bounds of a rejection trace response.</summary>
public static class RejectionTraceLimits
{
    /// <summary>The default number of path witnesses.</summary>
    public const int DefaultMaxPaths = 16;

    /// <summary>The most path witnesses a request may ask for.</summary>
    public const int MaximumPaths = 64;

    /// <summary>The most outputs a response previews.</summary>
    public const int OutputPreviewCap = 64;

    /// <summary>The most operand entries kept for one event.</summary>
    public const int OperandEntryCap = 8;

    /// <summary>The longest operand value kept, in characters.</summary>
    public const int OperandValueCap = 120;
}

/// <summary>The parser and grammar a rejection trace is bound to.</summary>
/// <param name="ParserVersion">The parser's reported version.</param>
/// <param name="ParserBuild">The parser's reported build.</param>
/// <param name="Schema">The wire schema the evidence arrived in.</param>
/// <param name="Mode">The capture mode the parser used.</param>
/// <param name="GrammarHash">The grammar hash the parser recorded, or <see langword="null"/> when it recorded none.</param>
/// <param name="GrammarHashSemantics">What the hash covers; hashes with different semantics never compare.</param>
public sealed record RejectionTraceProducer(
    string ParserVersion, string ParserBuild, string Schema, string Mode, string? GrammarHash,
    string? GrammarHashSemantics);

/// <summary>How the parser's search for one word ended.</summary>
/// <param name="Status"><c>complete</c>, <c>stopped</c>, <c>invalid</c> or <c>refused</c>.</param>
/// <param name="OutputCensus"><c>complete</c>, <c>partial</c> or <c>unavailable</c>.</param>
/// <param name="OutputCount">The outputs the ordinary search returned before any capture limit.</param>
/// <param name="WorkUnits">The parser's work for the word, or <see langword="null"/> when unreported.</param>
/// <param name="StopReasons">Why the search stopped early, empty when it ran to completion.</param>
public sealed record RejectionTraceSearch(
    string Status, string OutputCensus, long? OutputCount, long? WorkUnits, IReadOnlyList<string> StopReasons)
{
    /// <summary>The typed error the parser attached to this word, or <see langword="null"/>.</summary>
    public string? ErrorCode { get; init; }
}

/// <summary>What the capture kept and what it dropped.</summary>
/// <param name="Coverage">How much history the parser kept, as it spells it.</param>
/// <param name="TruncatedBy">The capture limits that were reached.</param>
/// <param name="OmittedPaths">Paths dropped by a capture limit.</param>
/// <param name="OmittedEvents">Events dropped by a capture limit.</param>
/// <param name="OmittedOutputs">Outputs dropped by a capture limit.</param>
public sealed record RejectionTraceCapture(
    string Coverage, IReadOnlyList<string> TruncatedBy, RejectionTraceCount OmittedPaths,
    RejectionTraceCount OmittedEvents, RejectionTraceCount OmittedOutputs);

/// <summary>A count with the accuracy the parser gave it.</summary>
/// <param name="Value">The count, or <see langword="null"/> when unknown.</param>
/// <param name="Accuracy"><c>exact</c>, <c>lowerBound</c> or <c>unknown</c>.</param>
public sealed record RejectionTraceCount(long? Value, string Accuracy);

/// <summary>One parse output and the grammar objects that built it.</summary>
/// <param name="OutputId">The row-local output ordinal.</param>
/// <param name="Surface">The surface the output spells.</param>
/// <param name="RootIndex">The zero-based position of the root among the morphs, or <see langword="null"/>.</param>
/// <param name="Projection"><c>exact</c> when the morphs are authored identities; otherwise the parser's word.</param>
/// <param name="Morphs">The morphs in surface order.</param>
public sealed record RejectionTraceOutput(
    int OutputId, string Surface, int? RootIndex, string Projection, IReadOnlyList<RejectionTraceMorph> Morphs);

/// <summary>One morph of an output or target, by canonical id where the parser gave an authored identity.</summary>
/// <param name="Kind"><c>authored</c> for an authored entry and MSA; any other value has no authored identity.</param>
/// <param name="Entry">The lexical entry, or <see langword="null"/>.</param>
/// <param name="Msa">The morphosyntactic analysis, or <see langword="null"/>.</param>
/// <param name="Forms">The form parts in order; a multipart form lists every part.</param>
/// <param name="InflectionType">The variant inflection type, or <see langword="null"/>.</param>
/// <param name="SenseResolution"><c>unique_association</c>, <c>shared_msa</c> or <c>unavailable</c>.</param>
/// <param name="AssociatedSenseIds">The senses the MSA is associated with, in the parser's order.</param>
public sealed record RejectionTraceMorph(
    string Kind, RejectionTraceObject? Entry, RejectionTraceObject? Msa, IReadOnlyList<RejectionTraceObject> Forms,
    RejectionTraceObject? InflectionType, string SenseResolution, IReadOnlyList<string> AssociatedSenseIds);

/// <summary>One retained terminal path and where it stopped.</summary>
/// <param name="PathId">The row-local path id.</param>
/// <param name="ParentPathId">The path this one continues, or <see langword="null"/> for a root or an omitted parent.</param>
/// <param name="Depth">The number of lineage edges, or <see langword="null"/> when the parser did not record it.</param>
/// <param name="Outcome"><c>rejected</c>, <c>accepted</c> or <c>unresolved</c>.</param>
/// <param name="Attribution">
/// <c>rejected</c> names the earliest stop; <c>accepted</c> names the output; <c>unknown</c> says Motif has no
/// recorded stop to name, and <see cref="Unknown"/> says why.
/// </param>
/// <param name="OutputId">The output this path produced, when it was accepted and kept.</param>
/// <param name="EarliestRejection">The first terminal stop on this path, or <see langword="null"/>.</param>
/// <param name="Unknown">Why no earliest rejection is named, or <see langword="null"/> when one is.</param>
/// <param name="Sentence">One sentence saying what happened to this path.</param>
public sealed record RejectionTracePath(
    int PathId, int? ParentPathId, int? Depth, string Outcome, string Attribution, int? OutputId,
    RejectionTraceEvent? EarliestRejection, string? Unknown, string Sentence);

/// <summary>The earliest terminal stop on a path.</summary>
/// <param name="EventId">The row-local event id.</param>
/// <param name="Direction"><c>analysis</c> or <c>synthesis</c>.</param>
/// <param name="Stage">The parser stage that owned the decision.</param>
/// <param name="Reason">The parser's stable reason code.</param>
/// <param name="Subject">The grammar object whose gate stopped the path.</param>
/// <param name="Related">Other objects the event names, by their role.</param>
/// <param name="Operands">The scalar operands the parser recorded, capped.</param>
/// <param name="Sentence">The reason in a sentence a person can read, ending with what is not established.</param>
public sealed record RejectionTraceEvent(
    int EventId, string Direction, string Stage, string Reason, RejectionTraceObject Subject,
    IReadOnlyList<RejectionTraceRelated> Related, IReadOnlyDictionary<string, string> Operands, string Sentence);

/// <summary>An object an event names besides its subject.</summary>
/// <param name="Role">The role the parser gave it, such as <c>candidate</c> or <c>tested_template</c>.</param>
/// <param name="Object">The object.</param>
public sealed record RejectionTraceRelated(string Role, RejectionTraceObject Object);

/// <summary>
/// One grammar object by canonical id where it resolves. <see cref="Resolved"/> is false when the parser could not
/// bind it to authored data, and <see cref="Unknown"/> then says why; the compiled key stays so the object can
/// still be told apart within this grammar.
/// </summary>
/// <param name="Kind">The kind of object, as the parser spells it.</param>
/// <param name="Key">The parser's key: an authored GUID, or a compiled key scoped to its build and grammar.</param>
/// <param name="Quality"><c>authored</c>, <c>compiled</c>, <c>synthetic</c>, <c>xml</c> or <c>unavailable</c>.</param>
/// <param name="BindingStatus"><c>exact</c>, <c>multiple</c>, <c>structural</c> or <c>unavailable</c>.</param>
/// <param name="Resolved">Whether at least one canonical id names the object itself.</param>
/// <param name="Ids">Every authored source, by canonical id and the relation the parser recorded for it.</param>
/// <param name="Unknown">Why the object has no canonical id of its own, or <see langword="null"/>.</param>
public sealed record RejectionTraceObject(
    string Kind, string Key, string Quality, string BindingStatus, bool Resolved,
    IReadOnlyList<RejectionTraceId> Ids, string? Unknown);

/// <summary>One authored source of an object.</summary>
/// <param name="Id">The canonical id of the authored FieldWorks object.</param>
/// <param name="Kind">The kind of authored object the id names.</param>
/// <param name="Relation">How it relates to the object: <c>authored</c>, <c>form_part</c>, <c>owning_entry</c> and so on.</param>
/// <param name="Ordinal">Its zero-based source position.</param>
/// <param name="IsSubject">Whether the id names the object itself rather than something it relates to.</param>
public sealed record RejectionTraceId(string Id, string Kind, string Relation, long Ordinal, bool IsSubject);

/// <summary>The caps a response applied and what they left out.</summary>
/// <param name="MaxPaths">The path cap requested.</param>
/// <param name="TerminalPathsRetained">The terminal paths the parser kept.</param>
/// <param name="PathsShown">The terminal paths in <see cref="RejectionTraceResponse.Paths"/>.</param>
/// <param name="PathsOmittedByCap">The terminal paths dropped here by <paramref name="MaxPaths"/>.</param>
/// <param name="OutputsShown">The outputs in <see cref="RejectionTraceResponse.Outputs"/>.</param>
/// <param name="OutputsOmittedByCap">The outputs dropped here by the output preview cap.</param>
public sealed record RejectionTraceBound(
    int MaxPaths, int TerminalPathsRetained, int PathsShown, int PathsOmittedByCap, int OutputsShown,
    int OutputsOmittedByCap)
{
    /// <summary>Whether anything was left out here or by the parser's own capture limits.</summary>
    public bool Truncated => PathsOmittedByCap > 0 || OutputsOmittedByCap > 0;
}
