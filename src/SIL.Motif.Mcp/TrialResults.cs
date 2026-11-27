using SIL.Motif.Commands;
using SIL.Motif.Commands.Assess;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Worker;
using SIL.Motif.Worker.Baselines;

namespace SIL.Motif.Mcp;

internal static class TrialResults
{
    internal static CommandOutcome<AssessCommandResponse> Assess(ServerContext context, IReadOnlyList<string> words,
        CancellationToken cancellation, bool noBackground = false) => TrialBaselines.Assess(context.ProjectPath, words,
            RunnerOptions.ResolveRoot(), context.ParserPath, cancellation, noBackground);
}
