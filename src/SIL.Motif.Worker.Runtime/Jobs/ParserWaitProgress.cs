using SIL.Motif.Contract.Jobs;

namespace SIL.Motif.Worker.Jobs;

/// <summary>Temporarily publishes a parser-capacity wait over the progress this invocation displaced.</summary>
/// <remarks>Pinned by <c>ParserWaitRestoresStagePayloadAndKeepsItsStepCounts</c> and
/// <c>IndependentHandlesCannotRestoreAcrossAnIdenticalWaitPayloadAba</c>.</remarks>
internal sealed class ParserWaitProgress
{
    private readonly object _gate = new();
    private readonly ClaimedJob _claim;
    private readonly JobStageProgress _phase;
    private readonly long _generation;
    private bool _waitReported;
    private bool _waitEnded;
    private (long Version, string? PreviousProgressJson, string WaitProgressJson)? _published;

    /// <summary>Captures this parser invocation as the handle's current progress scope.</summary>
    public ParserWaitProgress(ClaimedJob claim, JobStageProgress phase)
    {
        _claim = claim ?? throw new ArgumentNullException(nameof(claim));
        _phase = phase ?? throw new ArgumentNullException(nameof(phase));
        _generation = claim.BeginProgressScope();
    }

    /// <summary>Publishes a wait once and restores its exact displaced payload once when that wait ends.</summary>
    public void Observe(bool waiting)
    {
        lock (_gate)
        {
            if (waiting)
            {
                if (_waitReported || _waitEnded) return;
                _waitReported = true;
                _published = _claim.TryPublishParserWait(_generation, _phase);
                return;
            }

            if (!_waitReported || _waitEnded) return;
            _waitEnded = true;
            if (_published is { } published)
                _ = _claim.TryRestoreParserWait(_generation, published.Version,
                    published.WaitProgressJson, published.PreviousProgressJson);
        }
    }
}
