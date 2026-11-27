using SIL.Motif.App.GuideQueries;
using SIL.Motif.Commands;
using SIL.Motif.Commands.Queries;
using SIL.Motif.Contract.Requests;
using SIL.Motif.Contract.Commands;
using SIL.Motif.Contract.Query;
using SIL.Motif.Contract.Responses;
using SIL.Motif.Host;

namespace SIL.Motif.App.Services;

public sealed partial class CommandClient
{
    // One loaded project for every evidence block this window runs; it reloads when the project file changes.
    private readonly ProjectSession _guideSession = new(GuideSessionIdle);

    internal static readonly TimeSpan GuideSessionIdle = TimeSpan.FromMinutes(5);

    public void ReleaseGuideSession() =>
        _ = _guideSession.ReleaseInBackground(exception => System.Diagnostics.Trace.TraceWarning("Releasing the Guide project session failed: " + exception.Message));

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

    public Task<CommandOutcome<GdrRefreshTablesResponse>> RefreshGuideTablesAsync(GdrRefreshTablesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return OneAtATime(() => GdrTableCommands.Refresh(request, _options.ParserPath, cancellationToken, session: _guideSession), cancellationToken);
    }

    public Task<GuideEvidenceResult> RunGuideEvidenceAsync(string projectPath, string line, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(projectPath);
        ArgumentNullException.ThrowIfNull(line);
        var epoch = _guideSession.Epoch;
        return Task.Run(() =>
        {
            GuideEvidenceResult Left() => new(GuideEvidenceStatus.Refused, null, MotifAppStrings.GuideQueryProjectChanged,
                RefusalCodes.GuideQueryRunInvalid, null, 0, new Dictionary<string, double>());
            // A block queued before the project was left must not load it again, or keep it loaded afterwards.
            if (_guideSession.Epoch != epoch) return Left();
            var result = GuideEvidenceRunner.Evaluate(line, GuideEvidenceRun.BaseDirectory(projectPath), _options.ParserPath, cancellationToken, projectPath, session: _guideSession);
            if (_guideSession.Epoch == epoch) return result;
            _ = _guideSession.ReleaseInBackground();
            return Left();
        }, CancellationToken.None);
    }
}
