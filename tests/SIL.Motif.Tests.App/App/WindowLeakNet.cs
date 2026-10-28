using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Xunit.Sdk;

[assembly: SIL.Motif.Tests.App.WindowLeakNet]

namespace SIL.Motif.Tests.App;

/// <summary>
/// After every App test, closes any window the test constructed and left open, and says which test did.
/// </summary>
/// <remarks>
/// <para>
/// Avalonia's <c>Application</c> subscribes every window to its <c>StylesAdded</c> event in the window's
/// constructor and drops the subscription only in <c>Close()</c>. A window a test never closes therefore stays
/// rooted, with its whole visual tree and view models (60-110 MB), until the process exits, and every later
/// garbage collection in the process marks and copies it.
/// </para>
/// <para>
/// The net reads that subscription list, which sees windows that were constructed but never shown. It looks only at
/// windows new since the test began, so a window a class fixture opened earlier is not touched; a fixture that
/// opens its window lazily inside a test registers it with <see cref="Keep"/>. A leak is written to standard error
/// and appended to the file named by <c>MOTIF_WINDOW_LEAK_LOG</c>, then closed. With
/// <c>MOTIF_WINDOW_LEAK_STRICT=1</c> the test fails instead, so a new leak can be made a gate failure.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class WindowLeakNetAttribute : BeforeAfterTestAttribute
{
    private static readonly FieldInfo? StylesAdded =
        typeof(Application).GetField("_stylesAdded", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly ConditionalWeakTable<Window, object> Kept = new();

    [ThreadStatic] private static HashSet<Window>? _before;

    /// <summary>Exempts <paramref name="window"/>, which a fixture owns and closes itself, from the net.</summary>
    internal static T Keep<T>(T window) where T : Window
    {
        Kept.AddOrUpdate(window, Kept);
        return window;
    }

    /// <summary>Every window constructed and not yet closed, read on the Avalonia thread.</summary>
    internal static IReadOnlyList<Window> OpenWindows()
    {
        if (Application.Current is not { } application || StylesAdded?.GetValue(application) is not Delegate handlers)
            return [];
        return handlers.GetInvocationList().Select(handler => handler.Target).OfType<Window>().Distinct().ToList();
    }

    /// <summary>Closes and returns the windows open now that were not in <paramref name="before"/> and are not kept.</summary>
    internal static IReadOnlyList<Window> CloseLeaked(IReadOnlySet<Window> before)
    {
        var leaked = OpenWindows().Where(window => !before.Contains(window) && !Kept.TryGetValue(window, out _)).ToList();
        foreach (var window in leaked) window.Close();
        return leaked;
    }

    public override void Before(System.Reflection.MethodInfo methodUnderTest)
    {
        _before = null;
        if (!AvaloniaHeadlessPlatform.IsCreated) return;
        HashSet<Window>? open = null;
        AvaloniaHeadlessPlatform.Invoke(() => open = OpenWindows().ToHashSet());
        _before = open;
    }

    public override void After(System.Reflection.MethodInfo methodUnderTest)
    {
        var before = _before;
        _before = null;
        if (before is null && !AvaloniaHeadlessPlatform.IsCreated) return;
        before ??= [];
        IReadOnlyList<Window> leaked = [];
        AvaloniaHeadlessPlatform.Invoke(() => leaked = CloseLeaked(before));
        if (leaked.Count == 0) return;

        var message = $"{methodUnderTest.DeclaringType?.FullName}.{methodUnderTest.Name} left {leaked.Count} window(s) open: " +
            string.Join(", ", leaked.Select(window => window.GetType().Name));
        Console.Error.WriteLine("WINDOW LEAK: " + message);
        if (Environment.GetEnvironmentVariable("MOTIF_WINDOW_LEAK_LOG") is { Length: > 0 } log)
        {
            try { File.AppendAllText(log, message + Environment.NewLine); }
            catch (IOException) { }
        }
        if (Environment.GetEnvironmentVariable("MOTIF_WINDOW_LEAK_STRICT") == "1")
            throw new InvalidOperationException(message);
    }
}

/// <summary>Closes a test's window deterministically, so the leak net has nothing to report.</summary>
internal static class WindowClosing
{
    /// <summary>Closes <paramref name="window"/> when the returned value is disposed: <c>using var _ = window.CloseOnExit();</c>.</summary>
    internal static IDisposable CloseOnExit(this Window window) => new Closer(window);

    private sealed class Closer(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }
}
