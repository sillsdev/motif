using System;
using Avalonia;

namespace SIL.TextEditing.Avalonia
{
    /// <summary>Why a field asks its host to scroll.</summary>
    public enum ScrollReason
    {
        /// <summary>The caret moved (typing, an arrow key, a click) and should be visible.</summary>
        Caret,
        /// <summary>PageUp or PageDown moved the caret by a page; the view should move with it.</summary>
        Page,
        /// <summary>A mouse drag that selects text went past the edge of what is visible.</summary>
        DragPastEdge,
        /// <summary>The host's find dialog selected a match.</summary>
        FindMatch,
    }

    /// <summary>A field asks the host to scroll the view that shows it (ADR 0021): the field never scrolls anything itself. When no
    /// handler sets <see cref="Handled"/>, the field asks the scroll viewer around it, if any: it scrolls by
    /// <see cref="Delta"/> when that is not zero, and otherwise brings <see cref="Target"/> into view.</summary>
    public sealed class ScrollRequestedEventArgs : EventArgs
    {
        public ScrollRequestedEventArgs(ScrollReason reason, Rect target, double delta)
        {
            Reason = reason;
            Target = target;
            Delta = delta;
        }

        public ScrollReason Reason { get; }

        /// <summary>The part of the field to make visible, in the field's own coordinates: the caret with a line of margin, the
        /// match, or the strip just past the edge a drag went beyond.</summary>
        public Rect Target { get; }

        /// <summary>For <see cref="ScrollReason.Page"/> and <see cref="ScrollReason.DragPastEdge"/>, how far to scroll the view, in
        /// pixels: positive moves down the text, negative up. A page request carries how far the caret moved, so the caret stays
        /// where it was on the screen. Zero for the other reasons.</summary>
        public double Delta { get; }

        /// <summary>Set by a host that scrolls by itself; the field then does nothing more.</summary>
        public bool Handled { get; set; }
    }
}
