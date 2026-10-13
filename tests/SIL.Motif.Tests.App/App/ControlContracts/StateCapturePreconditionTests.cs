using Xunit;

namespace SIL.Motif.Tests.App.ControlContracts;

public sealed class StateCapturePreconditionTests
{
    [Fact]
    public void AnUnopenedTooltipCannotProduceATooltipCapture()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            InteractiveStateCaptureCases.RequireTooltipOpen("shell/refresh-tooltip", isOpen: false));

        Assert.Equal("State 'shell/refresh-tooltip' requires its tooltip to be open.", exception.Message);
    }

    [Fact]
    public void AControlInTheWrongAvailabilityStateCannotProduceADisabledCapture()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            InteractiveStateCaptureCases.RequireAvailability(
                "review/disabled-apply-hover", isEnabled: true, expectedEnabled: false));

        Assert.Equal("State 'review/disabled-apply-hover' requires a disabled control.", exception.Message);
    }

    [Fact]
    public void AnUnfocusedControlCannotProduceAKeyboardFocusCapture()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            InteractiveStateCaptureCases.RequireKeyboardFocus("shell/button-focus", hasFocus: false));

        Assert.Equal("State 'shell/button-focus' requires keyboard focus.", exception.Message);
    }
}
