namespace SIL.Motif.Contract.Commands;

public static partial class RefusalCodes
{
    public const string DraftRevisionConflict = "draft.revision-conflict";
    /// <summary>A Draft request or its content is not valid.</summary>
    public const string DraftInvalid = "draft.invalid";
    /// <summary>A compose-batch <c>forEach</c> asked for a <c>targetsFile</c> from a caller that has no batch file folder, such as MCP.</summary>
    public const string DraftForEachTargetsFileUnavailable = "draft.foreach-targets-file-unavailable";
    /// <summary>A compose-batch <c>forEach</c> names no targets, so it would compose nothing.</summary>
    public const string DraftForEachEmpty = "draft.foreach-empty";
    /// <summary>A compose-batch intent uses a <c>{...}</c> placeholder that <c>forEach</c> does not define.</summary>
    public const string DraftForEachUnknownPlaceholder = "draft.foreach-unknown-placeholder";
    /// <summary>A compose-batch intent uses a <c>{target}</c> placeholder but carries no <c>forEach</c>.</summary>
    public const string DraftForEachPlaceholderWithoutForEach = "draft.foreach-placeholder-without-foreach";
    /// <summary>A compose-batch <c>forEach</c> sits inside another <c>forEach</c> entry.</summary>
    public const string DraftForEachNested = "draft.foreach-nested";
    /// <summary>A compose-batch <c>forEach</c> target names no object, or no unique object, in the project.</summary>
    public const string DraftForEachTargetUnresolved = "draft.foreach-target-unresolved";
    /// <summary>A part-of-speech-only sense analysis needs a linked variant.</summary>
    public const string VariantComponentRequired = "draft.variant-component-required";
    /// <summary>An analysis contains information the empty-sense deletion cannot safely remove.</summary>
    public const string SenseAnalysisContent = "draft.sense-analysis-content";
    /// <summary>The Draft has authored content that automatic cleanup must preserve.</summary>
    public const string DraftAuthoredContent = "draft.authored-content";
    /// <summary>The Draft's Trial results are still pinned or retained, so it is kept.</summary>
    public const string DraftEvidenceRetained = "draft.evidence-retained";
    /// <summary>A Draft with that name already exists.</summary>
    public const string DraftNameCollision = "draft.name-collision";
    /// <summary>No Draft has that name.</summary>
    public const string DraftNotFound = "draft.not-found";
    /// <summary>A phonology Example Set keeps the Draft's results.</summary>
    public const string DraftRetainedByExampleSet = "draft.retained-by-example-set";
    /// <summary>The pending-changes Draft belongs to the person's window and is not discarded by name from the CLI.</summary>
    public const string DraftDiscardPendingChanges = "draft.discard-pending-changes";
    /// <summary>The Draft is being tried; wait for the try to finish before discarding it.</summary>
    public const string DraftTrialRunning = "draft.trial-running";
}
