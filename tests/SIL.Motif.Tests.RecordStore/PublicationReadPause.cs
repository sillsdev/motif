using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Store = SIL.Motif.RecordStore.RecordStore;

namespace SIL.Motif.Tests.RecordStore;

internal sealed class PublicationReadPause : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>, IDisposable
{
    private readonly string path;
    private readonly string table;
    private readonly IDisposable all;
    private readonly List<IDisposable> subscriptions = [];
    private readonly TaskCompletionSource paused = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> seen = new();
    public string Detail => string.Join(" | ", seen);
    private int armed;
    private int used;
    private bool disposed;
    public Task Paused => paused.Task;
    public PublicationReadPause(string path, string table = "Commits")
    {
        this.path = Store.ResolvePath(path);
        this.table = table;
        all = DiagnosticListener.AllListeners.Subscribe(this);
    }
    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener listener)
    {
        if (listener.Name != "Microsoft.EntityFrameworkCore") return;
        lock (subscriptions)
            if (!disposed) subscriptions.Add(listener.Subscribe(this, name => name == RelationalEventId.CommandExecuted.Name));
    }
    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> value)
    {
        if (value.Value is not CommandExecutedEventData data || Volatile.Read(ref armed) == 0) return;
        if (data.Command.Connection?.DataSource != path) return;
        seen.Enqueue(data.Command.CommandText);
        if (!data.Command.CommandText.Contains($"FROM \"{table}\"", StringComparison.Ordinal) ||
            Interlocked.Exchange(ref used, 1) != 0) return;
        paused.TrySetResult();
        released.Task.GetAwaiter().GetResult();
    }
    public void Arm() => Volatile.Write(ref armed, 1);
    public void Release() => released.TrySetResult();
    public void OnCompleted() { }
    public void OnError(Exception error) { }
    public void Dispose()
    {
        Release();
        lock (subscriptions)
        {
            disposed = true;
            foreach (var subscription in subscriptions) subscription.Dispose();
            subscriptions.Clear();
        }
        all.Dispose();
    }
}
