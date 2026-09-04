using System.Collections.Generic;

namespace SIL.Motif.Commands.Requests;

public sealed record OpenRequest(string FwDataPath);

public sealed record ManualAnalysesRequest(string FwDataPath);

public sealed record AssessmentAnalysesRequest(
    string FwDataPath,
    string ProductVersion,
    string AssessmentId,
    string CurrentSelectionSha256,
    string CurrentGrammarSourceSha256);

public sealed record NewDraftRequest(string FwDataPath, string ProductVersion, string DraftName, string? Label);

public sealed record AddSetGlossRequest(
    string FwDataPath,
    string ProductVersion,
    string DraftName,
    string Target,
    string Ws,
    string Text,
    IReadOnlyList<string>? DependsOn = null);

public sealed record AddDeleteLexemeFormRequest(
    string FwDataPath, string ProductVersion, string DraftName, string Target);

public sealed record ComposeAuthorLexemeFormRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

public sealed record ComposeAuthorFeatureStructureRequest(
    string FwDataPath, string ProductVersion, string DraftName, string IntentJson);

public sealed record PromoteGlossRequest(
    string FwDataPath,
    string ProductVersion,
    string DraftName,
    string Target,
    string Ws,
    string Text,
    string CorpusId,
    string? DocumentId = null);

public sealed record LabelRequest(string FwDataPath, string ProductVersion, string DraftName, string Text);

public sealed record CommentRequest(string FwDataPath, string ProductVersion, string DraftName, string Text);

public sealed record FinalizeRequest(string FwDataPath, string ProductVersion, string DraftName);

public sealed record DiscardDraftRequest(string FwDataPath, string ProductVersion, string DraftName);

public sealed record ReopenRequest(string FwDataPath, string ProductVersion, string DraftName, string ProposalId);

public sealed record DuplicateRequest(
    string FwDataPath, string ProductVersion, string SourceProposalId, string NewDraftName);

/// <summary>One group <see cref="SplitRequest"/> sends its share of the source Proposal's operations to.</summary>
public sealed record SplitGroup(string DraftName, IReadOnlyList<string> OperationIds);

public sealed record RemoveOperationsRequest(
    string FwDataPath,
    string ProductVersion,
    string DraftName,
    IReadOnlyList<string> OperationIds,
    bool Force);

public sealed record SplitRequest(
    string FwDataPath,
    string ProductVersion,
    string SourceProposalId,
    IReadOnlyList<SplitGroup> Groups,
    bool Force);

public sealed record DeferRequest(string FwDataPath, string ProductVersion, string ProposalId);

public sealed record RejectRequest(string FwDataPath, string ProductVersion, string ProposalId);

public sealed record SupersedeRequest(
    string FwDataPath, string ProductVersion, string ProposalId, string SupersededByProposalId);

public sealed record ListProposalsRequest(string FwDataPath, string ProductVersion);

public sealed record ShowProposalRequest(string FwDataPath, string ProductVersion, string ProposalId);

public sealed record ApplyRequest(
    string FwDataPath, string ProductVersion, string ProposalId, string User, bool Force = false);

public sealed record LogRequest(string FwDataPath);
