using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Data.Sqlite;
using SIL.Motif.Host.Store;
using SIL.Motif.Projection.Usage;

namespace SIL.Motif.Commands;

/// <summary>
/// Records the outer explicit CLI or window action. Nested scopes share the outer entry, so a composed workflow
/// stays one observation. Argument shapes contain parameter names and kinds, never supplied values.
/// </summary>
public sealed class UsageRecorder
{
    private readonly IUsageLogSink _sink;
    private readonly AsyncLocal<ScopeFrame?> _current = new();

    public UsageRecorder(IUsageLogSink sink) =>
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));

    public static UsageRecorder ForMachineRoot(string workerRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerRoot);
        return new UsageRecorder(new MachineUsageLogSink(workerRoot));
    }

    /// <summary>Begins an explicit user action; dispose the returned scope after its outcome is known.</summary>
    public IDisposable BeginAction(string command, IReadOnlyList<string> argumentShape)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        ArgumentNullException.ThrowIfNull(argumentShape);

        var parent = _current.Value;
        if (parent is null)
            _sink.Append(UsageLog.CreateEntry(command, argumentShape));

        var frame = new ScopeFrame(parent);
        _current.Value = frame;
        return new ActionScope(this, frame);
    }

    private sealed class MachineUsageLogSink(string workerRoot) : IUsageLogSink
    {
        public void Append(UsageLogEntry entry)
        {
            try
            {
                using var machine = MachineDatabase.Open(workerRoot);
                new MachineUsageLog(machine).Append(entry);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                               ArgumentException or InvalidOperationException or SqliteException or MotifStoreVersionException)
            {
            }
        }
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
