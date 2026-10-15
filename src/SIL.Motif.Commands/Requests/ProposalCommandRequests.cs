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

/// <summary>Requests an empty feature structure on an existing or earlier-staged stem or inflectional affix MSA.</summary>
/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">
/// Closed <c>{msa}</c> input naming the exact canonical MSA identity. Its MsFeatures or InflFeats owning slot
/// must be unoccupied; a present structure is refused even with no selections. Inflectional affix features
/// are word requirements. Use the returned structure identity for selections.
/// </param>
public sealed record ComposeAuthorFeatureStructureRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <summary>Requests a closed feature selection on an existing or earlier-staged feature structure.</summary>
/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">
/// Closed <c>{featStruc, feature, value?}</c> input using exact canonical identities. Omit value when unchosen;
/// a supplied value belongs to the exact closed feature. InflFeats selections use the current morphosyntactic
/// domain and any declared structure type, and are word requirements.
/// </param>
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
public sealed record ComposeAuthorPartOfSpeechRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeAuthorLexicalEntryRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">The closed semantic intent JSON accepted by this composer.</param>
public sealed record ComposeAuthorAlternateFormRequest(
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

/// <summary>Edits the explicit default inflection class of a part of speech in a Draft.</summary>
/// <param name="FwDataPath">The saved project this Draft belongs to.</param>
/// <param name="ProductVersion">The Motif version opening the paired store.</param>
/// <param name="DraftName">The Draft to append the authored operations to.</param>
/// <param name="IntentJson">The closed default-class edit resolved against the prepared Draft.</param>
public sealed record ComposeEditPartOfSpeechInflectionClassDefaultRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <summary>Sets or clears the stem name of one stem allomorph in a Draft.</summary>
/// <param name="FwDataPath">The saved project this Draft belongs to.</param>
/// <param name="ProductVersion">The Motif version opening the paired store.</param>
/// <param name="DraftName">The Draft to append the authored operations to.</param>
/// <param name="IntentJson">The closed stem-name edit resolved against the prepared Draft.</param>
public sealed record ComposeEditStemAllomorphStemNameRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <summary>Sets or clears the inflection class of one stem analysis in a Draft.</summary>
/// <param name="FwDataPath">The saved project this Draft belongs to.</param>
/// <param name="ProductVersion">The Motif version opening the paired store.</param>
/// <param name="DraftName">The Draft to append the authored operations to.</param>
/// <param name="IntentJson">The closed class edit resolved against the prepared Draft.</param>
public sealed record ComposeEditStemInflectionClassRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <summary>Edits the exact inflection class restrictions on one ordinary affix form in a Draft.</summary>
/// <param name="FwDataPath">The saved project this Draft belongs to.</param>
/// <param name="ProductVersion">The Motif version opening the paired store.</param>
/// <param name="DraftName">The Draft to append the authored operations to.</param>
/// <param name="IntentJson">The closed class-set edit resolved against the prepared Draft.</param>
public sealed record ComposeEditAffixInflectionClassesRequest(
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

/// <summary>Requests a new Text, with the wordforms its tokens still need, staged in a Draft.</summary>
/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">
/// Closed <c>{title, ws, paragraphs:[{segments:[[token,...],...]}]}</c> input. Every token is one NFD form without
/// spaces; a token whose exact form already is a wordform in <c>ws</c> reuses it, and any other token creates one.
/// </param>
public sealed record ComposeAuthorTextRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

/// <summary>Requests one human decision about an existing analysis or wordform, staged in a Draft.</summary>
/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="DraftName">The name of the Draft being authored.</param>
/// <param name="IntentJson">
/// Closed <c>{decision, analysis}</c> input for approve, disapprove or unknown, or <c>{decision, wordform}</c> for
/// misspelled, naming the exact canonical identity.
/// </param>
public sealed record ComposeEditAnalysisDecisionRequest(
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
/// <param name="IntentsJson">
/// <c>{"intents":[{"verb":"compose-author-phoneme","intent":{...},"citation":{"source":"Grammar","locator":"§ 3"}}, ...]}</c>:
/// each entry names a composing verb, carries exactly the closed intent that verb's <c>--intent</c> takes, and may
/// include one citation beside that intent.
/// </param>
public sealed record ComposeBatchRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentsJson);

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
/// <param name="ExpectedRevision">The Draft revision the caller read, or null to discard whatever it holds.</param>
public sealed record DiscardDraftRequest(string FwDataPath, string ProductVersion, string DraftName,
    string? ExpectedRevision = null);

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
/// <param name="All">Whether closed Proposals (applied, declined, superseded, withdrawn) are listed too.</param>
/// <param name="Status">Lists only Proposals in this stored status, open or closed.</param>
public sealed record ListProposalsRequest(string FwDataPath, string ProductVersion, bool All = false,
    string? Status = null);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="ProposalId">A Draft name or the exact canonical identity of a Draft or finalized Proposal.</param>
public sealed record ShowProposalRequest(string FwDataPath, string ProductVersion, string ProposalId);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="ProposalId">The exact canonical Proposal identity.</param>
/// <param name="Force">Whether to override the requested command constraint.</param>
public sealed record ApplyRequest(
    string FwDataPath, string ProductVersion, string ProposalId, bool Force = false);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="ProposalId">The exact canonical Proposal identity.</param>
public sealed record PreflightRequest(string FwDataPath, string ProductVersion, string ProposalId);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
public sealed record LogRequest(string FwDataPath);

/// <param name="FwDataPath">The saved FieldWorks project path supplied by the caller.</param>
/// <param name="ProductVersion">The Motif product version used to open its paired store.</param>
/// <param name="UncitedOnly">List only the Applied operations that cite no source.</param>
public sealed record CitationsRequest(string FwDataPath, string ProductVersion, bool UncitedOnly = false);

/// <summary>Authors a new inflection feature in a Draft through its closed intent.</summary>
/// <param name="FwDataPath">The saved project this Draft belongs to.</param>
/// <param name="ProductVersion">The Motif version opening the paired store.</param>
/// <param name="DraftName">The Draft to append the authored operations to.</param>
/// <param name="IntentJson">The closed inflection feature input, resolved against the prepared Draft.</param>
public sealed record ComposeAuthorInflectionFeatureRequest(string FwDataPath, string ProductVersion,
    string DraftName, string IntentJson);

/// <summary>Authors a new inflection class in a Draft through its closed intent.</summary>
/// <param name="FwDataPath">The saved project this Draft belongs to.</param>
/// <param name="ProductVersion">The Motif version opening the paired store.</param>
/// <param name="DraftName">The Draft to append the authored operations to.</param>
/// <param name="IntentJson">The closed inflection class input, resolved against the prepared Draft.</param>
public sealed record ComposeAuthorInflectionClassRequest(string FwDataPath, string ProductVersion,
    string DraftName, string IntentJson);

/// <summary>Authors a new stem name in a Draft through its closed intent.</summary>
/// <param name="FwDataPath">The saved project this Draft belongs to.</param>
/// <param name="ProductVersion">The Motif version opening the paired store.</param>
/// <param name="DraftName">The Draft to append the authored operations to.</param>
/// <param name="IntentJson">The closed stem name input, resolved against the prepared Draft.</param>
public sealed record ComposeAuthorStemNameRequest(string FwDataPath, string ProductVersion,
    string DraftName, string IntentJson);
