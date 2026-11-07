using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Xunit;

namespace SIL.TextEditing.Playground.Tests
{
    /// <summary>A fact that is opt-in: it runs only when the environment variable <see cref="Switch"/> is 1 and is skipped otherwise, so the default test run
    /// never writes into the working tree. It needs Avalonia 12 (net10.0) for the same pixels every time.</summary>
    public sealed class ScreenshotFactAttribute : FactAttribute
    {
        public const string Switch = "TEXTEDITING_SCREENSHOTS";

        public ScreenshotFactAttribute()
        {
#if !NET10_0_OR_GREATER
            Skip = "screenshots are made on net10.0 (Avalonia 12) only";
#else
            if (Environment.GetEnvironmentVariable(Switch) != "1")
                Skip = "opt-in: set " + Switch + "=1 to write the PNGs in docs/images";
#endif
        }
    }

    /// <summary>Regenerates the screenshots in docs/images: the playground in a headless Skia window of a fixed size with the Fluent theme and the playground's own
    /// fixed fonts, one PNG per scenario that shows what the editor does. Run it with
    /// <c>$env:TEXTEDITING_SCREENSHOTS=1; dotnet test tests\SIL.TextEditing.Playground.Tests -c Release -f net10.0 --filter ScreenshotGenerator</c>.</summary>
    public class ScreenshotGenerator
    {
        const double W = 1280, H = 800;

        static string OutDir()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SIL.TextEditing.sln"))) dir = dir.Parent;
            if (dir == null) throw new InvalidOperationException("SIL.TextEditing.sln not found above " + AppContext.BaseDirectory);
            var path = Path.Combine(dir.FullName, "docs", "images");
            Directory.CreateDirectory(path);
            return path;
        }

        /// <summary>Renders the window (open menus included) and writes the whole of it, or <paramref name="crop"/> of it, as a PNG.</summary>
        static void Save(Session s, string name, Rect? crop = null)
        {
            Headless.Pump();
            var window = s.Window;
            var full = new PixelSize((int)window.Width, (int)window.Height);
            var area = crop.HasValue ? new PixelRect((int)crop.Value.X, (int)crop.Value.Y, (int)crop.Value.Width, (int)crop.Value.Height) : new PixelRect(full);
            using (var render = new RenderTargetBitmap(full))
            {
                render.Render(window);
                var stride = full.Width * 4;
                var buffer = new byte[stride * full.Height];
                var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try { render.CopyPixels(new PixelRect(full), handle.AddrOfPinnedObject(), buffer.Length, stride); }
                finally { handle.Free(); }
                using (var cropped = new WriteableBitmap(area.Size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
                {
                    using (var fb = cropped.Lock())
                        for (int y = 0; y < area.Height; y++)
                            Marshal.Copy(buffer, (area.Y + y) * stride + area.X * 4, fb.Address + y * fb.RowBytes, area.Width * 4);
#if NET10_0_OR_GREATER
                    cropped.Save(Path.Combine(OutDir(), name + ".png"), new PngBitmapEncoderOptions());
#else
                    cropped.Save(Path.Combine(OutDir(), name + ".png"));
#endif
                }
            }
        }

        /// <summary>A full-width strip of the window of <paramref name="height"/> pixels that starts a little above <paramref name="control"/>, so each shot starts at
        /// the field (or at the switches above it) however many lines the scenario description takes.</summary>
        static Rect From(Session s, Control control, double height)
        {
            double top = Math.Max(0, control.TranslatePoint(new Point(0, 0), s.Window)!.Value.Y - 8);
            return new Rect(0, top, W, Math.Min(height, H - top));
        }

        /// <summary>The strip that starts at the field and the inspector beside it (the inspector, because a long field is scrolled and its own top is out of sight).</summary>
        static Rect FieldStrip(Session s, double height) => From(s, s.Find<TabControl>(PlaygroundView.Names.InspectorTabs), height);

        /// <summary>The top of the window: the scenario bar, the field and the Markdown beside it, with room for the selection bar over the first line.</summary>
        static Rect Top(double height) => new Rect(0, 0, W, height);

        static void Select(Session s, int paragraph, string from, string to)
        {
            var text = s.ParagraphText(paragraph);
            int a = text.IndexOf(from, StringComparison.Ordinal), b = text.IndexOf(to, a, StringComparison.Ordinal) + to.Length;
            Assert.True(a >= 0 && b > a, from);
            s.Select(paragraph, a, b);
        }

        [ScreenshotFact]
        public void Bidi_selection() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi, W, H, developerMode: false))
            {
                // From the Latin "means" across the Arabic "مرحبا بالعالم" to "hello": the selection changes direction twice.
                Select(s, 0, "means book", "hello");
                Save(s, "bidi-selection", Top(330));
            }
        });

        [ScreenshotFact]
        public void Selection_bar_with_more() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi, W, H, developerMode: false))
            {
                // The last paragraph, so the two rows have room above it; More opens the second row.
                Select(s, 3, "expression", "française");
                Headless.Pump();
                s.FindLogical<global::Avalonia.Controls.Primitives.ToggleButton>(PlaygroundView.Names.BarMore).IsChecked = true;
                Save(s, "selection-bar", Top(330));
            }
        });

        [ScreenshotFact]
        public void Indic_and_thai_shaping() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.IndicThai, W, H, developerMode: false))
            {
                // One Devanagari conjunct (kshatriya) selected: the selection covers whole clusters.
                Select(s, 0, "क्षत्रिय", "क्षत्रिय");
                Save(s, "indic-thai-shaping", Top(345));
            }
        });

        [ScreenshotFact]
        public void Nfd_and_surrogates() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Unicode, W, H, developerMode: false))
            {
                s.View.SelectionBarEnabled = false;   // the strip is two lines high: the bar would cover the first
                Select(s, 1, "Surrogates","\U0001F600");
                // Only the first two lines: the private-use line below them has no font to draw it, and shows a missing-glyph box.
                var strip = FieldStrip(s, 62);
                Save(s, "nfd-surrogates", new Rect(0, strip.Y, 760, 62));
            }
        });

        [ScreenshotFact]
        public void Inline_pictures() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Picture, W, H, developerMode: false))
            {
                s.Select(0, 3, 36);
                Save(s, "inline-pictures", Top(345));
            }
        });

        [ScreenshotFact]
        public void Unknown_properties_in_the_inspector() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Opaque, W, H))
            {
                var text = s.ParagraphText(1);
                int at = text.IndexOf("unknown", StringComparison.Ordinal);
                s.Select(1, at, at + 7);
                // The model tab: the run dump shows the property numbers the editor carries without understanding them.
                s.View.ShowsMarkdown = false;
                Save(s, "unknown-properties-inspector", FieldStrip(s, 345));
            }
        });

        [ScreenshotFact]
        public void Environment_with_language_service() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment, W, H, developerMode: false))
            {
                s.Editor.SetCaret(new TextPosition(0, 0));
                s.Editor.SetCaret(new TextPosition(0, s.ParagraphText(0).Length), true);
                s.Type("/ [X] _ (a ,");
                s.Press(Key.End);
                Headless.Pump();
                Save(s, "environment-box", FieldStrip(s, 200));
            }
        });

        [ScreenshotFact]
        public void Environment_context_menu() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Environment, W, H, developerMode: false))
            {
                s.Editor.SetCaret(new TextPosition(0, 0));
                s.Editor.SetCaret(new TextPosition(0, s.ParagraphText(0).Length), true);
                s.Type("/ (a _ ,");
                Headless.Pump();
                var at = s.PointAt(0, 2);
                s.RightClick(at);
                Save(s, "environment-fix-menu", FieldStrip(s, 365));
                s.View.CloseContextMenu();
            }
        });

        [ScreenshotFact]
        public void Spelling_squiggles_and_menu() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Spelling, W, H, developerMode: false))
            {
                Headless.Pump();
                Save(s, "spelling-squiggles", FieldStrip(s, 345));
                var at = s.PointAt(0, s.ParagraphText(0).IndexOf("jumpd", StringComparison.Ordinal) + 2);
                s.RightClick(at);
                Save(s, "spelling-context-menu", FieldStrip(s, 345));
                s.View.CloseContextMenu();
            }
        });

        [ScreenshotFact]
        public void Block_features() => Headless.Run(async () =>
        {
            using (var s = new Session(Scenarios.Empty, W, H))
            {
                // Pasted in the clipboard's own interchange form, which keeps each paragraph's style. (Tabs, the cell separator, are not typed or pasted into a field yet, so the table is one column.)
                Paragraph P(string style, string text)
                {
                    var p = new Paragraph { Style = style, DefaultWs = "en" };
                    p.Runs.Add(new Run { Ws = "en", Text = text });
                    return p;
                }
                var paragraphs = new[]
                {
                    P("Heading 1", "Block features"),
                    P("Normal", "A heading, a list and a table are paragraphs with a style the host names; the three switches turn each kind on or off."),
                    P("List Bullet", "Headings, lists and tables are all paragraph styles"),
                    P("List Bullet", "Switching a kind off hides its styles and flattens a paste"),
                    P("Table Header", "Style the switch controls"),
                    P("Table Row", "Heading 1 (Headings)"),
                    P("Table Row", "List Bullet, List Number and others (Lists)"),
                    P("Table Row", "Table Header and Table Row (Tables)"),
                };
                await global::SIL.TextEditing.Avalonia.ClipboardCompat.SetAsync(TopLevel.GetTopLevel(s.Field), "x",
                    new[] { new System.Collections.Generic.KeyValuePair<string, object>(Interchange.ClipboardFormat, Interchange.ToXml(paragraphs)) });
                s.Caret(0, 0);
                s.Click(PlaygroundView.Names.Paste);
                await s.WaitClipboard();
                s.Caret(7, s.ParagraphText(7).Length);
                Save(s, "block-features", From(s, s.Find<CheckBox>(PlaygroundView.Names.AllowTables), 330));
            }
        });

        [ScreenshotFact]
        public void Long_document_and_many_fields() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.Long, W, H, developerMode: false))
            {
                s.Caret(120, 10);
                Save(s, "long-document", FieldStrip(s, 420));
            }
            using (var s = new Session(Scenarios.ManyFields, W, H, developerMode: false))
                Save(s, "many-fields", FieldStrip(s, 440));
        });

        [ScreenshotFact]
        public void Copy_as_submenu() => Headless.Run(() =>
        {
            using (var s = new Session(Scenarios.MixedBidi, W, H, developerMode: false))
            {
                Select(s, 0, "The word", "book");
                s.RightClick(s.PointAt(0, 4));
                var menu = s.Window.GetVisualDescendantsOf<MenuItem>().FirstOrDefault(m => m.Header as string == PlaygroundView.CopyAsCaption);
                Assert.NotNull(menu);
                menu!.IsSubMenuOpen = true;
                Headless.Pump();
                Save(s, "copy-as-submenu", FieldStrip(s, 345));
                s.View.CloseContextMenu();
            }
        });
    }

    static class VisualExtensions
    {
        public static System.Collections.Generic.IEnumerable<T> GetVisualDescendantsOf<T>(this Control root) where T : Control =>
            global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
    }
}
