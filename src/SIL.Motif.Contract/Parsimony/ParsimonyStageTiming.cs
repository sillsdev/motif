namespace SIL.Motif.Contract.Parsimony;

/// <summary>How long one stage of a Parsimony job took on one side of a comparison, as wall time.</summary>
/// <param name="Stage">The stage: <c>states</c>, <c>baseline-reuse</c>, <c>import</c>, <c>facts</c>, <c>adopt</c>,
/// <c>evidence</c>, <c>report</c>, <c>publish</c>, or <c>total</c> for the whole job.</param>
/// <param name="Side"><c>baseline</c> or <c>candidate</c> when the stage ran for one side, otherwise null.</param>
/// <param name="Milliseconds">Wall-clock milliseconds, summed when the stage ran more than once on that side.</param>
public sealed record ParsimonyStageTiming(string Stage, string? Side, long Milliseconds);
