using Avalonia;
using Avalonia.Controls;
#if NET10_0_OR_GREATER
using Avalonia.Input;
#endif

namespace SIL.TextEditing.Avalonia
{
    // Context-menu information (ADR 0021, ADR 0025). The field rides on Avalonia's own context request (a right click, the Menu
    // key, a long press), so one request gives one event, whatever raised it; Avalonia does not raise one for Shift+F10 on either
    // line, so the field raises that one itself. A host builds its menu from this event or sets Avalonia's ContextMenu on the
    // field; when it does both, the event wins and the ContextMenu does not open.
    public partial class RichTextField
    {
        /// <summary>A context menu was asked for: the host builds it from <see cref="ContextMenuRequestedEventArgs.Info"/> (the word
        /// and its range, its writing system, the run's properties, any link or embedded object, the selection, and the spelling problem
        /// with its suggestions when the field has a spell checker, <see cref="ContextInfo.Spelling"/>) and shows it at <see cref="ContextMenuRequestedEventArgs.Position"/>. A
        /// right click inside a selection has kept it and one outside has moved the caret there first, as in FieldWorks. A host
        /// builds its menu from this event or sets <c>ContextMenu</c> on the field; with both, this event wins.</summary>
        public event System.EventHandler<ContextMenuRequestedEventArgs>? ContextMenuRequested;

        void HookContextRequests()
        {
            ContextRequested += (sender, e) =>
            {
                bool pointer = e.TryGetPosition(this, out var at);
                if (ContextMenuRequested == null) return;
                e.Handled = true;
                RaiseContextMenuRequested(pointer, at);
            };
        }

        /// <summary>Shift+F10: Avalonia raises no context request for it, so the field raises the standard one (as for the Menu key), which gives
        /// the event to a host that listens and opens a ContextMenu for a host that only sets one.</summary>
        void RaiseKeyboardContextRequest()
        {
            RaiseEvent(new ContextRequestedEventArgs { RoutedEvent = ContextRequestedEvent, Source = this });
        }

        /// <summary>True when the pointer is over the character it hit, not in the empty space past the end of a line.</summary>
        bool PointerIsOverText(Located loc, Point at)
        {
            int index = loc.Character.Offset;
            var para = Editor.Document.Paragraphs[loc.Character.Paragraph];
            if (index >= para.Length) return false;
            double y = at.Y - ParagraphTop(loc.Character.Paragraph);
            foreach (var r in ParagraphLayoutFor(loc.Character.Paragraph).SelectionRects(index, index + 1))
                if (at.X >= r.X && at.X <= r.Right && y >= r.Y && y <= r.Bottom) return true;
            return false;
        }

        /// <summary>Adds the spelling problem at the place, if the word there is marked (ADR 0020, 0021). A pointer means the character under it, so it
        /// must be inside the marked text; a caret may also be at its end.</summary>
        ContextInfo WithSpelling(ContextInfo info, bool pointer)
        {
            if (info.Word.Length == 0) return info;
            var spelling = SpellingSuggestionsAt(info.Position);
            if (spelling == null || (pointer && info.Position.Offset >= spelling.End.Offset)) return info;
            info.Spelling = spelling;
            info.SpellingSuggestions.AddRange(spelling.All);
            return info;
        }

        /// <summary>Raises <see cref="ContextMenuRequested"/>: for the character under the pointer, or for the caret.</summary>
        void RaiseContextMenuRequested(bool pointer, Point at)
        {
            var handler = ContextMenuRequested;
            if (handler == null) return;
            if (pointer)
                {
                var loc = Locate(at);
                handler(this, new ContextMenuRequestedEventArgs(ContextMenuTrigger.Pointer, at, WithLanguage(WithSpelling(Editor.DescribeContext(loc.Character, false, PointerIsOverText(loc, at)), true))));
            }
            else
            {
                var caret = CaretRect();
                handler(this, new ContextMenuRequestedEventArgs(ContextMenuTrigger.Keyboard, new Point(caret.X, caret.Bottom), WithLanguage(WithSpelling(Editor.DescribeContext(Editor.Caret, true), false))));
            }
        }
    }
}
