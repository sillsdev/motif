using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;

namespace SIL.Motif.App.Services;

public partial interface ICommandClient
{
    /// <summary>Reads what Motif has done with a project: Baselines captured, Assessments run, Handoffs written.</summary>
    Task<CommandOutcome<ProjectHistoryResponse>> GetProjectHistoryAsync(
        ProjectHistoryRequest request, CancellationToken cancellationToken);

    /// <summary>Checks the current Baseline's grammar with no Text or word involved.</summary>
    Task<CommandOutcome<GrammarCheckResponse>> CheckGrammarAsync(
        GrammarCheckRequest request, CancellationToken cancellationToken);

    /// <summary>Reads the chosen Texts' words, where each occurs, and the analyses the project holds for them.</summary>
    Task<CommandOutcome<TextWordsResponse>> ListTextWordsAsync(
        TextWordsRequest request, CancellationToken cancellationToken);

    /// <summary>Traces one word against the current Baseline's grammar.</summary>
    Task<CommandOutcome<WordTraceResponse>> TraceWordAsync(
        WordTraceRequest request, CancellationToken cancellationToken);
}
