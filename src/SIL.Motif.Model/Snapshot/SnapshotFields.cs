namespace SIL.Motif.Model.Snapshot;

/// <summary>
/// Canonical field-name constants used as keys into <see cref="ObjectSnapshot.AlternativesFields"/>
/// and as the <c>field</c> value of an expected effect. These are exactly the "field" strings the
/// Change Set contract's "Expected effects" section shows (e.g. <c>"lexical/sense/gloss"</c>), so a
/// snapshot field and an effect's field always agree textually.
/// </summary>
/// <remarks>
/// Stage C populated exactly one: the <see cref="LexSenseGloss"/> field behind the in-scope
/// <c>lexical/lexSense/setGloss</c> operation. <c>partial</c> because the remaining
/// in-scope basic <c>set|clear</c> fields are added in a generated sibling file
/// (<c>SnapshotFields.Generated.g.cs</c>, emitted by <c>SIL.Motif.Generator</c>) rather than by
/// hand-editing this one; the snapshot/effect shapes below do not need to change to accommodate them.
/// </remarks>
public static partial class SnapshotFields
{
    /// <summary>A <c>LexSense</c>'s <c>Gloss</c> MultiUnicode field.</summary>
    public const string LexSenseGloss = "lexical/sense/gloss";

    /// <summary>
    /// A <c>MoStemMsa</c>'s <c>MsFeatures</c> owning/atomic <c>FsFeatStruc</c> field — hand-written
    /// like <see cref="LexSenseGloss"/> above, because <c>owning/atomic</c> beyond one already-built
    /// field still has no generator support (its creation validity is not derivable from the model
    /// file alone).
    /// </summary>
    public const string MoStemMsaMsFeatures = "grammar/moStemMsa/msFeatures";

    /// <summary>
    /// An <c>FsFeatStruc</c>'s <c>FeatureSpecs</c> owning/col <c>FsFeatureSpecification</c> field --
    /// hand-written for the same reason as <see cref="MoStemMsaMsFeatures"/>: an owning/col creation
    /// validity decision is not derivable from the model file alone.
    /// </summary>
    public const string FsFeatStrucFeatureSpecs = "grammar/fsFeatStruc/featureSpecs";

    /// <summary>A <c>WfiAnalysis</c>'s evaluation references.</summary>
    public const string WfiAnalysisEvaluations = "analysis/wfiAnalysis/evaluations";

    /// <summary>A <c>WfiWordform</c>'s owned analyses.</summary>
    public const string WfiWordformAnalyses = "analysis/wfiWordform/analyses";

    /// <summary>A <c>WfiMorphBundle</c>'s authoritative <c>Morph</c> reference.</summary>
    public const string WfiMorphBundleMorph = "analysis/wfiMorphBundle/morph";

    /// <summary>A <c>WfiMorphBundle</c>'s copied multilingual <c>Form</c> text.</summary>
    public const string WfiMorphBundleForm = "analysis/wfiMorphBundle/form";

    /// <summary>The full live incoming-reference census bound to allomorph retargeting.</summary>
    public const string AllomorphRetargetCensus = "retirement/allomorph/referenceCensus";

    /// <summary>The complete owned object graph removed by redundant zero-affix retirement.</summary>
    public const string RetireRedundantZeroAffixGraph = "retirement/redundantZeroAffix/graph";

    /// <summary>The semantic context read by a retained-form retarget operation.</summary>
    public const string AllomorphRetargetContext = "retirement/allomorph/retargetContext";

    /// <summary>The ordered alternate forms owned by a lexical entry.</summary>
    public const string LexEntryAlternateForms = "lexical/lexEntry/alternateForms";

    /// <summary>
    /// The Canonical Semantic Snapshot / expected-effect projection shape version, recorded on
    /// <see cref="SIL.Motif.Model.DryRun.BoundDryRunAnchor.ProjectionVersion"/>. Bump this
    /// only when the snapshot/effect shape changes in a way that could alter a digest for otherwise
    /// unchanged content.
    /// </summary>
    public const string ProjectionVersion = "4";
}
