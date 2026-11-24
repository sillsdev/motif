using System.Collections.Generic;

namespace SIL.Motif.Contract.Responses;

/// <summary>
/// What one word lost and gained between the Baseline and a Proposal's Trial copy, and for each lost Reading the stop
/// PanGloss recorded and the Proposal operation that names the object it stopped at.
/// </summary>
/// <remarks>
/// Both sides come from one parse lease, so they describe one project state. A cause is only ever something the
/// parser recorded: where PanGloss recorded no stop for a lost Reading the cause says so and no operation is named.
/// </remarks>
/// <param name="Word">The spelling that was parsed, exactly as given.</param>
/// <param name="ProposalId">The Proposal whose latest Trial copy was read.</param>
/// <param name="TrialJobId">The Trial job whose scratch copy was read, or <see langword="null"/> when none is known.</param>
/// <param name="Baseline">How the Baseline side ended.</param>
/// <param name="Trial">How the Trial side ended.</param>
/// <param name="Lost">Readings the Baseline had and the Trial copy does not.</param>
/// <param name="Gained">Readings the Trial copy has and the Baseline did not.</param>
/// <param name="Kept">Readings both sides have.</param>
public sealed record WhyLostResponse(
    string Word,
    string ProposalId,
    string? TrialJobId,
    WhyLostSide Baseline,
    WhyLostSide Trial,
    IReadOnlyList<WhyLostReading> Lost,
    IReadOnlyList<WhyLostReading> Gained,
    IReadOnlyList<WhyLostReading> Kept)
{
    /// <summary>Sentences about what this comparison does and does not establish.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>How one side's trace ended.</summary>
/// <param name="TracedAgainst"><c>baseline</c> or <c>proposal &lt;id&gt;</c>.</param>
/// <param name="Producer">The parser build and grammar identity the evidence is bound to.</param>
/// <param name="Search">How the parser's search ended.</param>
/// <param name="Outputs">How many outputs the parser returned.</param>
/// <param name="BlockedStopDescribed">Whether the parser that ran describes an <c>allomorph_blocked</c> stop, so its absence can be read.</param>
public sealed record WhyLostSide(
    string TracedAgainst, RejectionTraceProducer Producer, RejectionTraceSearch Search, int Outputs, bool BlockedStopDescribed);

/// <summary>One Reading of the word, by the canonical ids of the morphs that build it.</summary>
/// <param name="Surface">The surface the parser built.</param>
/// <param name="Morphs">The morphs in surface order.</param>
public sealed record WhyLostReading(string Surface, IReadOnlyList<RejectionTraceMorph> Morphs)
{
    /// <summary>For a lost Reading, what PanGloss recorded; one entry per distinct recorded stop, never chosen between.</summary>
    public IReadOnlyList<WhyLostCause> Causes { get; init; } = [];
}

/// <summary>
/// Where PanGloss recorded the loss of one Reading, or that it recorded none.
/// </summary>
/// <param name="Attribution"><c>recorded</c> when PanGloss recorded a stop that names this Reading's form; otherwise <c>not-recorded</c>.</param>
/// <param name="Sentence">One sentence saying what the parser recorded, or that it recorded nothing.</param>
public sealed record WhyLostCause(string Attribution, string Sentence)
{
    /// <summary>The parser's stable reason code, or <see langword="null"/> when none was recorded.</summary>
    public string? Reason { get; init; }

    /// <summary>The stage that owned the decision, or <see langword="null"/> when none was recorded.</summary>
    public string? Stage { get; init; }

    /// <summary>The form that was blocked, or <see langword="null"/>.</summary>
    public WhyLostForm? Blocked { get; init; }

    /// <summary>The form that blocked it, or <see langword="null"/>.</summary>
    public WhyLostForm? Blocker { get; init; }

    /// <summary>The Proposal operations that name the blocking form, empty when none does or none was recorded.</summary>
    public IReadOnlyList<WhyLostOperation> Operations { get; init; } = [];
}

/// <summary>A form the parser named in a stop.</summary>
/// <param name="Id">The canonical id of the authored form, or <see langword="null"/> when the parser could not bind one.</param>
/// <param name="EntryId">The canonical id of the entry that owns the form, or <see langword="null"/>.</param>
/// <param name="Text">The form as written in the Trial copy, or <see langword="null"/> when it could not be read.</param>
public sealed record WhyLostForm(string? Id, string? EntryId, string? Text);

/// <summary>A Proposal operation that names an object, and how.</summary>
/// <param name="Position">The one-based position of the operation in the Proposal.</param>
/// <param name="OperationId">The operation's canonical id.</param>
/// <param name="Kind">The operation kind.</param>
/// <param name="Role"><c>creates</c>, <c>edits</c>, <c>placed-beside</c>, <c>added-to</c> or <c>names-object-it-uses</c>.</param>
/// <param name="Sentence">One sentence saying what the operation does to the object.</param>
public sealed record WhyLostOperation(int Position, string OperationId, string Kind, string Role, string Sentence);
