using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using SIL.Motif.Host;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.Commands;

/// <summary>
/// Records the outer explicit CLI or window action. Nested scopes share the outer entry, so a composed workflow
/// stays one observation. Argument shapes contain parameter names and kinds, never supplied values. The window
/// writes its actions to the daily call log under the surface <c>window</c>.
/// </summary>
public sealed class UsageRecorder
{
    private readonly IUsageLogSink _sink;
    private readonly AsyncLocal<ScopeFrame?> _current = new();

    public UsageRecorder(IUsageLogSink sink) =>
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));

    /// <summary>A recorder that writes each action to <paramref name="calls"/> as a <c>window</c> call.</summary>
    public static UsageRecorder ForCallLog(CallLog calls)
    {
        ArgumentNullException.ThrowIfNull(calls);
        return new UsageRecorder(new CallLogSink(calls));
    }

    /// <summary>Begins an explicit user action; dispose the returned scope after its outcome is known.</summary>
    public IDisposable BeginAction(string command, IReadOnlyList<string> argumentShape)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(argumentShape);

        var parent = _current.Value;
        if (parent is null)
            _sink.Append(new UsageLogEntry(DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ"), command, argumentShape));

        var frame = new ScopeFrame(parent);
        _current.Value = frame;
        return new ActionScope(this, frame);
    }

    // A window action has no outcome to report: its line carries the command and argument names only.
    private sealed class CallLogSink(CallLog calls) : IUsageLogSink
    {
        public void Append(UsageLogEntry entry) =>
            calls.Record(new CallRecord("window", entry.Command, null,
                entry.ArgumentShape.ToDictionary(shape => shape, _ => string.Empty), null, null, false, null, 0, null, null,
                Outcome: "unknown"));
    }

    private sealed class ScopeFrame(ScopeFrame? parent)
    {
        public ScopeFrame? Parent { get; } = parent;
    }

    private sealed class ActionScope(UsageRecorder recorder, ScopeFrame frame) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            if (!ReferenceEquals(recorder._current.Value, frame))
                throw new InvalidOperationException("Usage action scopes must be disposed in reverse order.");
            recorder._current.Value = frame.Parent;
        }
    }
}
