using SIL.Motif.App.GuideQueries;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    public Task<CommandOutcome<FormsQueryResponse>> RunGuideQueryAsync(GuideQueryRunRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() =>
        {
            if (cancellationToken.IsCancellationRequested) return WaitCancelled<FormsQueryResponse>();
            if (!QueryRunLine.TryParseRun(request.Line, out var line, out var refusal)) return CommandOutcome<FormsQueryResponse>.Refused(refusal);
            if (line.Query is not { } query) return CommandOutcome<FormsQueryResponse>.Refused(
                new Refusal(RefusalCodes.GuideQueryRunInvalid, FailureReason.InvalidArgument, MotifAppStrings.GuideQueryGroupLine));
            return FormsQueryCommand.Run(new FormsQueryRequest(request.ProjectPath, MotifProductVersion.CurrentText, query.Record),
                parserPath: _options.ParserPath);
        }, CancellationToken.None);
    }

    public Task<GuideEvidenceResult> RunGuideEvidenceAsync(string projectPath, string line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(line);
        return Task.Run(() => GuideEvidenceRunner.Evaluate(line, GuideEvidenceRun.BaseDirectory(projectPath), _options.ParserPath, cancellationToken, projectPath),
            CancellationToken.None);
    }
}
