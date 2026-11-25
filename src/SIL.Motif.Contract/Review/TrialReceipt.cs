namespace SIL.Motif.Contract.Review;

/// <summary>
/// What a green Trial held to the author's expectations says about one Proposal revision, one Baseline and one build of
/// the gate. Apply honours it only while all three are still current, as the lane gate's receipt is bound to the live
/// revision and the gate script's sha.
/// </summary>
/// <param name="SchemaVersion">The version of this shape.</param>
/// <param name="ProposalId">The Proposal the Trial measured.</param>
/// <param name="IntentDigest">The Proposal revision the Trial measured and Apply must still find current.</param>
/// <param name="BaselineSemanticDigest">The Baseline the Trial measured and Apply must still find current.</param>
/// <param name="TrialJobId">The Trial whose retained evidence was held to the expectations.</param>
/// <param name="GateCodeDigest">The build of the expectation rules that judged it.</param>
/// <param name="ExpectationsDigest">A digest of the manifests and caps that were held, so a reviewer can tell receipts apart.</param>
/// <param name="State">Always <see cref="ReadingExpectationStates.Green"/>: a receipt is written only for a green Trial.</param>
/// <param name="Measured">The forms the Trial measured.</param>
/// <param name="Doubled">The doubled words the Trial showed, within the caps.</param>
/// <param name="ReceiptDigest">A digest of every other member, so an edited receipt is refused.</param>
public sealed record TrialReceipt(int SchemaVersion, string ProposalId, string IntentDigest, string BaselineSemanticDigest,
    string TrialJobId, string GateCodeDigest, string ExpectationsDigest, string State, long Measured, long Doubled,
    string ReceiptDigest)
{
    /// <summary>The current version of this shape.</summary>
    public const int Version = 1;
}

/// <summary>Where a receipt was written, reported on the delta that earned it.</summary>
/// <param name="Path">The receipt file.</param>
/// <param name="ReceiptDigest">Its digest, which <c>apply</c> checks.</param>
/// <param name="GateCodeDigest">The gate build it is bound to.</param>
public sealed record TrialReceiptSummary(string Path, string ReceiptDigest, string GateCodeDigest);
