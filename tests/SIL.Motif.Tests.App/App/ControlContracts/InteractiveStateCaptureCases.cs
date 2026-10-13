namespace SIL.Motif.Tests.App.ControlContracts;

internal static class InteractiveStateCaptureCases
{
    internal static void RequireTooltipOpen(string state, bool isOpen)
    {
        if (!isOpen)
            throw new InvalidOperationException($"State '{state}' requires its tooltip to be open.");
    }

    internal static void RequireAvailability(string state, bool isEnabled, bool expectedEnabled)
    {
        if (isEnabled != expectedEnabled)
            throw new InvalidOperationException(
                $"State '{state}' requires a {(expectedEnabled ? "enabled" : "disabled")} control.");
    }

    internal static void RequireKeyboardFocus(string state, bool hasFocus)
    {
        if (!hasFocus)
            throw new InvalidOperationException($"State '{state}' requires keyboard focus.");
    }
}
