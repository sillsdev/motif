using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;

namespace SIL.TextEditing.Avalonia
{
    // Paging and auto-scroll (ADR 0021, ADR 0025). The field moves the caret by a page and extends a drag past its edge; scrolling
    // the view that shows the field stays with the host, which is asked through ScrollRequested.
    public partial class RichTextField
    {
        // A drag past the edge scrolls this many pixels a pointer-move event: slowly while the pointer is within the zone, fast
        // beyond it (the speeds FieldWorks uses).
        const double SlowScroll = 10, FastScroll = 30, SlowZone = 30;

        /// <summary>The host asks to scroll its view: after the caret moves, after a page move, while a drag selects past the edge of what
        /// is visible, and when a find match is selected. See <see cref="ScrollRequestedEventArgs"/>.</summary>
        public event EventHandler<ScrollRequestedEventArgs>? ScrollRequested;

        /// <summary>How far PageUp and PageDown move the caret, in pixels: the host's own view height, when it knows it. Null (the
        /// default) uses <see cref="VisibleBounds"/>, the part of the field that shows.</summary>
        public double? PageHeight { get; set; }

        /// <summary>The part of the field that shows, in the field's coordinates: the field's bounds cut down to the scroll viewers
        /// around it. A field that is wholly visible gives its whole bounds.</summary>
        public Rect VisibleBounds
        {
            get
            {
                var visible = new Rect(Bounds.Size);
                foreach (var presenter in this.GetVisualAncestors().OfType<ScrollContentPresenter>())
                {
                    var origin = presenter.TranslatePoint(new Point(0, 0), this);
                    if (origin == null) continue;
                    visible = visible.Intersect(new Rect(origin.Value, presenter.Bounds.Size));
                }
                return visible;
            }
        }

        /// <summary>PageUp and PageDown, plain or with Shift (Ctrl goes to the corners of what shows instead, see <see cref="MovePageEdge"/>): the caret goes up or down by the
        /// page height and keeps its horizontal place, stopping at the first or last line; with Shift the selection grows. The host
        /// is asked to scroll by the distance the caret moved.</summary>
        void MovePage(bool down, bool extend)
        {
            var before = CaretRect();
            double page = PageHeight ?? VisibleBounds.Height;
            if (page < before.Height) page = before.Height;
            double total = 0;
            for (int i = 0; i < _layouts.Count; i++) total += ParagraphLayoutFor(i).Height;
            double y = before.Y + before.Height / 2 + (down ? page : -page);
            y = Math.Max(0, Math.Min(total - 1, y));
            var target = Locate(new Point(before.X, y));
            _suppressScroll = true;
            try { Move(target.Position, extend, false, target.Hit); }
            finally { _suppressScroll = false; }
            var after = CaretRect();
            RequestScroll(ScrollReason.Page, after, after.Y - before.Y);
        }

        /// <summary>Ctrl+PageUp and Ctrl+PageDown (with Shift, extending the selection): the caret goes to the top-left corner or the
        /// bottom-right corner of what shows, which is the start of the first visible line or the end of the last, as in FieldWorks.
        /// Nothing scrolls.</summary>
        void MovePageEdge(bool bottom, bool extend)
        {
            var visible = VisibleBounds;
            var point = bottom ? new Point(visible.Right, Math.Max(visible.Top, visible.Bottom - 1)) : new Point(visible.Left, visible.Top);
            var target = Locate(point);
            _suppressScroll = true;
            try { Move(target.Position, extend, false, target.Hit); }
            finally { _suppressScroll = false; }
        }

        bool _suppressScroll;

        /// <summary>Asks the host to show the caret, with a line of margin round it, as FieldWorks does when it scrolls a selection
        /// into view.</summary>
        void RequestCaretIntoView(ScrollReason reason = ScrollReason.Caret)
        {
            if (_suppressScroll) return;
            // The caret's place costs the layout of the paragraphs above it, so it is worked out only when somebody can scroll: a
            // host that listens, or a scroll viewer round the field.
            if (ScrollRequested == null && !this.GetVisualAncestors().OfType<ScrollViewer>().Any()) return;
            var r = CaretRect();
            RequestScroll(reason, new Rect(r.X, r.Y - r.Height, Math.Max(r.Width, 1), r.Height * 3), 0);
        }

        /// <summary>Asks the host to show a found range: <see cref="ScrollRequestedEventArgs.Target"/> is the whole match, so a match
        /// that runs over several lines is shown from its start, not only its end.</summary>
        void RequestRangeIntoView(TextRange range)
        {
            if (ScrollRequested == null && !this.GetVisualAncestors().OfType<ScrollViewer>().Any()) return;
            var layout = ParagraphLayoutFor(range.Start.Paragraph);
            double top = ParagraphTop(range.Start.Paragraph);
            Rect? union = null;
            foreach (var r in layout.SelectionRects(range.Start.Offset, range.End.Offset))
                union = union == null ? r : union.Value.Union(r);
            var target = union ?? CaretRect();
            RequestScroll(ScrollReason.FindMatch, union == null ? target : new Rect(target.X, target.Y + top, target.Width, target.Height), 0);
        }

        void RequestScroll(ScrollReason reason, Rect target, double delta)
        {
            var args = new ScrollRequestedEventArgs(reason, target, delta);
            ScrollRequested?.Invoke(this, args);
            if (args.Handled) return;
            if (delta != 0)
            {
                var viewer = this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
                if (viewer == null) return;
                double max = Math.Max(0, viewer.Extent.Height - viewer.Viewport.Height);
                viewer.Offset = new Vector(viewer.Offset.X, Math.Max(0, Math.Min(max, viewer.Offset.Y + delta)));
            }
            else this.BringIntoView(target);
        }

        /// <summary>While a drag selects, a pointer past the top or bottom of what shows extends the selection only to just past the
        /// edge (by the scroll speed), and asks the host to scroll by that speed; each pointer-move event does so once, with no timer,
        /// as FieldWorks does. Returns the point to select to.</summary>
        Point DragPoint(Point point)
        {
            var visible = VisibleBounds;
            if (point.Y < visible.Top)
            {
                double speed = visible.Top - point.Y < SlowZone ? SlowScroll : FastScroll;
                RequestScroll(ScrollReason.DragPastEdge, new Rect(visible.X, visible.Top - speed, visible.Width, speed), -speed);
                return new Point(point.X, visible.Top - speed);
            }
            if (point.Y > visible.Bottom)
            {
                double speed = point.Y - visible.Bottom < SlowZone ? SlowScroll : FastScroll;
                RequestScroll(ScrollReason.DragPastEdge, new Rect(visible.X, visible.Bottom, visible.Width, speed), speed);
                return new Point(point.X, visible.Bottom + speed);
            }
            return point;
        }
    }
}
