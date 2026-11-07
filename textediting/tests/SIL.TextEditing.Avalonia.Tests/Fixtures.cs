using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace SIL.TextEditing.Avalonia.Tests
{
    static class Fixtures
    {
        /// <summary>The registry the geometry tests start from: the fallback at 16 point, the size their widths were chosen for (the field's own
        /// fallback is FieldWorks' 10 point, which <see cref="FallbackTests"/> covers). A test that cares about the default makes its own field.</summary>
        public static WritingSystemRegistry SixteenPoint() => new WritingSystemRegistry { FallbackSize = 16 };

        /// <summary>A field laid out at the given width, not in a window.</summary>
        public static RichTextField Field(Document doc, double width = 520, Action<RichTextField>? configure = null)
        {
            var field = new RichTextField(doc) { WritingSystems = SixteenPoint() };
            configure?.Invoke(field);
            Layout(field, width);
            return field;
        }

        public static void Layout(RichTextField field, double width)
        {
            field.Measure(new Size(width, 4000));
            field.Arrange(new Rect(0, 0, width, field.DesiredSize.Height));
        }

        /// <summary>A field shown in a headless window, so it has a TopLevel (clipboard, focus).</summary>
        public static (Window Window, RichTextField Field) InWindow(Document doc, double width = 520)
        {
            var field = new RichTextField(doc) { WritingSystems = SixteenPoint() };
            var window = new Window { Width = width, Height = 400, Content = field };
            window.Show();
            Headless.Pump();
            return (window, field);
        }

        /// <summary>A 32 by 24 bitmap standing in for a picture object.</summary>
        public static IImage Picture(string data)
        {
            var wb = new WriteableBitmap(new PixelSize(32, 24), new Vector(96, 96), global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Premul);
            using (var fb = wb.Lock())
            {
                var px = new int[32 * 24];
                for (int y = 0; y < 24; y++)
                    for (int x = 0; x < 32; x++)
                        px[y * 32 + x] = unchecked((int)0xFF4060D0);
                System.Runtime.InteropServices.Marshal.Copy(px, 0, fb.Address, px.Length);
            }
            return wb;
        }

        /// <summary>The field drawn on a white page, as pixels.</summary>
        public static Shot Render(RichTextField field, double scale = 1) => new Shot(field, scale);

        /// <summary>A rendered field, readable by pixel.</summary>
        public sealed class Shot
        {
            readonly byte[] _buffer;
            readonly int _stride;

            public Shot(Control control, double scale = 1)
            {
                var size = new PixelSize((int)Math.Ceiling(control.Bounds.Width * scale), (int)Math.Ceiling(control.Bounds.Height * scale));
                Width = size.Width;
                Height = size.Height;
                using (var bitmap = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale)))
                {
                    bitmap.Render(control);
                    _stride = size.Width * 4;
                    _buffer = new byte[_stride * size.Height];
                    var handle = System.Runtime.InteropServices.GCHandle.Alloc(_buffer, System.Runtime.InteropServices.GCHandleType.Pinned);
                    try { bitmap.CopyPixels(new PixelRect(0, 0, size.Width, size.Height), handle.AddrOfPinnedObject(), _buffer.Length, _stride); }
                    finally { handle.Free(); }
                }
            }

            public int Width { get; }
            public int Height { get; }

            /// <summary>True when the pixel is dark: text ink on a white page.</summary>
            public bool IsInk(int x, int y, int threshold = 140)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height) return false;
                int o = y * _stride + x * 4;
                return _buffer[o + 3] > 128 && _buffer[o] < threshold && _buffer[o + 1] < threshold && _buffer[o + 2] < threshold;
            }

            /// <summary>The colour of a pixel (premultiplied alpha undone), or transparent outside the shot.</summary>
            public Color Pixel(int x, int y)
            {
                if (x < 0 || y < 0 || x >= Width || y >= Height) return Colors.Transparent;
                int o = y * _stride + x * 4;
                byte a = _buffer[o + 3];
                if (a == 0) return Colors.Transparent;
                return Color.FromArgb(a, (byte)Math.Min(255, _buffer[o + 2] * 255 / a), (byte)Math.Min(255, _buffer[o + 1] * 255 / a), (byte)Math.Min(255, _buffer[o] * 255 / a));
            }

            /// <summary>True when the pixel was drawn on at all (it is not transparent).</summary>
            public bool HasColor(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height && _buffer[y * _stride + x * 4 + 3] > 128;

            /// <summary>True when any pixel of the column between the rows is ink.</summary>
            public bool ColumnHasInk(int x, double top, double bottom, int threshold = 140)
            {
                for (int y = (int)Math.Floor(top); y < (int)Math.Ceiling(bottom); y++)
                    if (IsInk(x, y, threshold)) return true;
                return false;
            }

            /// <summary>The lowest row with ink in the columns between <paramref name="left"/> and <paramref name="right"/> (inner ones), or -1.</summary>
            public int InkBottom(double left, double right, double top, double bottom)
            {
                for (int y = Math.Min(Height - 1, (int)Math.Ceiling(bottom) - 1); y >= (int)top; y--)
                    for (int x = (int)Math.Floor(left) + 1; x < (int)Math.Ceiling(right) - 1; x++)
                        if (IsInk(x, y)) return y;
                return -1;
            }

            /// <summary>The highest row with ink in the columns between <paramref name="left"/> and <paramref name="right"/> (inner ones), or -1.</summary>
            public int InkTop(double left, double right, double top, double bottom)
            {
                for (int y = Math.Max(0, (int)top); y < Math.Min(Height, (int)Math.Ceiling(bottom)); y++)
                    for (int x = (int)Math.Floor(left) + 1; x < (int)Math.Ceiling(right) - 1; x++)
                        if (IsInk(x, y)) return y;
                return -1;
            }
        }
    }
}
