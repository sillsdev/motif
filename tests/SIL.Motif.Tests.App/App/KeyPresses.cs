using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using SIL.Motif.Tests.TestFixtures;

namespace SIL.Motif.Tests.App;

/// <summary>
/// Presses a key and then waits for the product state the key is meant to produce. A key handler may await work
/// before it moves focus or selection, so one dispatcher pump after the press can return before the state moves.
/// </summary>
internal static class KeyPresses
{
    /// <summary>The deadline before scaling; generous because only a failing run pays it.</summary>
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Presses <paramref name="key"/> once and settles the window until <paramref name="reached"/> holds. On timeout the
    /// failure names <paramref name="expected"/> and the text <paramref name="found"/> returns.
    /// </summary>
    internal static async Task PressUntilAsync(Window window, Key key, Func<bool> reached, string expected,
        Func<string> found, RawInputModifiers modifiers = RawInputModifiers.None,
        PhysicalKey physicalKey = PhysicalKey.None, bool release = false)
    {
        Press(window, key, modifiers, physicalKey, release);
        await Wait.UntilAsync(reached, Why(key, expected), Deadline, () => "found " + found(),
            between: () => PageScreenshots.Settle(window));
    }

    /// <summary>The blocking <see cref="PressUntilAsync"/>, for a test that runs on the UI thread without awaiting.</summary>
    internal static void PressUntil(Window window, Key key, Func<bool> reached, string expected,
        Func<string> found, RawInputModifiers modifiers = RawInputModifiers.None,
        PhysicalKey physicalKey = PhysicalKey.None, bool release = false)
    {
        Press(window, key, modifiers, physicalKey, release);
        Wait.Until(reached, Why(key, expected), Deadline, () => "found " + found(),
            between: () => PageScreenshots.Settle(window));
    }

    /// <summary>
    /// Settles <paramref name="window"/> until <paramref name="reached"/> holds, after a key sequence the caller sent
    /// itself (a chord, or a key sent to a popup root); the failure names <paramref name="expected"/> and what was found.
    /// </summary>
    internal static void SettleUntil(Window window, Func<bool> reached, string expected, Func<string> found) =>
        Wait.Until(reached, "after the key sequence, expected " + expected, Deadline, () => "found " + found(),
            between: () => PageScreenshots.Settle(window));

    private static string Why(Key key, string expected) => $"after {key}, expected {expected}";

    private static void Press(Window window, Key key, RawInputModifiers modifiers, PhysicalKey physicalKey, bool release)
    {
        window.KeyPress(key, modifiers, physicalKey, null);
        if (release) window.KeyRelease(key, modifiers, physicalKey, null);
    }
}
