using Avalonia.Controls;
using Avalonia.Input;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    public class LeavePlaygroundTests
    {
        [Fact]
        public void Tab_and_edge_arrows_are_logged_as_leave_requests_and_the_host_check_box_takes_them() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Empty))
            {
                s.Press(Key.Tab);
                Assert.Contains("leave requested: Next by Tab", s.Log);
                s.Press(Key.Tab, KeyModifiers.Control | KeyModifiers.Shift);
                Assert.Contains("leave requested: Previous by Tab (to the first field)", s.Log);
                s.Press(Key.Left);
                Assert.Contains("leave requested: Previous by Left", s.Log);
                s.Press(Key.Down);
                Assert.Contains("leave requested: Next by Down at x ", s.Log);
                Assert.DoesNotContain("the host moved focus", s.Log);

                s.Find<CheckBox>(PlaygroundView.Names.HostHandlesLeave).IsChecked = true;
                Headless.Pump();
                s.Press(Key.Right);
                Assert.Contains("leave requested: Next by Right: the host moved focus", s.Log);
            }
        });
    }
}
