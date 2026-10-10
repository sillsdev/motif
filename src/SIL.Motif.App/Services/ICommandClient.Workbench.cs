using SIL.Motif.Commands.Queries;
using SIL.Motif.Commands.Requests;
using SIL.Motif.Commands.SelectionReading;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Parsimony;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public partial interface ICommandClient
{
    /// <summary>Reads every stored analysis of one exact Baseline word, independently of any Assessment.</summary>
    Task<CommandOutcome<WordContextResponse>> ReadWordContextAsync(
        WordContextRequest request, CancellationToken cancellationToken);

    /// <summary>Reads the stored project Overview without opening a LibLCM project cache.</summary>
    Task<CommandOutcome<OverviewResponse>> OverviewAsync(
        OverviewRequest request, CancellationToken cancellationToken);

    /// <summary>Reads stored Assessment timings without opening a LibLCM project cache.</summary>
    Task<CommandOutcome<TimingResponse>> TimingAsync(
        TimingRequest request, CancellationToken cancellationToken);

    /// <summary>Reads the stored Baseline, default Selection, matching Assessment, and current freshness.</summary>
    Task<CommandOutcome<CurrentEvidenceSnapshot>> ReadCurrentEvidenceAsync(
        string projectPath, CancellationToken cancellationToken);

    /// <summary>Reads what Motif has done with a project: Baselines captured, Assessments run, Handoffs written.</summary>
    Task<CommandOutcome<ProjectHistoryResponse>> GetProjectHistoryAsync(
        ProjectHistoryRequest request, CancellationToken cancellationToken);

    /// <summary>Checks the current Baseline's grammar with no Text or word involved.</summary>
    Task<CommandOutcome<GrammarCheckResponse>> CheckGrammarAsync(
        GrammarCheckRequest request, CancellationToken cancellationToken);

    /// <summary>Reads the grammar check last stored for the current Baseline; never runs the parser.</summary>
    Task<CommandOutcome<StoredGrammarCheckResponse>> ReadStoredGrammarCheckAsync(
        GrammarCheckRequest request, CancellationToken cancellationToken);

    /// <summary>Opens the workspace-owned Selection session; the caller disposes it when replacing the Selection.</summary>
    Task<CommandOutcome<SelectionReader>> OpenSelectionReaderAsync(
        OpenSelectionReaderRequest request, CancellationToken cancellationToken);

    /// <summary>Traces one word against the current Baseline's grammar, with optional progress while waiting.</summary>
    Task<CommandOutcome<WordTraceResponse>> TraceWordAsync(
        WordTraceRequest request, CancellationToken cancellationToken, IProgress<AssessmentProgress>? progress = null);

    /// <summary>
    /// Reads what Motif knows about one inspector subject, each section from its own source: the Baseline's facts,
    /// the stored Parse all words' words and times, and the stored grammar check's findings.
    /// </summary>
    Task<CommandOutcome<InspectResponse>> InspectAsync(InspectRequest request, CancellationToken cancellationToken);

    /// <summary>Reads the staged allomorph-retirement Draft's Dry Run and stored review evidence.</summary>
    Task<CommandOutcome<RetirementReviewQueryResponse>> ReadRetirementReviewAsync(
        ReadRetirementReviewRequest request, CancellationToken cancellationToken);

    /// <summary>Names the newest stored Parsimony Report and its bundle, reading the store and never a parser.</summary>
    Task<CommandOutcome<ParsimonyLatestReportResponse>> ReadLatestParsimonyReportAsync(
        ReadLatestParsimonyReportRequest request, CancellationToken cancellationToken);

    /// <summary>Reads one stored Parsimony Report by its id, without starting a parser run.</summary>
    Task<CommandOutcome<ParsimonyReportResponse>> ReadParsimonyReportAsync(
        ShowParsimonyReportRequest request, CancellationToken cancellationToken);

    /// <summary>Reads one fixed Parsimony view, such as the Active or Suppressed findings, from the stored bundle.</summary>
    Task<CommandOutcome<ParsimonyNamedViewResponse>> ReadParsimonyViewAsync(
        ReadParsimonyViewRequest request, CancellationToken cancellationToken);

    /// <summary>Lists the saved Notebook record types a Parsimony decision can be recorded under.</summary>
    Task<CommandOutcome<NotebookRecordTypesResponse>> ListNotebookRecordTypesAsync(
        ListNotebookRecordTypesRequest request, CancellationToken cancellationToken);

    /// <summary>Stages a keep, ask, or defer for one finding in the pending changes; nothing is written.</summary>
    Task<CommandOutcome<ComposedOperationsResponse>> RecordParsimonyDispositionAsync(
        RecordParsimonyDispositionFromFindingRequest request, CancellationToken cancellationToken);

    /// <summary>Stages the withdrawal of a saved decision so its finding returns to Active once applied.</summary>
    Task<CommandOutcome<ComposedOperationsResponse>> RetractParsimonyDispositionAsync(
        RetractParsimonyDispositionRequest request, CancellationToken cancellationToken);
}
