using System.Collections.Generic;

namespace SIL.Motif.Commands.Requests;

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
public sealed record OpenRequest(string FwDataPath);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
public sealed record ManualAnalysesRequest(string FwDataPath);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="AssessmentId">The explicitly authored assessment id value.</param>
/// <param name="CurrentSelectionSha256">The explicitly authored current selection sha256 value.</param>
/// <param name="CurrentGrammarSourceSha256">The explicitly authored current grammar source sha256 value.</param>
public sealed record AssessmentAnalysesRequest(
    string FwDataPath,
    string ProductVersion,
    string AssessmentId,
    string CurrentSelectionSha256,
    string CurrentGrammarSourceSha256);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="Label">The short description a person reads while reviewing the Proposal.</param>
public sealed record NewDraftRequest(string FwDataPath, string ProductVersion, string DraftName, string? Label);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="Target">The exact canonical identity of the existing object.</param>
/// <param name="Ws">The project writing system tag for this value.</param>
/// <param name="Text">The authored Unicode text.</param>
/// <param name="DependsOn">Explicit operation dependencies; array position implies no dependency.</param>
public sealed record AddSetGlossRequest(
    string FwDataPath,
    string ProductVersion,
    string DraftName,
    string Target,
    string Ws,
    string Text,
    IReadOnlyList<string>? DependsOn = null);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="Target">The exact canonical identity of the existing object.</param>
public sealed record AddDeleteLexemeFormRequest(
    string FwDataPath, string ProductVersion, string DraftName, string Target);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeAuthorLexemeFormRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeAuthorFeatureStructureRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeAuthorFeatureValueRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeAuthorPhonemeRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeAuthorNaturalClassRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeEditNaturalClassRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeRelinkNaturalClassRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeAuthorEnvironmentRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeAuthorPhonologicalRuleRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeAuthorAffixSlotRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeAuthorAffixTemplateRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeRetireAllomorphRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeRetireRedundantZeroAffixRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeEditAdhocProhibitionRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeEditAffixSlotRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeEditAffixTemplateRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeEditInflectionalAffixRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeEditAllomorphConditionRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeOrderAllomorphsRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeRecordParsimonyDispositionRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="Target">The exact canonical identity of the existing object.</param>
/// <param name="Ws">The project writing system tag for this value.</param>
/// <param name="Text">The authored Unicode text.</param>
/// <param name="CorpusId">The explicitly authored corpus id value.</param>
/// <param name="DocumentId">The explicitly authored document id value.</param>
public sealed record PromoteGlossRequest(
    string FwDataPath,
    string ProductVersion,
    string DraftName,
    string Target,
    string Ws,
    string Text,
    string CorpusId,
    string? DocumentId = null);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="Text">The authored Unicode text.</param>
public sealed record LabelRequest(string FwDataPath, string ProductVersion, string DraftName, string Text);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="Text">The authored Unicode text.</param>
public sealed record CommentRequest(string FwDataPath, string ProductVersion, string DraftName, string Text);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="ExpectedRevision">The required Draft revision for this write.</param>
public sealed record FinalizeRequest(
    string FwDataPath, string ProductVersion, string DraftName, string? ExpectedRevision = null);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
public sealed record DiscardDraftRequest(string FwDataPath, string ProductVersion, string DraftName);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="ProposalId">The exact canonical Proposal identity.</param>
public sealed record ReopenRequest(string FwDataPath, string ProductVersion, string DraftName, string ProposalId);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="SourceProposalId">The explicitly authored source proposal id value.</param>
/// <param name="NewDraftName">The explicitly authored new draft name value.</param>
public sealed record DuplicateRequest(
    string FwDataPath, string ProductVersion, string SourceProposalId, string NewDraftName);

/// <summary>One group <see cref="SplitRequest"/> sends its share of the source Proposal's operations to.</summary>
public sealed record SplitGroup(string DraftName, IReadOnlyList<string> OperationIds);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="OperationIds">The exact operation identities to remove from the Draft.</param>
/// <param name="Force">Whether to override the requested command constraint.</param>
public sealed record RemoveOperationsRequest(
    string FwDataPath,
    string ProductVersion,
    string DraftName,
    IReadOnlyList<string> OperationIds,
    bool Force);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="SourceProposalId">The explicitly authored source proposal id value.</param>
/// <param name="Groups">The explicitly authored groups value.</param>
/// <param name="Force">Whether to override the requested command constraint.</param>
public sealed record SplitRequest(
    string FwDataPath,
    string ProductVersion,
    string SourceProposalId,
    IReadOnlyList<SplitGroup> Groups,
    bool Force);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="ProposalId">The exact canonical Proposal identity.</param>
public sealed record DeferRequest(string FwDataPath, string ProductVersion, string ProposalId);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="ProposalId">The exact canonical Proposal identity.</param>
public sealed record RejectRequest(string FwDataPath, string ProductVersion, string ProposalId);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="ProposalId">The exact canonical Proposal identity.</param>
/// <param name="SupersededByProposalId">The explicitly authored superseded by proposal id value.</param>
public sealed record SupersedeRequest(
    string FwDataPath, string ProductVersion, string ProposalId, string SupersededByProposalId);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
public sealed record ListProposalsRequest(string FwDataPath, string ProductVersion);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="ProposalId">The exact canonical Proposal identity.</param>
public sealed record ShowProposalRequest(string FwDataPath, string ProductVersion, string ProposalId);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="ProposalId">The exact canonical Proposal identity.</param>
/// <param name="User">The explicitly authored user value.</param>
/// <param name="Force">Whether to override the requested command constraint.</param>
public sealed record ApplyRequest(
    string FwDataPath, string ProductVersion, string ProposalId, string User, bool Force = false);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="ProposalId">The exact canonical Proposal identity.</param>
public sealed record PreflightRequest(string FwDataPath, string ProductVersion, string ProposalId);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
public sealed record LogRequest(string FwDataPath);
