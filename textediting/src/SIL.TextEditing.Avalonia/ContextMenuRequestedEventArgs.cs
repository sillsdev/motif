using System;
using Avalonia;

namespace SIL.TextEditing.Avalonia
{
    /// <summary>How a context menu was asked for.</summary>
    public enum ContextMenuTrigger
    {
        /// <summary>A right click (or a long press): the place is the character under the pointer.</summary>
        Pointer,
        /// <summary>Shift+F10 or the Menu key: the place is the caret.</summary>
        Keyboard,
    }

    /// <summary>A context menu was asked for (ADR 0021): the host builds and shows it from <see cref="Info"/>. The field has already
    /// done what FieldWorks does first: a right click inside a selection kept it, and one outside moved the caret there.</summary>
    public sealed class ContextMenuRequestedEventArgs : EventArgs
    {
        public ContextMenuRequestedEventArgs(ContextMenuTrigger trigger, Point position, ContextInfo info)
        {
            Trigger = trigger;
            Position = position;
            Info = info;
        }

        public ContextMenuTrigger Trigger { get; }

        /// <summary>Where to open the menu, in the field's coordinates: the pointer, or just below the caret.</summary>
        public Point Position { get; }

        /// <summary>The clicked word and its range, its writing system, the run and its properties, any link or embedded object, the
        /// selection and the slot for spelling suggestions.</summary>
        public ContextInfo Info { get; }

    }
}
