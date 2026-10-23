using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;

namespace SIL.Motif.App.Services;

/// <summary>
/// The "Motif.Ui" trace: <see cref="ActivitySource"/> spans around a page or view switch, from the command that
/// asked for it to the first frame that shows it. It is off unless something listens, so the ordinary app pays one
/// property read per call site; <c>MOTIF_TRACE=1</c> (with <c>MOTIF_TRACE_FILE</c> for the path) turns it on.
/// </summary>
public static class MotifTrace
{
    /// <summary>The name a listener subscribes to.</summary>
    public const string SourceName = "Motif.Ui";

    /// <summary>Names the environment variable that turns the trace on when it is <c>1</c>.</summary>
    public const string EnableVariable = "MOTIF_TRACE";

    /// <summary>Names the environment variable holding the JSON-lines file the trace writes to.</summary>
    public const string FileVariable = "MOTIF_TRACE_FILE";

    private static readonly ActivitySource Source = new(SourceName);
    private static Activity? _switch;
    private static int _switchSequence;

    private static readonly Dictionary<string, int> Counts = new();

    /// <summary>Counts one <paramref name="what"/> against the open switch; the totals become <c>n.*</c> tags on its root span.</summary>
    public static void Count(string what)
    {
        if (_switch is null) return;
        Counts[what] = Counts.GetValueOrDefault(what) + 1;
    }

    /// <summary>Whether anything is listening, so a call site can skip building tag values.</summary>
    public static bool IsEnabled => Source.HasListeners();

    /// <summary>A label stamped on every span while set, such as <c>warm 1280x800@100</c>; a harness sets it per run.</summary>
    public static string? RunLabel { get; set; }

    /// <summary>The switch span now open, or <see langword="null"/> when none is in flight or the trace is off.</summary>
    public static Activity? CurrentSwitch => _switch;

    /// <summary>Starts the file sink when <c>MOTIF_TRACE=1</c>; the returned handle flushes it, and is <see langword="null"/> when off.</summary>
    public static IDisposable? StartFromEnvironment()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1") return null;
        var path = Environment.GetEnvironmentVariable(FileVariable);
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Path.GetTempPath(), $"motif-trace-{Environment.ProcessId}.jsonl");
        return StartListening(path);
    }

    /// <summary>Listens to the source and appends one JSON line per finished span to <paramref name="filePath"/>.</summary>
    public static IDisposable StartListening(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        if (Path.GetDirectoryName(Path.GetFullPath(filePath)) is { Length: > 0 } folder) Directory.CreateDirectory(folder);
        return new JsonLinesSink(filePath);
    }

    /// <summary>Starts a span under the open switch (or free-standing when none is open); <see langword="null"/> when off.</summary>
    public static Activity? Begin(string name)
    {
        if (!Source.HasListeners()) return null;
        var parent = _switch;
        return parent is null ? Detached(name) : Detached(name, parent.Context);
    }

    /// <summary>Starts a span under the open switch only, so background work that is not part of a switch stays out of it.</summary>
    public static Activity? BeginInSwitch(string name) =>
        _switch is { } parent && Source.HasListeners() ? Detached(name, parent.Context) : null;

    /// <summary>Opens the root span of one page or view switch, ending any switch still open as superseded.</summary>
    /// <param name="kind">Either <c>page</c> or <c>tab</c>.</param>
    /// <param name="from">The page or tab being left.</param>
    /// <param name="to">The page or tab being shown.</param>
    public static void BeginSwitch(string kind, string from, string to)
    {
        if (!Source.HasListeners()) return;
        if (_switch is { } open)
        {
            open.SetTag("superseded", true);
            End(open);
        }
        var root = Detached("view.switch");
        if (root is null) return;
        root.SetTag("kind", kind);
        root.SetTag("from", from);
        root.SetTag("to", to);
        root.SetTag("seq", ++_switchSequence);
        Counts.Clear();
        _switch = root;
    }

    /// <summary>Whether the open switch already shows <paramref name="to"/>, so a second report of the same change joins it.</summary>
    public static bool SwitchIsTo(string kind, string to) =>
        _switch is { } open && Equals(open.GetTagItem("kind"), kind) && Equals(open.GetTagItem("to"), to);

    /// <summary>Ends the open switch when the next two frames of <paramref name="top"/> have begun, which is the first frame after the change drew.</summary>
    /// <remarks>
    /// Layout runs, and the change is committed to the compositor, between a frame's callback and the next one, so the
    /// second callback marks the first frame that includes the new page. Headless runs tick the frame by hand, so
    /// the figure there is the render pass and not the GPU.
    /// </remarks>
    public static void EndAtFirstFrame(TopLevel top)
    {
        if (_switch is not { } root) return;
        var attached = Detached("switch.wait-for-frame", root.Context);
        top.RequestAnimationFrame(_ => top.RequestAnimationFrame(_ =>
        {
            Finish(attached);
            if (ReferenceEquals(_switch, root)) End(root);
        }));
    }

    /// <summary>Starts a span named <paramref name="name"/> that measures while the open switch lays out; <see langword="null"/> when no switch is open.</summary>
    public static Activity? BeginLayout(string name) => _switch is null ? null : Begin(name);

    private static void End(Activity root)
    {
        if (ReferenceEquals(_switch, root))
        {
            _switch = null;
            foreach (var (what, count) in Counts) root.SetTag("n." + what, count);
            Counts.Clear();
        }
        Finish(root);
    }

    private static Activity? Detached(string name, ActivityContext parent = default)
    {
        var previous = Activity.Current;
        var activity = parent == default
            ? Source.StartActivity(name, ActivityKind.Internal, parentContext: default)
            : Source.StartActivity(name, ActivityKind.Internal, parent);
        Activity.Current = previous;
        if (activity is not null && RunLabel is { } label) activity.SetTag("label", label);
        return activity;
    }

    /// <summary>Stops <paramref name="activity"/> without disturbing the ambient current span.</summary>
    public static void Finish(Activity? activity)
    {
        if (activity is null) return;
        var previous = Activity.Current;
        activity.Stop();
        Activity.Current = ReferenceEquals(previous, activity) ? null : previous;
    }

    private sealed class JsonLinesSink : IDisposable
    {
        private readonly object _gate = new();
        private readonly StreamWriter _writer;
        private readonly ActivityListener _listener;

        public JsonLinesSink(string path)
        {
            _writer = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite),
                new UTF8Encoding(false));
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == SourceName,
                Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = Write,
            };
            ActivitySource.AddActivityListener(_listener);
        }

        private void Write(Activity activity)
        {
            var line = new Dictionary<string, object?>
            {
                ["name"] = activity.OperationName,
                ["id"] = activity.SpanId.ToHexString(),
                ["parent"] = activity.ParentSpanId == default ? null : activity.ParentSpanId.ToHexString(),
                ["start"] = activity.StartTimeUtc.ToString("O", CultureInfo.InvariantCulture),
                ["ms"] = Math.Round(activity.Duration.TotalMilliseconds, 3),
            };
            foreach (var tag in activity.TagObjects) line[tag.Key] = tag.Value;
            var text = JsonSerializer.Serialize(line);
            lock (_gate) _writer.WriteLine(text);
        }

        public void Dispose()
        {
            _listener.Dispose();
            lock (_gate) _writer.Dispose();
        }
    }
}
