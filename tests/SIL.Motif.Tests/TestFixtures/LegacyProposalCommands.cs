using System.Collections.Generic;
using SIL.Motif.Cli.Rendering;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// Renders <see cref="ProposalCommands"/>' typed outcomes back into the pre-typed-outcome
/// <see cref="CommandResult"/> shape, through the same <see cref="ProposalCommandRenderer"/> the CLI
/// itself uses. Integration tests written against the old rendered-text surface call through here
/// rather than asserting on <see cref="Contract.Commands.CommandOutcome{T}"/> directly, so they keep
/// exercising the same end-to-end call sequence a real invocation makes.
/// </summary>
internal static class LegacyProposalCommands
{
    public static CommandResult Open(string fwDataPath, UsageLog? usage = null) =>
        ProposalCommandRenderer.Render(ProposalCommands.Open(new OpenRequest(fwDataPath), usage), asJson: false);

    public static CommandResult OpenJson(string fwDataPath, UsageLog? usage = null) =>
        ProposalCommandRenderer.Render(ProposalCommands.Open(new OpenRequest(fwDataPath), usage), asJson: true);

    public static CommandResult Analyses(string fwDataPath, UsageLog? usage = null) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Analyses(new ManualAnalysesRequest(fwDataPath), usage), asJson: false);

    public static CommandResult AnalysesJson(string fwDataPath, UsageLog? usage = null) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Analyses(new ManualAnalysesRequest(fwDataPath), usage), asJson: true);

    public static CommandResult Analyses(
        string fwDataPath, string productVersion, string assessmentId, string currentSelectionSha256,
        string currentGrammarSourceSha256, UsageLog? usage = null) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Analyses(
                new AssessmentAnalysesRequest(
                    fwDataPath, productVersion, assessmentId, currentSelectionSha256, currentGrammarSourceSha256),
                usage),
            asJson: false);

    public static CommandResult AnalysesJson(
        string fwDataPath, string productVersion, string assessmentId, string currentSelectionSha256,
        string currentGrammarSourceSha256, UsageLog? usage = null) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Analyses(
                new AssessmentAnalysesRequest(
                    fwDataPath, productVersion, assessmentId, currentSelectionSha256, currentGrammarSourceSha256),
                usage),
            asJson: true);

    public static CommandResult New(string fwDataPath, string productVersion, string draftName, string? label) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.New(new NewDraftRequest(fwDataPath, productVersion, draftName, label)), asJson: false);

    public static CommandResult AddSetGloss(
        string fwDataPath, string productVersion, string draftName, string target, string ws, string text,
        IReadOnlyList<string>? dependsOn = null) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.AddSetGloss(
                new AddSetGlossRequest(fwDataPath, productVersion, draftName, target, ws, text, dependsOn)),
            asJson: false);

    public static CommandResult AddDeleteLexemeForm(
        string fwDataPath, string productVersion, string draftName, string target) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.AddDeleteLexemeForm(
                new AddDeleteLexemeFormRequest(fwDataPath, productVersion, draftName, target)),
            asJson: false);

    public static CommandResult ComposeAuthorLexemeForm(
        string fwDataPath, string productVersion, string draftName, string intentJson) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.ComposeAuthorLexemeForm(
                new ComposeAuthorLexemeFormRequest(fwDataPath, productVersion, draftName, intentJson)),
            asJson: false);

    public static CommandResult ComposeAuthorFeatureStructure(
        string fwDataPath, string productVersion, string draftName, string intentJson) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.ComposeAuthorFeatureStructure(
                new ComposeAuthorFeatureStructureRequest(fwDataPath, productVersion, draftName, intentJson)),
            asJson: false);

    public static CommandResult PromoteGloss(
        string fwDataPath, string productVersion, string draftName, string target, string ws, string text,
        string corpusId, string? documentId = null) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.PromoteGloss(new PromoteGlossRequest(
                fwDataPath, productVersion, draftName, target, ws, text, corpusId, documentId)),
            asJson: false);

    public static CommandResult Label(string fwDataPath, string productVersion, string draftName, string text) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Label(new LabelRequest(fwDataPath, productVersion, draftName, text)), asJson: false);

    public static CommandResult Comment(string fwDataPath, string productVersion, string draftName, string text) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Comment(new CommentRequest(fwDataPath, productVersion, draftName, text)),
            asJson: false);

    public static CommandResult Finalize(string fwDataPath, string productVersion, string draftName) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Finalize(new FinalizeRequest(fwDataPath, productVersion, draftName)), asJson: false);

    public static CommandResult DiscardDraft(string fwDataPath, string productVersion, string draftName) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.DiscardDraft(new DiscardDraftRequest(fwDataPath, productVersion, draftName)),
            asJson: false);

    public static CommandResult Reopen(
        string fwDataPath, string productVersion, string draftName, string proposalId) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Reopen(new ReopenRequest(fwDataPath, productVersion, draftName, proposalId)),
            asJson: false);

    public static CommandResult Duplicate(
        string fwDataPath, string productVersion, string sourceProposalId, string newDraftName) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Duplicate(
                new DuplicateRequest(fwDataPath, productVersion, sourceProposalId, newDraftName)),
            asJson: false);

    public static CommandResult RemoveOperations(
        string fwDataPath, string productVersion, string draftName, IReadOnlyList<string> operationIds,
        bool force) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.RemoveOperations(
                new RemoveOperationsRequest(fwDataPath, productVersion, draftName, operationIds, force)),
            asJson: false);

    public static CommandResult Split(
        string fwDataPath, string productVersion, string sourceProposalId, IReadOnlyList<SplitGroup> groups,
        bool force) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Split(new SplitRequest(fwDataPath, productVersion, sourceProposalId, groups, force)),
            asJson: false);

    public static CommandResult Defer(string fwDataPath, string productVersion, string proposalId) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Defer(new DeferRequest(fwDataPath, productVersion, proposalId)), asJson: false);

    public static CommandResult Reject(string fwDataPath, string productVersion, string proposalId) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Reject(new RejectRequest(fwDataPath, productVersion, proposalId)), asJson: false);

    public static CommandResult Supersede(
        string fwDataPath, string productVersion, string proposalId, string supersededByProposalId) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Supersede(
                new SupersedeRequest(fwDataPath, productVersion, proposalId, supersededByProposalId)),
            asJson: false);

    public static CommandResult List(string fwDataPath, string productVersion, UsageLog? usage = null) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.List(new ListProposalsRequest(fwDataPath, productVersion), usage), asJson: false);

    public static CommandResult ListJson(string fwDataPath, string productVersion, UsageLog? usage = null) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.List(new ListProposalsRequest(fwDataPath, productVersion), usage), asJson: true);

    public static CommandResult Show(
        string fwDataPath, string productVersion, string proposalId, UsageLog? usage = null) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Show(new ShowProposalRequest(fwDataPath, productVersion, proposalId), usage),
            asJson: false);

    public static CommandResult ShowJson(
        string fwDataPath, string productVersion, string proposalId, UsageLog? usage = null) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Show(new ShowProposalRequest(fwDataPath, productVersion, proposalId), usage),
            asJson: true);

    public static CommandResult Apply(
        string fwDataPath, string productVersion, string proposalId, string user, bool force = false,
        UsageLog? usage = null) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Apply(new ApplyRequest(fwDataPath, productVersion, proposalId, user, force), usage),
            asJson: false);

    public static CommandResult ApplyJson(
        string fwDataPath, string productVersion, string proposalId, string user, bool force = false,
        UsageLog? usage = null) =>
        ProposalCommandRenderer.Render(
            ProposalCommands.Apply(new ApplyRequest(fwDataPath, productVersion, proposalId, user, force), usage),
            asJson: true);

    public static CommandResult Log(string fwDataPath, UsageLog? usage = null) =>
        ProposalCommandRenderer.Render(ProposalCommands.Log(new LogRequest(fwDataPath), usage), asJson: false);

    public static CommandResult LogJson(string fwDataPath, UsageLog? usage = null) =>
        ProposalCommandRenderer.Render(ProposalCommands.Log(new LogRequest(fwDataPath), usage), asJson: true);
}
