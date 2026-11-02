using System.Diagnostics;
using System.Globalization;
using Xunit;

namespace SIL.Motif.Tests.TestFixtures;

/// <summary>
/// The one way a test waits for something that happens on another thread or in another process: poll a condition
/// against a real-time deadline, and say what was still unmet when the deadline passes.
/// </summary>
/// <remarks>
/// <para>
/// A deadline costs time only on a failing run, because a wait ends the moment its condition holds. So it is set
/// generously and then multiplied by <see cref="Scale"/>, which the <see cref="ScaleVariable"/> variable raises on
/// a machine that runs many suites at once. A test never counts passes or sleeps a fixed time instead, because a
/// pass count or a sleep is a guess about speed that a loaded machine turns into a failure.
/// </para>
/// <para>
/// <paramref name="between"/> is what lets the awaited work make progress while the test waits: a UI test passes a
/// dispatcher pump, a process test passes nothing.
/// </para>
/// </remarks>
public static class Wait
{
    /// <summary>The environment variable that multiplies every deadline taken through this class.</summary>
    public const string ScaleVariable = "MOTIF_TEST_DEADLINE_SCALE";

    /// <summary>The deadline a wait takes when its caller has no better bound; long, as only a failure pays it.</summary>
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(15);

    /// <summary>The multiplier read from <see cref="ScaleVariable"/>, or 1 when it is unset.</summary>
    public static double Scale { get; } = ParseScale(Environment.GetEnvironmentVariable(ScaleVariable));

    /// <summary>Parses a scale value: unset or empty is 1, and anything but a number of at least 1 is an error.</summary>
    public static double ParseScale(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 1;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) &&
            double.IsFinite(scale) && scale >= 1)
            return scale;
        throw new InvalidOperationException($"{ScaleVariable} must be a number of at least 1, not '{value}'.");
    }

    /// <summary><paramref name="timeout"/> multiplied by <see cref="Scale"/>.</summary>
    public static TimeSpan Scaled(TimeSpan timeout) => timeout * Scale;

    /// <summary>
    /// Tries <paramref name="condition"/> until it holds or the scaled <paramref name="timeout"/> passes, running
    /// <paramref name="between"/> before each sleep, and returns whether it held. The condition is tried at least
    /// <paramref name="minimumPasses"/> times whatever the clock says, for a condition whose own evaluation is
    /// what a starved machine slows.
    /// </summary>
    public static bool For(Func<bool> condition, TimeSpan timeout, Action? between = null, int minimumPasses = 1)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var deadline = Deadline(timeout);
        for (var pass = 1; ; pass++)
        {
            if (condition()) return true;
            var expired = pass >= minimumPasses && Stopwatch.GetTimestamp() >= deadline;
            between?.Invoke();
            if (expired) return condition();
            Thread.Sleep(PollInterval);
        }
    }

    /// <summary>The asynchronous <see cref="For"/>, for a test that awaits instead of blocking its thread.</summary>
    public static async Task<bool> ForAsync(
        Func<bool> condition, TimeSpan timeout, Action? between = null, int minimumPasses = 1)
    {
        ArgumentNullException.ThrowIfNull(condition);
        var deadline = Deadline(timeout);
        for (var pass = 1; ; pass++)
        {
            if (condition()) return true;
            var expired = pass >= minimumPasses && Stopwatch.GetTimestamp() >= deadline;
            between?.Invoke();
            if (expired) return condition();
            await Task.Delay(PollInterval).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Waits for <paramref name="condition"/>, failing the test with <paramref name="why"/> and the text of
    /// <paramref name="detail"/> when it does not hold in time.
    /// </summary>
    /// <param name="condition">What must become true.</param>
    /// <param name="timeout">The deadline before scaling; <see cref="Default"/> when omitted.</param>
    /// <param name="why">What the test was waiting for, in words a reader of the failure understands.</param>
    /// <param name="detail">The state of the awaited work, read only on failure.</param>
    /// <param name="between">Work that lets the awaited code progress, run before each sleep.</param>
    /// <param name="minimumPasses">The fewest times the condition is tried, whatever the clock says.</param>
    public static void Until(Func<bool> condition, string why, TimeSpan? timeout = null, Func<string>? detail = null,
        Action? between = null, int minimumPasses = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        if (!For(condition, timeout ?? Default, between, minimumPasses)) Assert.Fail(Describe(why, detail));
    }

    /// <summary>The asynchronous <see cref="Until"/>.</summary>
    public static async Task UntilAsync(Func<bool> condition, string why, TimeSpan? timeout = null,
        Func<string>? detail = null, Action? between = null, int minimumPasses = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(why);
        if (!await ForAsync(condition, timeout ?? Default, between, minimumPasses).ConfigureAwait(true))
            Assert.Fail(Describe(why, detail));
    }

    /// <summary>The failure text: what was awaited, then the state <paramref name="detail"/> reads.</summary>
    internal static string Describe(string why, Func<string>? detail)
    {
        var state = detail?.Invoke();
        return string.IsNullOrWhiteSpace(state) ? why : why + "; " + state;
    }

    private static long Deadline(TimeSpan timeout) =>
        Stopwatch.GetTimestamp() + (long)(Scaled(timeout).TotalSeconds * Stopwatch.Frequency);
}
