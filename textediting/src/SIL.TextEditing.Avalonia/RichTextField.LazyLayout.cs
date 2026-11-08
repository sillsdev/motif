using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace SIL.TextEditing.Avalonia
{
    public partial class RichTextField
    {
        const double EstimatedParagraphHeight = 24;
        const double InitialViewportHeight = 600;
        Rect? _effectiveViewport;
        double _measureHeight = InitialViewportHeight;
        int _heightAnchor;
        double _anchorShift;
        bool _anchorAtEnd;

        void ViewportChanged(object? sender, EffectiveViewportChangedEventArgs e)
        {
            if (_effectiveViewport == e.EffectiveViewport) return;
            _effectiveViewport = e.EffectiveViewport;
            InvalidateMeasure();
            InvalidateVisual();
        }

        Rect LayoutViewport()
        {
            Rect? viewport = null;
            foreach (var presenter in this.GetVisualAncestors().OfType<ScrollContentPresenter>())
            {
                var origin = presenter.TranslatePoint(default, this);
                if (origin == null || presenter.Bounds.Height <= 0) continue;
                var bounds = new Rect(origin.Value, presenter.Bounds.Size);
                viewport = viewport == null ? bounds : viewport.Value.Intersect(bounds);
            }
            if (viewport != null) return viewport.Value;
            if (_effectiveViewport is Rect effective && !double.IsInfinity(effective.Height)) return effective;
            var root = TopLevel.GetTopLevel(this);
            if (root != null)
            {
                double height = root.Bounds.Height > 0 ? root.Bounds.Height : root.Height;
                var origin = root.TranslatePoint(default, this);
                if (origin != null && height > 0 && !double.IsInfinity(height))
                    return new Rect(origin.Value, new Size(Math.Max(0, _layoutWidth), height));
            }
            return new Rect(0, 0, Math.Max(0, _layoutWidth), _measureHeight);
        }

        double ParagraphHeight(int index) => Current(index).Height;

        double StoredParagraphTop(int index)
        {
            double top = 0;
            for (int i = 0; i < index && i < _info.Count; i++) top += _info[i].Height;
            return top;
        }

        double EstimatedHeight()
        {
            double height = 0;
            for (int i = 0; i < _info.Count; i++) height += ParagraphHeight(i);
            return height;
        }

        int ParagraphAtHeight(double position)
        {
            double top = 0;
            for (int i = 0; i < _info.Count; i++)
            {
                top += ParagraphHeight(i);
                if (position < top) return i;
            }
            return Math.Max(0, _info.Count - 1);
        }

        // Keep estimates independent of cached layouts so invalidation does not collapse the scroll extent.
        void RememberHeight(int index, double height)
        {
            var info = _info[index];
            double change = height - info.Height;
            info.Height = height;
            if (Math.Abs(change) < 0.001) return;
            if (_onScreen && index < _heightAnchor) _anchorShift += change;
            InvalidateMeasure();
        }

        void LayoutVisibleParagraphs()
        {
            if (_info.Count == 0) return;
            var viewport = LayoutViewport();
            _anchorAtEnd = false;
            if (viewport.Height <= 0 || viewport.Bottom <= 0) return;
            double top = Math.Max(0, viewport.Top + _anchorShift);
            double bottom = viewport.Bottom + _anchorShift;
            _heightAnchor = ParagraphAtHeight(top);
            bool caretVisible = false;
            int caret = Editor.Caret.Paragraph;
            if (IsFocused && caret >= 0 && caret < _info.Count)
            {
                var layout = _layouts[caret];
                if (layout != null)
                {
                    var rect = layout.CaretRect(Editor.Caret.Offset, false, CurrentHit());
                    double caretTop = ParagraphTop(caret) + rect.Y;
                    caretVisible = caretTop >= top && caretTop + rect.Height <= bottom;
                    if (caretVisible) _heightAnchor = caret;
                }
            }
            var viewer = this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
            bool atEnd = !caretVisible && viewer != null && viewer.Extent.Height > viewer.Viewport.Height &&
                viewer.Offset.Y >= viewer.Extent.Height - viewer.Viewport.Height - 1 &&
                EndsScrollContent(viewer);
            if (atEnd)
            {
                _heightAnchor = _info.Count;
                _anchorAtEnd = true;
                double height = 0;
                for (int i = _info.Count - 1; i >= 0 && height <= viewport.Height * 2; i--)
                    height += ParagraphLayoutFor(i).Height;
                return;
            }
            double shiftBefore = _anchorShift;
            int first = ParagraphAtHeight(Math.Max(0, top - viewport.Height));
            double y = ParagraphTop(first);
            for (int i = first; i < _info.Count && y <= bottom + viewport.Height + _anchorShift - shiftBefore; i++)
                y += ParagraphLayoutFor(i).Height;
        }

        bool EndsScrollContent(ScrollViewer viewer)
        {
            if (!(viewer.Content is Visual content)) return false;
            var bottom = this.TranslatePoint(new Point(0, Bounds.Height), content);
            return bottom != null && bottom.Value.Y + Margin.Bottom >= content.Bounds.Height - 1;
        }

        void PrepareParagraph(int index)
        {
            ParagraphLayoutFor(index);
            double margin = Math.Max(EstimatedParagraphHeight, LayoutViewport().Height);
            double height = 0;
            for (int i = index - 1; i >= 0 && height < margin; i--) height += ParagraphLayoutFor(i).Height;
            height = 0;
            for (int i = index + 1; i < _info.Count && height < margin * 2; i++) height += ParagraphLayoutFor(i).Height;
        }

        // Offset is clamped to the extent, so apply corrections only after arrange publishes the new extent.
        void CorrectScrollAnchor(object? sender, EventArgs e)
        {
            double shift = _anchorShift;
            bool atEnd = _anchorAtEnd;
            _anchorShift = 0;
            _anchorAtEnd = false;
            if (Math.Abs(shift) < 0.001 && !atEnd) return;
            var viewer = this.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
            if (viewer == null) return;
            double offset = Math.Max(0, atEnd ? viewer.Extent.Height - viewer.Viewport.Height : viewer.Offset.Y + shift);
            if (Math.Abs(offset - viewer.Offset.Y) < 0.001) return;
            viewer.Offset = new Vector(viewer.Offset.X, offset);
            InvalidateVisual();
        }
    }
}
