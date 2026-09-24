using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.App.Services;

public partial interface ICommandClient
{
    /// <summary>Reads the stored project Overview without opening a LibLCM project cache.</summary>
    Task<CommandOutcome<OverviewResponse>> OverviewAsync(
        OverviewRequest request, CancellationToken cancellationToken);

    /// <summary>Reads stored Assessment timings without opening a LibLCM project cache.</summary>
    Task<CommandOutcome<TimingResponse>> TimingAsync(
        TimingRequest request, CancellationToken cancellationToken);

    /// <summary>Reads what Motif has done with a project: Baselines captured, Assessments run, Handoffs written.</summary>
    Task<CommandOutcome<ProjectHistoryResponse>> GetProjectHistoryAsync(
        ProjectHistoryRequest request, CancellationToken cancellationToken);

    /// <summary>Checks the current Baseline's grammar with no Text or word involved.</summary>
    Task<CommandOutcome<GrammarCheckResponse>> CheckGrammarAsync(
        GrammarCheckRequest request, CancellationToken cancellationToken);

    /// <summary>Reads the grammar check last stored for the current Baseline; never runs the parser.</summary>
    Task<CommandOutcome<StoredGrammarCheckResponse>> ReadStoredGrammarCheckAsync(
        GrammarCheckRequest request, CancellationToken cancellationToken);

    /// <summary>Reads the chosen Texts' words, where each occurs, and the analyses the project holds for them.</summary>
    Task<CommandOutcome<TextWordsResponse>> ListTextWordsAsync(
        TextWordsRequest request, CancellationToken cancellationToken);

    /// <summary>Traces one word against the current Baseline's grammar.</summary>
    Task<CommandOutcome<WordTraceResponse>> TraceWordAsync(
        WordTraceRequest request, CancellationToken cancellationToken);

    /// <summary>Reads stored Assessment timing for the requested words and optional rule.</summary>
    Task<CommandOutcome<TimingResponse>> TimingAsync(
        TimingRequest request, CancellationToken cancellationToken);
}
