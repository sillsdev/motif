using System;

namespace SIL.TextEditing.Avalonia
{
    /// <summary>Which way the user is leaving a field (ADR 0021): to the previous or the next one, in the host's order.</summary>
    public enum LeaveDirection { Previous, Next }

    /// <summary>The key that asked to leave a field.</summary>
    public enum LeaveKey { Tab, Left, Right, Up, Down }

    /// <summary>The user is trying to leave a <see cref="RichTextField"/>: Tab, Shift+Tab, Ctrl+Tab or Ctrl+Shift+Tab, or an arrow key
    /// that has nowhere left to go inside the field. FieldWorks views signal this by not handling the key, and the
    /// host (DataTree) then moves focus; this event is the same hand-over made explicit. The host sets
    /// <see cref="Handled"/> when it has moved focus (or decided to stay).</summary>
    public sealed class LeaveRequestedEventArgs : EventArgs
    {
        public LeaveRequestedEventArgs(LeaveDirection direction, LeaveKey key, bool toEnd, double goalX)
        {
            Direction = direction;
            Key = key;
            ToEnd = toEnd;
            GoalX = goalX;
        }

        /// <summary>The logical direction of the move that failed: backwards at the start of the text or Shift+Tab, forwards
        /// at its end or Tab. In a right-to-left paragraph Left is forwards.</summary>
        public LeaveDirection Direction { get; }
        /// <summary>The key pressed, so the host can tell an arrow from a Tab.</summary>
        public LeaveKey Key { get; }
        /// <summary>True for Ctrl+Tab and Ctrl+Shift+Tab: the host goes to the last (Next) or the first (Previous) field
        /// instead of the neighbouring one, as FieldWorks does.</summary>
        public bool ToEnd { get; }
        /// <summary>For Up and Down: the horizontal position of the caret in the field's own coordinates, so the host can put
        /// the caret at the same x in the next field; NaN for every other key.</summary>
        public double GoalX { get; }
        /// <summary>Set by the host when it took the request. A Tab that nobody handles keeps bubbling, so normal focus
        /// navigation still works; an arrow nobody handles does what it did before (Up and Down go to the text's ends).</summary>
        public bool Handled { get; set; }
    }
}
