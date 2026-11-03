using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
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
/// a machine that runs many suites at once. A test does not sleep a fixed time instead, because a sleep is a guess
/// about speed that a loaded machine turns into a failure; <c>minimumPasses</c> is for the one condition whose own
/// evaluation a starved machine slows, and it only adds looks.
/// </para>
/// <para>
/// <paramref name="between"/> is what lets the awaited work make progress while the test waits: a UI test passes a
/// dispatcher pump, a process test passes nothing. It also runs once after the condition first holds, so what a
/// test reads next has had one more turn of the pump.
/// </para>
/// </remarks>
public static class Wait
{
    /// <summary>The environment variable that multiplies every deadline taken through this class.</summary>
    public const string ScaleVariable = "MOTIF_TEST_DEADLINE_SCALE";

    /// <summary>The deadline a wait takes when its caller has no better bound; long, as only a failure pays it.</summary>
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(15);

    /// <summary>The largest scale taken; a larger value would turn a failing wait into hours.</summary>
    public const double MaxScale = 20;

    /// <summary>The multiplier read from <see cref="ScaleVariable"/>: 1 when it is unset or unusable.</summary>
    public static double Scale { get; }

    /// <summary>Why <see cref="ScaleVariable"/> was not used as given, or null; every failure text carries it.</summary>
    public static string? ScaleProblem { get; }

    static Wait()
    {
        Scale = ParseScale(Environment.GetEnvironmentVariable(ScaleVariable), out var problem);
        ScaleProblem = problem;
    }

    /// <summary>
    /// Parses a scale value: unset or empty is 1, a number of at least 1 is kept up to <see cref="MaxScale"/>, and
    /// anything else is 1, with <paramref name="problem"/> saying so, so a bad value never breaks a type initializer.
    /// </summary>
    public static double ParseScale(string? value, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(value)) return 1;
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) ||
            !double.IsFinite(scale) || scale < 1)
        {
            problem = $"{ScaleVariable} must be a number from 1 to {MaxScale}, not '{value}'; 1 was used.";
            return 1;
        }
        if (scale <= MaxScale) return scale;
        problem = $"{ScaleVariable} '{value}' is above {MaxScale}; {MaxScale} was used.";
        return MaxScale;
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
            if (condition())
            {
                between?.Invoke();
                return true;
            }
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
            if (condition())
            {
                between?.Invoke();
                return true;
            }
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
    /// <param name="why">What the test was waiting for, in words a reader of the failure understands; the
    /// condition's own source text when omitted.</param>
    /// <param name="timeout">The deadline before scaling; <see cref="Default"/> when omitted.</param>
    /// <param name="detail">The state of the awaited work, read only on failure.</param>
    /// <param name="between">Work that lets the awaited code progress, run before each sleep.</param>
    /// <param name="minimumPasses">The fewest times the condition is tried, whatever the clock says.</param>
    /// <param name="conditionText">Filled in by the compiler from the argument passed as <paramref name="condition"/>.</param>
    public static void Until(Func<bool> condition, string? why = null, TimeSpan? timeout = null,
        Func<string>? detail = null, Action? between = null, int minimumPasses = 1,
        [CallerArgumentExpression(nameof(condition))] string conditionText = "")
    {
        if (!For(condition, timeout ?? Default, between, minimumPasses))
            Assert.Fail(Describe(why ?? "Timed out waiting for " + conditionText, detail));
    }

    /// <summary>The asynchronous <see cref="Until"/>.</summary>
    public static async Task UntilAsync(Func<bool> condition, string? why = null, TimeSpan? timeout = null,
        Func<string>? detail = null, Action? between = null, int minimumPasses = 1,
        [CallerArgumentExpression(nameof(condition))] string conditionText = "")
    {
        if (!await ForAsync(condition, timeout ?? Default, between, minimumPasses).ConfigureAwait(true))
            Assert.Fail(Describe(why ?? "Timed out waiting for " + conditionText, detail));
    }

    /// <summary>The failure text: what was awaited, then the state <paramref name="detail"/> reads.</summary>
    internal static string Describe(string why, Func<string>? detail)
    {
        var state = detail?.Invoke();
        var text = string.IsNullOrWhiteSpace(state) ? why : why + "; " + state;
        return ScaleProblem is null ? text : text + " (" + ScaleProblem + ")";
    }

    private static long Deadline(TimeSpan timeout) =>
        Stopwatch.GetTimestamp() + (long)(Scaled(timeout).TotalSeconds * Stopwatch.Frequency);
}
