using System.Diagnostics;
using SIL.Motif.Contract.Responses;

namespace SIL.Motif.Worker.Jobs;

/// <summary>Collects the wall time of a Trial's phases so its receipt says where the time went.</summary>
internal sealed class TrialPhaseLog
{
    private readonly Dictionary<string, (double Seconds, string? Note)> _phases = new(StringComparer.Ordinal);

    /// <summary>Adds the CLI's phases the Trial was queued with.</summary>
    internal void AddRange(IEnumerable<TrialPhase>? phases)
    {
        foreach (var phase in phases ?? []) Add(phase.Name, phase.Seconds, phase.Note);
    }

    /// <summary>Adds seconds to a phase; a phase that happens in several partitions sums.</summary>
    internal void Add(string name, double seconds, string? note = null)
    {
        _phases.TryGetValue(name, out var current);
        _phases[name] = (current.Seconds + Math.Max(0, seconds), note ?? current.Note);
    }

    internal static double Since(long startTimestamp) => Stopwatch.GetElapsedTime(startTimestamp).TotalSeconds;

    /// <summary>Every phase in its stable order; one that did not run reads 0 and is noted as such.</summary>
    internal IReadOnlyList<TrialPhase> Snapshot() => TrialPhaseNames.All.Select(name =>
        _phases.TryGetValue(name, out var phase)
            ? new TrialPhase(name, Math.Round(phase.Seconds, 3)) { Note = phase.Note }
            : new TrialPhase(name, 0) { Note = TrialPhaseNames.NotRun }).ToArray();
}
