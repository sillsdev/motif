using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;

namespace SIL.TextEditing.Avalonia
{
    sealed class RunTextProperties : TextRunProperties
    {
        readonly Typeface _typeface;
        readonly double _size;
        readonly TextDecorationCollection? _decorations;
        readonly IBrush _foreground;
        readonly CultureInfo _culture;
        readonly FontFeatureCollection? _features;
        readonly IBrush? _background;
        readonly BaselineAlignment _baseline;
        readonly IBrush? _squiggle;
        readonly double _offset;

        public RunTextProperties(Typeface typeface, double size, IBrush foreground, TextDecorationCollection? decorations, CultureInfo culture, FontFeatureCollection? features, IBrush? background = null, BaselineAlignment baseline = BaselineAlignment.Baseline, IBrush? squiggle = null, double offset = 0, int lineKind = 0, IBrush? lineBrush = null, bool lineFontThickness = false)
        {
            LineKind = lineKind;
            LineBrush = lineBrush;
            LineFontThickness = lineFontThickness;
            _squiggle = squiggle;
            _offset = offset;
            _background = background;
            _baseline = baseline;
            _typeface = typeface;
            _size = size;
            _foreground = foreground;
            _decorations = decorations;
            _culture = culture;
            _features = features;
        }

        public override Typeface Typeface => _typeface;
        public override double FontRenderingEmSize => _size;
        public override TextDecorationCollection? TextDecorations => _decorations;
        public override IBrush? ForegroundBrush => _foreground;
        /// <summary>The run's background colour. Avalonia is not given it (it would paint over the selection): the layout paints it itself, below the selection.</summary>
        public IBrush? Back => _background;
        /// <summary>The colour of the run's squiggle underline, or null for none. Avalonia has no wavy decoration: the layout draws it itself, after the text.</summary>
        public IBrush? Squiggle => _squiggle;
        /// <summary>How far the run's baseline is raised above the line's (negative: lowered), in pixels. Avalonia is not given it: the layout shifts the run when it draws it.</summary>
        public double Offset => _offset;
        /// <summary>The run's underline or strikethrough as a FieldWorks underline type (1 dotted, 2 dashed, 3 single, 4 double, 5 strikethrough), 0 for none
        /// (the squiggle is <see cref="Squiggle"/>). The decoration itself is in <see cref="TextDecorations"/> unless the paragraph draws its own
        /// (<see cref="ParagraphLayout.DrawDecorations"/>), when that is null.</summary>
        public int LineKind { get; }
        /// <summary>The colour of the decoration (the under colour, else the text's).</summary>
        public IBrush? LineBrush { get; }
        /// <summary>True for a single underline in the font's own thickness (one with no under colour set); the others are a pixel thick.</summary>
        public bool LineFontThickness { get; }
        public override IBrush? BackgroundBrush => null;
        public override BaselineAlignment BaselineAlignment => _baseline;
        public override CultureInfo? CultureInfo => _culture;
        public override FontFeatureCollection? FontFeatures => _features;
    }

    sealed class ParagraphProperties : TextParagraphProperties
    {
        readonly FlowDirection _flowDirection;
        readonly TextRunProperties _default;

        readonly TextAlignment _alignment;

        public ParagraphProperties(FlowDirection flowDirection, TextRunProperties defaultProperties, TextAlignment alignment = TextAlignment.Start)
        {
            _flowDirection = flowDirection;
            _default = defaultProperties;
            _alignment = alignment;
        }

        public override FlowDirection FlowDirection => _flowDirection;
        public override TextAlignment TextAlignment => _alignment;
        public override double LineHeight => double.NaN;
        public override bool FirstLineInParagraph => true;
        public override TextRunProperties DefaultTextRunProperties => _default;
        public override TextWrapping TextWrapping => TextWrapping.Wrap;
        public override double Indent => 0;
    }

    /// <summary>An inline object (an ORC) drawn as an image box; length 1 like the U+FFFC it stands for. It carries the
    /// run properties so the formatter can resolve a bidi level for it (Avalonia issue 21792 concerns text-less
    /// drawable runs in right-to-left paragraphs; see <see cref="ParagraphLayout.Build"/>).</summary>
    sealed class ObjectRun : DrawableTextRun
    {
        readonly IImage? _image;
        readonly Size _size;
        readonly TextRunProperties _properties;

        public ObjectRun(IImage? image, Size size, TextRunProperties properties, int sourceIndex)
        {
            _image = image;
            _size = size;
            _properties = properties;
            SourceIndex = sourceIndex;
        }

        /// <summary>Where the object's character is in the paragraph text.</summary>
        public int SourceIndex { get; }

        public override Size Size => _size;
        public override double Baseline => _size.Height * 0.85;
        public override TextRunProperties Properties => _properties;

        public override void Draw(DrawingContext context, Point origin)
        {
            var r = new Rect(origin, _size);
            if (_image != null) context.DrawImage(_image, r);
            else context.DrawRectangle(Brushes.Gray, null, r);
        }
    }

    sealed class ParagraphSource : ITextSource
    {
        public struct Segment
        {
            public int Start, End;
            public TextRunProperties Properties;
            public bool IsObject;
            public string? Data;
        }

        public string Text = string.Empty;
        public List<Segment> Segments = new List<Segment>();
        /// <summary>Test seam: when set, <see cref="GetTextRun"/> looks segments up in this list instead of <see cref="Segments"/>
        /// (the same segments), so a test can count the reads.</summary>
        internal IReadOnlyList<Segment>? SegmentLookup;
        public Func<string, IImage?>? ObjectImage;
        public bool ObjectAsGlyph;

        /// <summary>The index of the first segment that ends after <paramref name="textSourceIndex"/>, or the count when none does.</summary>
        internal static int SegmentAt(IReadOnlyList<Segment> segments, int textSourceIndex)
        {
            // Segments are contiguous and in order, so the first one that ends after the index is the one that holds it. The
            // formatter asks once per segment, so a scan from the start would make a paragraph of many runs quadratic.
            int lo = 0, hi = segments.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (segments[mid].End > textSourceIndex) hi = mid; else lo = mid + 1;
            }
            return lo;
        }

        public TextRun? GetTextRun(int textSourceIndex)
        {
            if (textSourceIndex >= Text.Length) return null;
            { var probe = ParagraphLayout.Probe; if (probe != null) probe.RunsRead++; }
            IReadOnlyList<Segment> segments = SegmentLookup ?? Segments;
            int lo = SegmentAt(segments, textSourceIndex);
            if (lo < segments.Count)
            {
                var s = segments[lo];
                if (textSourceIndex < s.Start) return null;
                if (s.IsObject && ObjectAsGlyph)
                    return new TextCharacters(Text.AsMemory(textSourceIndex, 1), new RunTextProperties(s.Properties.Typeface, 28, s.Properties.ForegroundBrush ?? Brushes.Black, null, s.Properties.CultureInfo ?? CultureInfo.InvariantCulture, null));
                if (s.IsObject)
                    return new ObjectRun(s.Data == null ? null : ObjectImage?.Invoke(s.Data), new Size(32, 24), s.Properties, textSourceIndex);
                return new TextCharacters(Text.AsMemory(textSourceIndex, s.End - textSourceIndex), s.Properties);
            }
            return null;
        }
    }

    /// <summary>A paragraph's text source that ends at <see cref="End"/>, so the formatter, which keeps and lays out every run it has been offered up to the end of
    /// the text for each line it makes, is offered a few lines of a paragraph of tens of thousands of runs and not what is left of it (see
    /// <see cref="ParagraphLayout.FormatWindowed"/>).</summary>
    sealed class WindowedSource : ITextSource
    {
        readonly ParagraphSource _paragraph;

        public WindowedSource(ParagraphSource paragraph)
        {
            _paragraph = paragraph;
            End = paragraph.Text.Length;
        }

        /// <summary>The length of the whole paragraph's text.</summary>
        public int Length => _paragraph.Text.Length;

        /// <summary>The index the text ends at, as far as the formatter can tell.</summary>
        public int End { get; set; }

        public TextRun? GetTextRun(int textSourceIndex)
        {
            if (textSourceIndex >= End) return null;
            var run = _paragraph.GetTextRun(textSourceIndex);
            if (run is TextCharacters characters && textSourceIndex + characters.Length > End)
                return new TextCharacters(_paragraph.Text.AsMemory(textSourceIndex, End - textSourceIndex), characters.Properties);
            return run;
        }
    }

    /// <summary>How a justified line is stretched: its interior spaces, in visual order (natural x, from the line's origin), each widened by
    /// <see cref="Extra"/>. The line's text keeps its leading edge fixed: the left one in a left-to-right paragraph, the right one in a
    /// right-to-left one (<see cref="RightAnchor"/>), so the stretched text ends at the other edge of the text area. Avalonia's own line is
    /// never changed; <see cref="Map"/> and <see cref="Unmap"/> convert between its x and the stretched x for the caret, the selection, the
    /// mouse and the drawing.</summary>
    sealed class Justification
    {
        public Justification(List<KeyValuePair<double, double>> spaces, double extra, bool rightAnchor)
        {
            Spaces = spaces;
            Extra = extra;
            RightAnchor = rightAnchor;
        }

        public List<KeyValuePair<double, double>> Spaces { get; }
        public double Extra { get; }
        public bool RightAnchor { get; }

        /// <summary>How much of a space, at each of its edges, is left to the letters beside it when the line is drawn: half of it.</summary>
        public double Guard(int space) => (Spaces[space].Value - Spaces[space].Key) / 2;

        /// <summary>The stretched x of a natural x: each space to the anchor's far side adds <see cref="Extra"/>, and one the x is inside adds its share.</summary>
        public double Map(double x)
        {
            double shift = 0;
            foreach (var space in Spaces)
            {
                double a = space.Key, b = space.Value;
                if (RightAnchor) shift -= Extra * (x <= a ? 1 : x < b ? (b - x) / (b - a) : 0);
                else shift += Extra * (x >= b ? 1 : x > a ? (x - a) / (b - a) : 0);
            }
            return x + shift;
        }

        /// <summary>The natural x of a stretched x.</summary>
        public double Unmap(double x)
        {
            double done = 0;
            if (!RightAnchor)
            {
                foreach (var space in Spaces)
                {
                    double a = space.Key, b = space.Value, width = b - a;
                    if (x < a + done) return x - done;
                    if (x < b + done + Extra) return a + (x - a - done) * width / (width + Extra);
                    done += Extra;
                }
                return x - done;
            }
            for (int i = Spaces.Count - 1; i >= 0; i--)
            {
                double a = Spaces[i].Key, b = Spaces[i].Value, width = b - a;
                if (x > b - done) return x + done;
                if (x > a - done - Extra) return b - (b - done - x) * width / (width + Extra);
                done += Extra;
            }
            return x + done;
        }
    }

    /// <summary>The runs of a paragraph that have a baseline offset: effective text ranges and how far each is raised in pixels (negative: lowered).</summary>
    sealed class OffsetMap
    {
        readonly List<int> _starts = new List<int>(), _ends = new List<int>();
        readonly List<double> _values = new List<double>();
        public bool IsEmpty => _starts.Count == 0;

        public void Add(int start, int end, double offset)
        {
            _starts.Add(start);
            _ends.Add(end);
            _values.Add(offset);
        }

        /// <summary>The offset of the run that holds the index, or 0. The runs are added in text order, so the one that holds it is found by halving.</summary>
        public double At(int index)
        {
            int lo = 0, hi = _starts.Count, steps = 0;   // the first run that ends after the index
            while (lo < hi)
            {
                steps++;
                int mid = (lo + hi) / 2;
                if (_ends[mid] > index) hi = mid; else lo = mid + 1;
            }
            ParagraphLayout.CountRangeProbes(steps);
            return lo < _starts.Count && index >= _starts[lo] ? _values[lo] : 0;
        }
    }

    /// <summary>The work a layout and a draw did, for a test that counts it (<see cref="ParagraphLayout.Probe"/>): nothing counts unless a test started a probe on
    /// the thread.</summary>
    internal sealed class LayoutProbe : IDisposable
    {
        readonly LayoutProbe? _outer;

        LayoutProbe() { _outer = ParagraphLayout.Probe; ParagraphLayout.Probe = this; }

        /// <summary>Starts counting on this thread until the probe is disposed.</summary>
        public static LayoutProbe Start() => new LayoutProbe();

        /// <summary>How many times a line's run slots were worked out.</summary>
        public long RunSlotBuilds;

        /// <summary>How many ranges (a run's offset, background, decoration or squiggle) were looked at.</summary>
        public long RangeProbes;

        /// <summary>How many times a line of paragraph text was formatted (a label, an ellipsis and an empty line are not counted).</summary>
        public long LinesFormatted;

        /// <summary>How many glyphs (and object runs) were handed to the renderer.</summary>
        public long GlyphsDrawn;

        /// <summary>How many times the formatter asked the paragraph's text source for a run.</summary>
        public long RunsRead;

        public void Dispose() => ParagraphLayout.Probe = _outer;
    }

    /// <summary>A run of a line: where it is drawn (x from the line's origin) and the text it holds (<c>Start</c> and <c>Length</c>).</summary>
    readonly struct RunSlot
    {
        public RunSlot(DrawableTextRun run, double x, int start, int length)
        {
            Run = run;
            X = x;
            Start = start;
            Length = length;
            // A glyph run numbers its characters from where its own text began, which is not always where the paragraph's text did: the
            // first of the run's characters is the lowest cluster number it has.
            Glyphs = (run as ShapedTextRun)?.GlyphRun;
            _firstCluster = int.MaxValue;
            if (Glyphs != null)
                foreach (var info in Glyphs.GlyphInfos) _firstCluster = Math.Min(_firstCluster, (int)info.GlyphCluster);
            ClustersAgainstDirection = false;
            if (Glyphs != null && Glyphs.GlyphInfos.Count >= 2)
            {
                var infos = Glyphs.GlyphInfos;
                ClustersAgainstDirection = (infos[0].GlyphCluster < infos[infos.Count - 1].GlyphCluster) != Glyphs.IsLeftToRight;
            }
        }

        readonly int _firstCluster;
        GlyphRun? Glyphs { get; }

        public DrawableTextRun Run { get; }
        public double X { get; }
        public int Start { get; }
        public int Length { get; }
        public double Width => Run.Size.Width;

        /// <summary>True when the run has glyphs, so it can answer <see cref="Distance"/> and <see cref="HitFromDistance"/> (an object run has none).</summary>
        public bool HasGlyphs => Glyphs != null && _firstCluster != int.MaxValue;

        /// <summary>True when the run holds nothing but spaces.</summary>
        public bool IsSpaces
        {
            get
            {
                var text = Run.Text.Span;
                for (int i = 0; i < text.Length; i++)
                    if (text[i] != ' ') return false;
                return text.Length > 0;
            }
        }

        /// <summary>True when the run's own direction is right to left (its characters run from its right edge to its left).</summary>
        public bool RightToLeft => Glyphs != null && !Glyphs.IsLeftToRight;

        /// <summary>The distance from the run's origin to the leading edge of the character at the paragraph text <paramref name="index"/>
        /// (the run's whole width at its end), or, with a <paramref name="trailing"/> length, to the trailing edge of the character after it has been
        /// passed by that many characters; null when the run cannot say.</summary>
        public double? Distance(int index, int trailing = 0)
        {
            if (!HasGlyphs || index < Start || index > Start + Length) return null;
            if (UsesEvenSpacing) return DistanceByCount(Math.Min(Math.Max(index + trailing - Start, 0), Length));
            return Glyphs!.GetDistanceFromCharacterHit(new CharacterHit(_firstCluster + index - Start, trailing));
        }

        /// <summary>The caret hit (in paragraph text offsets) at a distance from the run's origin, or null when the run cannot say. A distance
        /// outside the run is read as the run's nearest edge (Avalonia is not consistent about which character a point on or past an edge is
        /// in).</summary>
        public CharacterHit? HitFromDistance(double distance)
        {
            if (!HasGlyphs) return null;
            double inside = Math.Max(Math.Min(distance, Width - Edge), Math.Min(Edge, Width / 2));
            if (UsesEvenSpacing)
            {
                double each = Width / Length, from = Glyphs!.IsLeftToRight ? inside : Width - inside;
                int k = Math.Min(Math.Max((int)(from / each), 0), Length - 1);
                return new CharacterHit(Start + k, from - k * each >= each / 2 ? 1 : 0);
            }
            var hit = Glyphs!.GetCharacterHitFromDistance(inside, out _);
            return new CharacterHit(hit.FirstCharacterIndex - _firstCluster + Start, hit.TrailingLength);
        }

        const double Edge = 0.01;

        /// <summary>True when the run's glyphs are numbered against the direction it says it has: a left-to-right run whose clusters fall from one glyph
        /// to the next, or a right-to-left one whose rise. Avalonia 12 does this to the spaces a wrapped line ends with after a right-to-left run
        /// (ClusterOrderTests); the run's own distance and hit answers are then wrong.</summary>
        public bool ClustersAgainstDirection { get; }

        /// <summary>True for such a run of spaces, which are all one width and so can be placed by counting them instead.</summary>
        bool UsesEvenSpacing => ClustersAgainstDirection && IsSpaces && Length > 0;

        /// <summary>The distance to the edge before the character that many characters in: the run's own direction decides which side that is.</summary>
        double DistanceByCount(int count) => Glyphs!.IsLeftToRight ? Width * count / Length : Width - Width * count / Length;
    }
    public sealed class LineInfo
    {
        public LineInfo(TextLine line, double y, double x = 0, double height = -1, double baselineInSlot = double.NaN)
            : this(line, y, x, height, baselineInSlot, null)
        {
        }

        internal LineInfo(TextLine line, double y, double x, double height, double baselineInSlot, OffsetMap? offsets, bool rightToLeft = false, List<RunSlot>? slots = null)
        {
            slots ??= ParagraphLayout.RunSlots(line);
            AllSlots = slots;
            Line = line;
            Y = y;
            X = x;
            Height = height < 0 ? line.Height : height;
            _baselineInSlot = baselineInSlot;
            _offsets = offsets;
            _runs = ParagraphLayout.RunOffsets(line, offsets, out var raise, out var lower, slots);
            HasOffsets = _runs != null;
            // A line of several runs is read from the runs as they are drawn (see DistanceOf) when it is a right-to-left paragraph's or has a
            // right-to-left run in it, whatever the paragraph's direction.
            UsesRuns = slots.Count >= 2 && slots.All(slot => slot.HasGlyphs) && (rightToLeft || slots.Any(slot => slot.RightToLeft));
            if (HasOffsets || UsesRuns) _slots = slots;
            // The spaces a wrapped line ends with take the paragraph's direction (they hang at its end), whatever Avalonia 11.3 says of the run
            // they are in; the caret and the mouse read such a run as running that way (see DistanceOf).
            if (UsesRuns && slots[slots.Count - 1] is var tail && tail.Start + tail.Length == line.FirstTextSourceIndex + line.Length
                && tail.IsSpaces && tail.RightToLeft != rightToLeft)
                _hangingSlot = slots.Count - 1;
            Raise = raise;
            Lower = lower;
            Start = line.FirstTextSourceIndex;
            Length = line.Length;
            End = Start + Length;
        }

        /// <summary>The empty line after a hard line break that ends the paragraph. <paramref name="metrics"/> is a formatted line
        /// that only gives its height; it has no text of this paragraph, so nothing but the caret place is asked of it.</summary>
        internal LineInfo(TextLine metrics, double y, double x, double height, int at, double caretX, double baselineInSlot = double.NaN)
        {
            Line = metrics;
            Y = y;
            X = x;
            Height = height;
            Start = at;
            Length = 0;
            End = at;
            IsEmpty = true;
            EmptyCaretX = caretX;
            _baselineInSlot = baselineInSlot;
        }

        /// <summary>The runs of the line as they are drawn (<see cref="ParagraphLayout.RunSlots"/>), made once when the line was.</summary>
        internal List<RunSlot> AllSlots { get; } = new List<RunSlot>();

        /// <summary>True for the empty last line a trailing hard line break makes: it holds no text, only a caret place.</summary>
        public bool IsEmpty { get; }
        /// <summary>Where the caret sits on an empty line, in paragraph coordinates (following the alignment).</summary>
        internal double EmptyCaretX { get; }

        public TextLine Line { get; }
        /// <summary>The top of the line's slot in paragraph coordinates; a line height rule can make the slot taller (or shorter)
        /// than the text, which sits at the bottom of it.</summary>
        public double Y { get; }
        /// <summary>The left edge of the line's available width in paragraph coordinates; the line's own alignment offset is inside it. A wrapped
        /// line that is aligned by its text and not by the spaces it ends with has the difference moved into it (<see cref="Move"/>).</summary>
        public double X { get; private set; }

        /// <summary>Moves the line's origin sideways by <paramref name="dx"/>, the caret, the selection, the mouse and the drawing with it.</summary>
        internal void Move(double dx) => X += dx;
        /// <summary>The height of the slot.</summary>
        public double Height { get; }
        /// <summary>Where the text of the line starts vertically: at the bottom of the slot, or, under an exact line height, wherever puts the
        /// baseline at the same fraction of the slot as the text's own (the text then overflows the slot both ways when it is taller).</summary>
        public double TextY => double.IsNaN(_baselineInSlot) ? Y + Height - Lower - Line.Height : Y + _baselineInSlot - Line.Baseline;
        readonly double _baselineInSlot;
        /// <summary>True when a run on the line has a baseline offset, so the line is drawn run by run (<see cref="Draw"/>).</summary>
        public bool HasOffsets { get; }
        /// <summary>True when the caret, the mouse and the selection on this line are read from its runs and not from Avalonia's line. Avalonia 11.3
        /// and 12 both give the wrong place for a left-to-right run on a right-to-left paragraph's line of two or more runs, and for a line of two or
        /// more runs with a right-to-left run in it whatever the paragraph's direction (and throw for some ranges), where the runs themselves, which
        /// are what is drawn, are right. See <see cref="DistanceOf"/>.</summary>
        public bool UsesRuns { get; }

        /// <summary>The slot of the spaces the line ends with that Avalonia gives the opposite direction to the paragraph's, or -1.</summary>
        readonly int _hangingSlot = -1;

        int SlotOf(int offset)
        {
            for (int i = 0; i < _slots!.Count; i++)
                if (offset >= _slots[i].Start && offset < _slots[i].Start + _slots[i].Length) return i;
            return -1;
        }

        /// <summary>The x from the line's origin of a caret hit, as <c>TextLine.GetDistanceFromCharacterHit</c> says it, but on a line that
        /// <see cref="UsesRuns"/> from the run the character is in.</summary>
        internal double DistanceOf(CharacterHit hit)
        {
            if (!UsesRuns) return Line.GetDistanceFromCharacterHit(hit);
            int offset = hit.FirstCharacterIndex;
            int k = SlotOf(offset);
            if (hit.TrailingLength == 0 && k < 0) { k = SlotOf(offset - 1); }   // after the last character: the end of the run it is in
            if (k >= 0 && _slots![k].Distance(offset, hit.TrailingLength) is double d) return _slots[k].X + (k == _hangingSlot ? _slots[k].Width - d : d);
            return Line.GetDistanceFromCharacterHit(hit);
        }

        /// <summary>The caret hit at an x from the line's origin, as <c>TextLine.GetCharacterHitFromDistance</c> says it, but on a line that
        /// <see cref="UsesRuns"/> from the run the point is over.</summary>
        internal CharacterHit HitAt(double x)
        {
            if (!UsesRuns) return Line.GetCharacterHitFromDistance(x);
            int k = 0;
            while (k < _slots!.Count - 1 && x >= _slots[k].X + _slots[k].Width) k++;
            double within = x - _slots[k].X;
            return _slots[k].HitFromDistance(k == _hangingSlot ? _slots[k].Width - within : within) ?? Line.GetCharacterHitFromDistance(x);
        }
        readonly OffsetMap? _offsets;
        readonly List<RunSlot>? _slots;
        /// <summary>The caret stops a click can be moved to when the control draws carets away from where the layout has them (<see cref="ParagraphLayout.HitTestDetailed"/>); null until asked for.</summary>
        internal List<ParagraphLayout.Stop>? DisplacedStops;
        Justification? _justification;

        /// <summary>True when the line is stretched to the edges of the text area (a justified paragraph's line that is not its last).</summary>
        public bool IsJustified => _justification != null;
        internal void Justify(Justification justification) => _justification = justification;
        internal Justification? Stretch => _justification;

        /// <summary>The x, from the line's origin, where Avalonia's natural x ends up on the line as shown.</summary>
        internal double Map(double x) => _justification == null ? x : _justification.Map(x);

        /// <summary>The natural x for an x from the line's origin on the line as shown.</summary>
        internal double Unmap(double x) => _justification == null ? x : _justification.Unmap(x);
        /// <summary>Every run of a line that has shifted runs, in drawing order, with its offset; null for a line with none.</summary>
        internal List<KeyValuePair<DrawableTextRun, double>>? _runs;
        /// <summary>How far the shifted runs reach above the text's own top, and below its bottom: room the slot makes for them.</summary>
        public double Raise { get; }
        public double Lower { get; }

        /// <summary>Draws the line with its text at <paramref name="top"/> plus <see cref="TextY"/>. A line with no shifted run is Avalonia's own
        /// drawing; otherwise each run is drawn where Avalonia would draw it, less the run's offset.</summary>
        public void Draw(DrawingContext context, double top)
        {
            if (_justification == null) { DrawNatural(context, top); return; }
            // A justified line is drawn run by run, each run in pieces: the glyphs between two spaces are one piece, moved to where the stretched text puts it.
            // Each glyph is drawn once, whatever the number of spaces. The stretched middle of a space is blank: a justified paragraph draws its own
            // underlines and strikethrough across the stretched text (see ParagraphLayout.DrawDecorations).
            _pieces ??= MakePieces(_justification);
            double y = top + TextY;
            foreach (var piece in _pieces)
            {
                { var p = ParagraphLayout.Probe; if (p != null) p.GlyphsDrawn += piece.Count; }
                if (piece.Glyphs == null) { piece.Run!.Draw(context, new Point(piece.X, y + piece.Y)); continue; }
                using (context.PushTransform(Matrix.CreateTranslation(piece.X, y + piece.Y)))
                using (context.PushClip(new Rect(piece.Left - piece.X, -2500, piece.Right - piece.Left, 5000)))
                    context.DrawGlyphRun(piece.Brush, piece.Glyphs);
            }
        }

        /// <summary>A part of a justified line drawn on its own: a run of glyphs, or an object, at an x from the control and a y from the line's text top.</summary>
        sealed class Piece
        {
            public GlyphRun? Glyphs;
            public DrawableTextRun? Run;
            public IBrush? Brush;
            public double X, Y, Left, Right;
            public int Count;
        }

        List<Piece>? _pieces;

        List<Piece> MakePieces(Justification j)
        {
            var pieces = new List<Piece>();
            foreach (var slot in AllSlots)
            {
                double runY = ParagraphLayout.RunTop(Line, slot.Run) - (_offsets?.At(slot.Start) ?? 0);
                var shaped = slot.Run as ShapedTextRun;
                var glyphs = shaped?.GlyphRun;
                if (glyphs == null || glyphs.GlyphInfos.Count == 0)
                {
                    double centre = slot.X + slot.Width / 2;
                    pieces.Add(new Piece { Run = slot.Run, X = X + slot.X + j.Map(centre) - centre, Y = runY, Count = 1 });
                    continue;
                }
                var infos = glyphs.GlyphInfos;
                double at = slot.X, before = 0, pieceShift = 0, pieceStart = 0;   // where the glyph starts (the line's own x), the run's advance up to it, and the open piece's shift and start
                int from = 0;
                for (int g = 0; g <= infos.Count; g++)
                {
                    double advance = g < infos.Count ? infos[g].GlyphAdvance : 0;
                    double shift = g < infos.Count ? j.Map(at + advance / 2) - (at + advance / 2) : 0;
                    if (g == from && g < infos.Count) pieceShift = shift;
                    else if (g > from && (g == infos.Count || Math.Abs(shift - pieceShift) > 0.01))
                    {
                        var infoPiece = new List<GlyphInfo>(g - from);
                        // The piece keeps the characters its glyphs stand for, numbered from its own first: a run numbers them from where it began in the paragraph.
                        int lo = int.MaxValue, hi = 0, first = int.MaxValue;
                        for (int i = 0; i < infos.Count; i++) first = Math.Min(first, infos[i].GlyphCluster);
                        for (int i = from; i < g; i++) { lo = Math.Min(lo, infos[i].GlyphCluster); hi = Math.Max(hi, infos[i].GlyphCluster); }
                        int end = first + glyphs.Characters.Length;
                        for (int i = 0; i < infos.Count; i++) if (infos[i].GlyphCluster > hi) end = Math.Min(end, infos[i].GlyphCluster);
                        for (int i = from; i < g; i++) infoPiece.Add(new GlyphInfo(infos[i].GlyphIndex, infos[i].GlyphCluster - lo, infos[i].GlyphAdvance, infos[i].GlyphOffset));
                        var chars = glyphs.Characters.Slice(lo - first, Math.Max(0, end - lo));
                        var bounds = PieceBounds(j, pieceShift);
                        pieces.Add(new Piece
                        {
                            Glyphs = new GlyphRun(glyphs.GlyphTypeface, glyphs.FontRenderingEmSize, chars, infoPiece, glyphs.BaselineOrigin, glyphs.BiDiLevel),
                            Brush = shaped!.Properties?.ForegroundBrush,
                            X = X + slot.X + pieceStart + pieceShift, Y = runY, Left = bounds.Left, Right = bounds.Right, Count = g - from,
                        });
                        from = g;
                        pieceStart = before;
                        pieceShift = shift;
                    }
                    at += advance;
                    before += advance;
                }
            }
            return pieces;
        }

        /// <summary>The x range, from the line's origin, that the words between two spaces are drawn in: from the middle of the space before them to the middle of the
        /// space after, as stretched. A letter's ink can reach past its advance (the hook of a "j", italics), into the space beside it; it stays with its letter up
        /// to the middle of the space, which is as far as the space's own width reaches, and is not smeared across the stretched gap. Ink of a letter beyond that
        /// is not drawn (a hook longer than half a space loses its tip: a pixel or two of an italic "j"), where the line drawn whole and cut at each space drew
        /// it at the wrong place, apart from its letter; <c>JustifiedDrawTests</c> pins both.</summary>
        (double Left, double Right) PieceBounds(Justification j, double shift)
        {
            const double Far = 5000;
            int n = j.Spaces.Count;
            int k = j.RightAnchor ? n - (int)Math.Round(-shift / j.Extra) : (int)Math.Round(shift / j.Extra);
            k = Math.Max(0, Math.Min(n, k));
            double left = k > 0 ? X + (j.Spaces[k - 1].Key + j.Spaces[k - 1].Value) / 2 + shift : -Far;
            double right = k < n ? X + (j.Spaces[k].Key + j.Spaces[k].Value) / 2 + shift : Far;
            return (left, right);
        }

        internal void DisposePieces()
        {
            if (_pieces == null) return;
            foreach (var piece in _pieces) piece.Glyphs?.Dispose();
            _pieces = null;
        }

        void DrawNatural(DrawingContext context, double top)
        {
            double y = top + TextY;
            var probe = ParagraphLayout.Probe;
            if (probe != null) foreach (var slot in AllSlots) probe.GlyphsDrawn += (slot.Run as ShapedTextRun)?.GlyphRun.GlyphInfos.Count ?? 1;
            if (!HasOffsets) { Line.Draw(context, new Point(X, y)); return; }
            double x = X + Line.Start;
            foreach (var run in _runs!)
            {
                run.Key.Draw(context, new Point(x, y + ParagraphLayout.RunTop(Line, run.Key) - run.Value));
                x += run.Key.Size.Width;
            }
        }

        /// <summary>A box from Avalonia's line as the line is shown: moved by the line's origin and stretched with it.</summary>
        Rect Shown(Rect r)
        {
            double left = X + Map(r.X), right = X + Map(r.Right);
            return new Rect(left, r.Y, right - left, r.Height);
        }

        /// <summary>The boxes of the characters from <paramref name="from"/> to <paramref name="to"/> (effective offsets), one per run piece, with
        /// each piece's baseline offset: x in paragraph coordinates, y from the top of the line's text (before the offset is applied).</summary>
        internal List<KeyValuePair<Rect, double>> Pieces(int from, int to)
        {
            var result = new List<KeyValuePair<Rect, double>>();
            if (_slots != null && (HasOffsets || UsesRuns))
            {
                // A line with shifted runs is read run by run, from the runs as they are drawn (see ParagraphLayout.RunSlots).
                foreach (var slot in _slots)
                {
                    int a = Math.Max(from, slot.Start), b = Math.Min(to, slot.Start + slot.Length);
                    if (b <= a) continue;
                    double left = 0, right = slot.Width;
                    if (a > slot.Start || b < slot.Start + slot.Length)
                    {
                        double? p = slot.Distance(a), q = slot.Distance(b);
                        if (p.HasValue && q.HasValue)
                        {
                            left = Math.Min(p.Value, q.Value);
                            right = Math.Max(p.Value, q.Value);
                        }
                    }
                    result.Add(new KeyValuePair<Rect, double>(Shown(new Rect(slot.X + left, 0, right - left, Line.Height)), _offsets?.At(slot.Start) ?? 0));
                }
                return result;
            }
            try
            {
                foreach (var tb in Line.GetTextBounds(from, to - from))
                    result.Add(new KeyValuePair<Rect, double>(Shown(tb.Rectangle), 0));
            }
            catch (InvalidOperationException)
            {
                // Avalonia 12 refuses a range that covers no width ("Covered length must be greater than zero"), such as a right-to-left mark alone
                // before the spaces a line ends with: a range with no width has no box.
                result.Clear();
            }
            return result;
        }
        public int Start { get; }
        public int Length { get; }
        public int End { get; }
    }

    /// <summary>The formats of one layout of a paragraph, each worked out once: the host's styles are asked once for each style and writing system, and
    /// runs that format alike share one set of text run properties (typeface, font features, culture, brushes) and so cost one allocation of them.</summary>
    sealed class FormatCache
    {
        readonly WritingSystemRegistry _writingSystems;
        readonly IBrush _foreground;
        readonly IBrush? _forceText;
        readonly Dictionary<(CharFormat, string, bool), RunTextProperties> _properties = new Dictionary<(CharFormat, string, bool), RunTextProperties>(new KeyComparer());
        readonly Dictionary<(string?, string?), FontFeatureCollection?> _features = new Dictionary<(string?, string?), FontFeatureCollection?>();

        sealed class KeyComparer : IEqualityComparer<(CharFormat, string, bool)>
        {
            public bool Equals((CharFormat, string, bool) x, (CharFormat, string, bool) y) => x.Item2 == y.Item2 && x.Item3 == y.Item3 && x.Item1.SameAs(y.Item1);
            public int GetHashCode((CharFormat, string, bool) k) => k.Item1.SameHash() * 31 + k.Item2.GetHashCode() + (k.Item3 ? 1 : 0);
        }

        public FormatCache(Paragraph paragraph, WritingSystemRegistry writingSystems, IBrush foreground, IStyleResolver? styles, IBrush? forceText)
        {
            Formats = new FormatResolver.CharFormats(paragraph, styles);
            _writingSystems = writingSystems;
            _foreground = foreground;
            _forceText = forceText;
        }

        public FormatResolver.CharFormats Formats { get; }

        /// <summary>How many sets of text run properties were made (the runs that format alike share one).</summary>
        public int Built { get; private set; }

        /// <summary>The paragraph style's character format in the writing system of a run (the run's own properties not applied).</summary>
        public CharFormat Char(Run run) => Formats.Resolve(run, _writingSystems.Get(run.Ws).Size);

        public RunTextProperties For(Run run, bool preedit = false)
        {
            var ws = _writingSystems.Get(run.Ws);
            var f = Formats.Resolve(run, ws.Size);
            return For(f, ws, run.Ws, preedit);
        }

        public RunTextProperties For(CharFormat f, WritingSystemInfo ws, string wsTag, bool preedit = false)
        {
            var key = (f, wsTag, preedit);
            if (_properties.TryGetValue(key, out var found)) return found;
            Built++;
            return _properties[key] = ParagraphLayout.MakeProperties(f, ws, wsTag, _foreground, preedit, true, _forceText, this);
        }

        public FontFeatureCollection? Features(string? ws, string? run)
        {
            if (!_features.TryGetValue((ws, run), out var found)) _features[(ws, run)] = found = ParseFeatures(ws, run);
            return found;
        }

        internal static FontFeatureCollection? ParseFeatures(string? ws, string? run)
        {
            FontFeatureCollection? features = null;
            foreach (var list in new[] { ws, run })
            {
                if (string.IsNullOrEmpty(list)) continue;
                foreach (var item in list!.Split(','))
                {
                    if (item.Trim().Length == 0) continue;
                    features ??= new FontFeatureCollection();
                    features.Add(FontFeature.Parse(item.Trim()));
                }
            }
            return features;
        }
    }

    /// <summary>The layout of one paragraph with TextFormatter: lines, hit testing, caret, selection and visual caret
    /// stops. Offsets are model offsets unless a name says "effective"; the two differ only while an IME preedit
    /// string is shown inside the paragraph.</summary>
    public sealed class ParagraphLayout : IDisposable
    {
        FormatCache? _formats;

        /// <summary>How many sets of text run properties this layout made: runs that format alike share one.</summary>
        internal int FormatsBuilt => _formats?.Built ?? 0;

        /// <summary>How many times the host's styles were asked for a style while this layout was made.</summary>
        internal int StyleLookups => _formats?.Formats.StyleLookups ?? 0;

        ParagraphLayout(Paragraph paragraph, double layoutWidth)
        {
            Paragraph = paragraph;
            LayoutWidth = layoutWidth;
        }

        public Paragraph Paragraph { get; }
        public List<LineInfo> Lines { get; } = new List<LineInfo>();
        public double Width { get; private set; }
        public double Height { get; private set; }
        public double LayoutWidth { get; }
        public string EffectiveText { get; private set; } = string.Empty;
        public int PreeditAt { get; private set; } = -1;
        public int PreeditLength { get; private set; }
        public bool Rtl => Paragraph.Rtl;

        /// <summary>The paragraph's resolved formatting (alignment, indents, spacing, borders, numbering).</summary>
        public ParaFormat Format { get; private set; } = new ParaFormat();
        /// <summary>The paragraph box (inside the margins, border included): its left edge, top and width in paragraph coordinates.</summary>
        Rect Box { get; set; }
        /// <summary>The left edge of the text area, and its width.</summary>
        public double ContentLeft { get; private set; }
        public double ContentWidth { get; private set; }
        /// <summary>The bullet or number text this layout was built for (empty for none); the field rebuilds the layout when the number changes.</summary>
        public string LabelText { get; private set; } = string.Empty;
        /// <summary>The space after the paragraph above (0 for the first), which this layout was built against: the gap between two
        /// paragraphs is the larger of the margin top and space after plus space before, not their sum.</summary>
        public double PreviousSpaceAfter { get; private set; }
        /// <summary>True when lines past the style's maximum were left out.</summary>
        public bool Truncated { get; private set; }

        /// <summary>True when the paragraph ends in a hard line break whose empty last line the line limit leaves out. No text is
        /// hidden (so <see cref="Truncated"/> stays false), but the caret cannot sit after the break.</summary>
        public bool HiddenTrailingBreak { get; private set; }

        /// <summary>The last model offset a caret can reach: the end of the text, or the end of the last line shown when the line
        /// limit hid the rest (or the empty line after a trailing break). As in FieldWorks, a caret never sits in hidden text.</summary>
        public int VisibleEnd => !(Truncated || HiddenTrailingBreak) || Lines.Count == 0 ? Paragraph.Length : Math.Min(Paragraph.Length, ToModel(TextEnd(Lines[Lines.Count - 1])));
        double _borderLeft, _borderTop, _borderRight, _borderBottom;
        IBrush? _fill, _borderBrush;
        CharFormat? _labelFormat;
        WritingSystemInfo? _labelWs;
        string _labelWsTag = string.Empty;
        IBrush? _labelForeground;
        string? _labelText;
        TextLine? _label;
        RunTextProperties? _labelRun;

        /// <param name="ownDecorations">True when the paragraph draws the run's underline or strikethrough itself (<see cref="DrawDecorations"/>), so Avalonia is not given it.</param>
        /// <param name="forceText">A brush that is the text's colour whatever colour the run has, or null.</param>
        internal static RunTextProperties MakeProperties(CharFormat f, WritingSystemInfo ws, string wsTag, IBrush foreground, bool preedit = false, bool ownDecorations = false, IBrush? forceText = null, FormatCache? cache = null)
        {
            var weight = (FontWeight)f.Weight;
            var style = f.Italic ? FontStyle.Italic : FontStyle.Normal;
            double size = f.SizePt > 0 ? f.SizePt : ws.Size;
            var typeface = new Typeface(new FontFamily(f.FontFamily ?? ws.Font), style, weight);
            double scriptOffset = 0;
            if (f.Superscript != 0)
            {
                // The font's own size and offset for superscripts and subscripts, from its OS/2 table; FieldWorks' two thirds where it gives none.
                var place = ScriptPlacement.For(typeface, f.Superscript == 1);
                scriptOffset = (f.Superscript == 1 ? 1 : -1) * place.OffsetScale * size;
                size *= place.SizeScale;
            }
            FontFeatureCollection? features = cache != null ? cache.Features(ws.Features, f.FontFeatures) : FormatCache.ParseFeatures(ws.Features, f.FontFeatures);
            CultureInfo culture;
            try { culture = CultureInfo.GetCultureInfo(wsTag); }
            catch (CultureNotFoundException) { culture = CultureInfo.InvariantCulture; }
            IBrush text = forceText ?? ColorBrush(f.ForeColor) ?? foreground;
            int lineKind = preedit ? 3 : f.Underline >= 1 && f.Underline <= 5 ? f.Underline : 0;
            var lineBrush = lineKind == 0 ? null : forceText ?? (preedit ? null : ColorBrush(f.UnderColor)) ?? text;
            var decorations = ownDecorations ? null : preedit ? Decorations(3, null, text) : Decorations(f.Underline, f.UnderColor, text);
            // The raise or drop is the run's baseline offset (the layout draws the run shifted and makes room for it), on top of the offset the run has of its own.
            return new RunTextProperties(typeface, size, text, decorations, culture, features, ColorBrush(f.BackColor), BaselineAlignment.Baseline, !preedit && f.Underline == 6 ? ColorBrush(f.UnderColor) ?? text : null, (preedit ? 0 : f.OffsetPt) + scriptOffset, lineKind, lineBrush, lineKind == 3 && (preedit || ColorBrush(f.UnderColor) == null));
        }

        /// <summary>Where a run's top is below the top of its line, as Avalonia draws it: baselines aligned, a superscript at the top, a subscript at the bottom.</summary>
        internal static double RunTop(TextLine line, DrawableTextRun run)
        {
            switch (run.Properties?.BaselineAlignment)
            {
                case BaselineAlignment.Superscript: return 0;
                case BaselineAlignment.Subscript: return line.Height - run.Size.Height;
                default: return line.Baseline - run.Baseline;
            }
        }

        /// <summary>The work counts a test asks for, on this thread; null (the default, and always in a host) counts nothing. Thread-local, so a field on
        /// another dispatcher never writes a test's counts and the counts need no locking.</summary>
        [ThreadStatic] static LayoutProbe? _probe;

        internal static LayoutProbe? Probe { get => _probe; set => _probe = value; }

        internal static void CountRangeProbes(int n) { var p = _probe; if (p != null) p.RangeProbes += n; }

        /// <summary>Where each run of a line is drawn and which text it holds, in drawing order. Avalonia's own numbering of a line's runs (the
        /// text run bounds and the character hits) is wrong for a right-to-left paragraph's line with two or more left-to-right runs and a wrapped
        /// space: it takes the runs' visual order for their logical one. So a run's text is read from the run itself: the slice of the paragraph text
        /// it was shaped from.</summary>
        internal static List<RunSlot> RunSlots(TextLine line)
        {
            { var p = _probe; if (p != null) p.RunSlotBuilds++; }
            var result = new List<RunSlot>();
            double x = line.Start;
            foreach (var run in line.TextRuns)
            {
                if (!(run is DrawableTextRun d)) continue;
                int start;
                if (d is ObjectRun o) start = o.SourceIndex;
                else if (!(d.Text.Length > 0 && System.Runtime.InteropServices.MemoryMarshal.TryGetString(d.Text, out _, out start, out _)))
                {
                    // Every run the formatter returns is an object run or a slice of the paragraph text, which the source hands it as a slice of
                    // one string (RunSlotTests): a run with no place in the text would be one Avalonia made itself, and has no text to find.
                    x += d.Size.Width;
                    continue;
                }
                result.Add(new RunSlot(d, x, start, run.Length));
                x += d.Size.Width;
            }
            return result;
        }
        /// <summary>The runs of a line with their baseline offsets, or null when the line has no shifted run (<paramref name="raise"/> and
        /// <paramref name="lower"/> are then 0). Otherwise they are how far the shifted runs reach above the line's top and below its bottom.
        /// Avalonia 11.3 hands back its own copy of each run's properties (Avalonia 12 the original), so on both a run is matched to its offset by the text it holds (<see cref="RunSlots"/>), not through its properties.</summary>
        internal static List<KeyValuePair<DrawableTextRun, double>>? RunOffsets(TextLine line, OffsetMap? offsets, out double raise, out double lower, List<RunSlot>? slots = null)
        {
            raise = 0;
            lower = 0;
            if (offsets == null || offsets.IsEmpty || line.Length <= 0) return null;
            var result = new List<KeyValuePair<DrawableTextRun, double>>();
            bool any = false;
            foreach (var slot in slots ?? RunSlots(line))
            {
                var d = slot.Run;
                double offset = offsets.At(slot.Start);
                result.Add(new KeyValuePair<DrawableTextRun, double>(d, offset));
                if (offset == 0) continue;
                any = true;
                double top = RunTop(line, d) - offset;
                raise = Math.Max(raise, -top);
                lower = Math.Max(lower, top + d.Size.Height - line.Height);
            }
            return any ? result : null;
        }
        /// <summary>The paragraph's resolved formatting, sized from the writing system of its first run.</summary>
        internal static ParaFormat ResolveFormat(Paragraph paragraph, WritingSystemRegistry writingSystems, IStyleResolver? styles)
        {
            string ws = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Ws : paragraph.DefaultWs;
            return FormatResolver.ResolveParagraph(paragraph, styles, writingSystems.Get(ws).Size);
        }

        /// <summary>A FieldWorks BGR colour as a brush, or null for none or a special value.</summary>
        internal static IBrush? ColorBrush(int? bgr)
        {
            if (bgr == null || !CharFormat.TryColor(bgr.Value, out var r, out var g, out var b)) return null;
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        /// <summary>The decorations of a FieldWorks underline type (1 dotted, 2 dashed, 3 single, 4 double, 5 strikethrough);
        /// 0 and the squiggle (6), which Avalonia cannot draw and the layout draws itself (<see cref="DrawSquiggles"/>), give none.</summary>
        static TextDecorationCollection? Decorations(int type, int? color, IBrush text)
        {
            if (type < 1 || type > 5) return null;
            var colour = ColorBrush(color);
            if (type == 3 && colour == null) return new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Underline } };
            var stroke = colour ?? text;
            TextDecoration Line(TextDecorationLocation where, double offset = 0, params double[] dashes)
            {
                var d = new TextDecoration { Location = where, Stroke = stroke, StrokeThickness = 1, StrokeThicknessUnit = TextDecorationUnit.Pixel, StrokeOffsetUnit = TextDecorationUnit.Pixel, StrokeOffset = offset };
                if (dashes.Length > 0) d.StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double>(dashes);
                return d;
            }
            var result = new TextDecorationCollection();
            switch (type)
            {
                case 1: result.Add(Line(TextDecorationLocation.Underline, 0, 2, 2)); break;
                case 2: result.Add(Line(TextDecorationLocation.Underline, 0, 6, 3)); break;
                case 3: result.Add(Line(TextDecorationLocation.Underline)); break;
                case 4: result.Add(Line(TextDecorationLocation.Underline)); result.Add(Line(TextDecorationLocation.Underline, 2)); break;
                case 5: result.Add(Line(TextDecorationLocation.Strikethrough)); break;
            }
            return result;
        }

        /// <summary>
        /// Lays out a paragraph. <paramref name="objectAsGlyph"/> is the workaround for Avalonia issue 21792 (a text-less
        /// DrawableTextRun in a right-to-left paragraph gets the wrong visual order in 11.3 and 12.1): the object is
        /// laid out as an EM SPACE sized to the picture and the picture is painted over its text bounds.
        /// </summary>
        public static ParagraphLayout Build(Paragraph paragraph, double width, WritingSystemRegistry writingSystems, Func<string, IImage?>? objectImage, IBrush foreground, bool objectAsGlyph = false, int preeditAt = -1, string? preedit = null, Run? preeditTemplate = null, IStyleResolver? styles = null, string? label = null, double previousSpaceAfter = 0, IBrush? forceText = null)
        {
            // Avalonia's formatter throws for some sequences it cannot split a line inside (11.3: "Cannot split: requested length N consumes entire run", for a
            // zero width joiner after a space before Arabic text, and for a space followed by a combining mark or a variation selector in a paragraph with Arabic or
            // Hebrew in it). Typed text must not crash the layout, so a paragraph the formatter refuses is laid out again with the offending characters swapped,
            // one for one, for ones that are drawn the same and break nowhere (the model and every offset are untouched), and each time with more of them, down
            // to every format character and mark. If even that is refused, the formatter's own exception is the one thrown.
            Exception? first = null;
            for (int swap = 0; ; swap++)
            {
                try { return BuildWith(swap, paragraph, width, writingSystems, objectImage, foreground, objectAsGlyph, preeditAt, preedit, preeditTemplate, styles, label, previousSpaceAfter, forceText); }
                catch (FormatterRefused e)
                {
                    first ??= e.InnerException;
                    if (swap < FormatSwaps - 1) continue;
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(first!).Throw();
                    throw;
                }
            }
        }

        /// <summary>How many layouts of a paragraph are tried: as it is, with the zero width joiners swapped, with the other format characters swapped, with the glyphless characters after a space swapped and a space before a mark made a no-break
        /// space, the same with a dotted circle, with every format character and mark swapped.</summary>
        const int FormatSwaps = 6;

        /// <summary>The formatter refused a line (<see cref="InvalidOperationException"/> from Avalonia's text formatter).</summary>
        sealed class FormatterRefused : Exception
        {
            public FormatterRefused(Exception inner) : base(inner.Message, inner) { }
        }

        /// <summary>A paragraph with at least this many runs (after runs that format alike are one) is formatted from windows of its text
        /// (<see cref="FormatWindowed"/>), if it is Latin text. Settable for a test that compares the two.</summary>
        internal static int WindowedFormatSegments = 16000;

        /// <summary>Whether a text is Latin script, its marks and its punctuation and nothing else, so a window of it is shaped as the whole of it is: nothing in it can
        /// change the order or the direction of what is round it (Avalonia resolves the directions of a paragraph's text once for all of it, and a window would
        /// resolve them for each line), and nothing in it falls back to another font, which a neutral character at a line start would keep in the whole text and
        /// lose in a window (CJK, Thai and the rest are formatted whole).</summary>
        internal static bool IsWindowable(string text)
        {
            foreach (char c in text)
            {
                if (c <= 'ɏ' || (c >= '̀' && c <= 'ͯ') || (c >= '₠' && c <= '⃏')) continue;
                if (c >= ' ' && c <= '⁯' && !(c >= '‎' && c <= '‏') && !(c >= '‪' && c <= '‮') && !(c >= '⁦' && c <= '⁩')) continue;
                return false;
            }
            return true;
        }

        /// <summary>The characters of lookahead a line needs past its end in a window, before it is trusted to be the line the whole paragraph would give.</summary>
        const int WindowLookahead = 32;

        /// <summary>Formats the line at <paramref name="index"/> from a source that ends a little past the line, and not from all that is left of the
        /// paragraph. Avalonia's formatter keeps what it has read of the text, up to its end, in the break of every line it makes and goes through it again for the next, which
        /// for a paragraph of tens of thousands of runs is most of what a layout costs (it grows with the square of the runs). The window is
        /// about twice as long as the line before; a line that is not at least <see cref="WindowLookahead"/> characters short of the window's end is
        /// made again from a window twice as long, so a break is always decided with text after it, and with the rest of its own word. Each line is
        /// formatted on its own, without the previous line's break, which carries only the runs it left over: the window starts at the line.
        /// For Latin text (<see cref="IsWindowable"/>) the lines are those the whole text gives, to the kerning of a pair across a line start (a fraction of a pixel).</summary>
        static TextLine? FormatWindowed(TextFormatter formatter, WindowedSource window, int index, double width, TextParagraphProperties properties, int previousLength)
        {
            int total = window.Length;
            int span = Math.Max(2 * WindowLookahead, 2 * previousLength + WindowLookahead);
            while (true)
            {
                window.End = (int)Math.Min((long)total, (long)index + span);
                var line = FormatLineGuarded(formatter, window, index, width, properties, null);
                if (line == null || window.End >= total || line.Length + WindowLookahead <= window.End - index) return line;
                line.Dispose();
                span *= 2;
            }
        }

        /// <summary>Formats a short line of its own (a label, an ellipsis, a space to measure a height by): the line, or, when the formatter refuses it, the line
        /// of its text with every format character and mark swapped (<see cref="SwapFormatCharacters"/>), or null.</summary>
        static TextLine? FormatShort(ParagraphSource source, double width, TextParagraphProperties properties)
        {
            try { return TextFormatter.Current.FormatLine(source, 0, width, properties, null); }
            catch (InvalidOperationException) { }
            string text = source.Text;
            source.Text = SwapFormatCharacters(text, 5);
            try { return TextFormatter.Current.FormatLine(source, 0, width, properties, null); }
            catch (InvalidOperationException) { return null; }
            finally { source.Text = text; }
        }

        static TextLine? FormatLineGuarded(TextFormatter formatter, ITextSource source, int index, double width, TextParagraphProperties properties, TextLineBreak? previous)
        {
            try { return formatter.FormatLine(source, index, width, properties, previous); }
            catch (InvalidOperationException e) { throw new FormatterRefused(e); }
        }

        /// <summary>The text the formatter is given, with characters swapped, one for one, so every offset stays. Swap 1: every zero width joiner for a word joiner
        /// (U+2060). Swap 2: also every zero width non-joiner and bidirectional format character. Swap 3: also every character with no glyph of its own (a variation
        /// selector, a format character) that follows a space, for a word joiner, and a space before a mark for a no-break space, so the mark stays on a base the
        /// formatter does not split it from, and is drawn (a mark on a no-break space is how a lone diacritic is shown). Swap 4: the same with a dotted circle
        /// (U+25CC) for the space, where the no-break space is refused. Swap 5: every format character and every mark for a word joiner.</summary>
        internal static string SwapFormatCharacters(string text, int swap)
        {
            if (swap == 0) return text;
            char[]? chars = null;
            void Put(int at, char c)
            {
                if (chars == null) chars = text.ToCharArray();
                chars[at] = c;
            }
            bool afterSpace = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                bool hit = c == '\u200D';
                if (!hit && swap >= 2)
                    hit = c == '\u200C' || (c >= '\u200E' && c <= '\u200F') || (c >= '\u202A' && c <= '\u202E') || (c >= '\u2066' && c <= '\u2069') || c == '\u061C';
                if (!hit && swap >= 3)
                    hit = swap >= 5 ? IsMark(text, i, true) : afterSpace && IsMark(text, i, false);
                if (hit)
                {
                    Put(i, '\u2060');
                    afterSpace = true;   // what follows a swapped character is treated as what follows a space
                }
                else afterSpace = c == ' ' || (afterSpace && char.IsLowSurrogate(c));
            }
            if (swap == 3 || swap == 4)
            {
                // A space that a mark with a glyph follows (the swapped characters between them, which are word joiners now, left out).
                for (int i = 0; i < text.Length; i++)
                {
                    if (text[i] != ' ') continue;
                    int j = i + 1;
                    while (j < text.Length && (chars == null ? text[j] : chars[j]) == '\u2060') j++;
                    if (j < text.Length && IsMark(text, j, true) && !IsInvisible(text, j)) Put(i, swap == 3 ? '\u00A0' : '\u25CC');
                }
            }
            return chars == null ? text : new string(chars);
        }

        /// <summary>Whether the UTF-16 unit at <paramref name="i"/> is (half of) a mark, a variation selector or another format character; with
        /// <paramref name="visible"/> false only those that have no glyph of their own are meant.</summary>
        static bool IsMark(string text, int i, bool visible)
        {
            if (!MarkOrFormat(text, i)) return false;
            return visible || IsInvisible(text, i);
        }

        /// <summary>Whether a mark or format character has no glyph of its own: a variation selector, the combining grapheme joiner, a format character or a tag.</summary>
        static bool IsInvisible(string text, int i)
        {
            char c = text[i];
            if (char.IsSurrogate(c))
            {
                int start = char.IsHighSurrogate(c) ? i : i - 1;
                if (start < 0 || start + 1 >= text.Length || !char.IsHighSurrogate(text[start]) || !char.IsLowSurrogate(text[start + 1])) return false;
                int cp = char.ConvertToUtf32(text[start], text[start + 1]);
                return (cp >= 0xE0000 && cp <= 0xE0FFF) || CharUnicodeInfo.GetUnicodeCategory(text, start) == UnicodeCategory.Format;
            }
            return (c >= '\uFE00' && c <= '\uFE0F') || c == '\u034F' || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format;
        }

        static bool MarkOrFormat(string text, int i)
        {
            char c = text[i];
            if (char.IsSurrogate(c))
            {
                int start = char.IsHighSurrogate(c) ? i : i - 1;
                if (start < 0 || start + 1 >= text.Length || !char.IsHighSurrogate(text[start]) || !char.IsLowSurrogate(text[start + 1])) return false;
                int cp = char.ConvertToUtf32(text[start], text[start + 1]);
                if (cp >= 0xE0000 && cp <= 0xE0FFF) return true;   // tags and the variation selectors supplement
                var pair = CharUnicodeInfo.GetUnicodeCategory(text, start);
                return pair == UnicodeCategory.NonSpacingMark || pair == UnicodeCategory.EnclosingMark || pair == UnicodeCategory.Format;
            }
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            return category == UnicodeCategory.NonSpacingMark || category == UnicodeCategory.EnclosingMark || category == UnicodeCategory.Format || (c >= '\uFE00' && c <= '\uFE0F') || c == '\u034F';
        }

        static ParagraphLayout BuildWith(int swap, Paragraph paragraph, double width, WritingSystemRegistry writingSystems, Func<string, IImage?>? objectImage, IBrush foreground, bool objectAsGlyph, int preeditAt, string? preedit, Run? preeditTemplate, IStyleResolver? styles, string? label, double previousSpaceAfter, IBrush? forceText)
        {
            var layout = new ParagraphLayout(paragraph, width);
            if (forceText == null)
            {
                layout._recolour = brush => Build(paragraph, width, writingSystems, objectImage, foreground, objectAsGlyph, preeditAt, preedit, preeditTemplate, styles, label, previousSpaceAfter, brush);
                layout._builtFrom = paragraph.ChangeStamp;
            }
            var pf = ResolveFormat(paragraph, writingSystems, styles);
            var formats = layout._formats = new FormatCache(paragraph, writingSystems, foreground, styles, forceText);
            // Every paragraph draws its own underlines and strikethrough (DrawDecorations): Avalonia's leave a gap at each descender, and in a justified
            // paragraph, which is drawn in pieces, they would restart their pattern in each.
            layout.PreviousSpaceAfter = previousSpaceAfter;
            layout.LabelText = label ?? string.Empty;
            var source = new ParagraphSource { ObjectImage = objectImage, ObjectAsGlyph = objectAsGlyph };
            var sb = new StringBuilder();
            int pos = 0;
            bool hasPreedit = !string.IsNullOrEmpty(preedit);
            string firstWs = paragraph.Runs.Count > 0 ? paragraph.Runs[0].Ws : paragraph.DefaultWs;
            bool preeditDone = !hasPreedit;
            var preeditRun = preeditTemplate ?? new Run { Ws = firstWs };

            void AddSegment(string t, Run r)
            {
                if (t.Length == 0) return;
                int a = sb.Length;
                sb.Append(t);
                var properties = formats.For(r);
                int last = source.Segments.Count - 1;
                if (last >= 0 && !source.Segments[last].IsObject && ReferenceEquals(source.Segments[last].Properties, properties))
                {
                    // Runs that format alike (they differ in a property that is not drawn) are one stretch of text to the formatter: shaped and laid out once.
                    var joined = source.Segments[last];
                    joined.End = sb.Length;
                    source.Segments[last] = joined;
                    return;
                }
                source.Segments.Add(new ParagraphSource.Segment { Start = a, End = sb.Length, Properties = properties });
            }

            void AddPreedit()
            {
                int a = sb.Length;
                sb.Append(preedit);
                source.Segments.Add(new ParagraphSource.Segment { Start = a, End = sb.Length, Properties = formats.For(preeditRun, true) });
                preeditDone = true;
            }

            foreach (var r in paragraph.Runs)
            {
                if (!preeditDone && preeditAt == pos) AddPreedit();
                if (!preeditDone && !r.IsObject && preeditAt > pos && preeditAt < pos + r.Text.Length)
                {
                    int k = preeditAt - pos;
                    AddSegment(r.Text.Substring(0, k), r);
                    AddPreedit();
                    AddSegment(r.Text.Substring(k), r);
                }
                else if (r.IsObject)
                {
                    int s0 = sb.Length;
                    sb.Append(objectAsGlyph ? " " : r.Text);
                    source.Segments.Add(new ParagraphSource.Segment { Start = s0, End = sb.Length, Properties = formats.For(r), IsObject = true, Data = r.ObjData });
                }
                else AddSegment(r.Text, r);
                pos += r.Text.Length;
            }
            if (!preeditDone) AddPreedit();
            layout.PreeditAt = hasPreedit ? preeditAt : -1;
            layout.PreeditLength = hasPreedit ? preedit!.Length : 0;
            layout.EffectiveText = sb.ToString();   // the real text: grapheme boundaries and spaces are read from it
            source.Text = SwapFormatCharacters(layout.EffectiveText, swap);
            foreach (var seg in source.Segments)
            {
                if (seg.IsObject || !(seg.Properties is RunTextProperties rp)) continue;
                if (rp.Back != null) layout._backgrounds.Add(new KeyValuePair<int, KeyValuePair<int, IBrush>>(seg.Start, new KeyValuePair<int, IBrush>(seg.End, rp.Back)));
                if (rp.Offset != 0) layout._offsets.Add(seg.Start, seg.End, rp.Offset);
                if (rp.LineKind != 0) layout._decorations.Add(new DecorationRange { Start = seg.Start, End = seg.End, Properties = rp });
                if (rp.Squiggle != null) layout._squiggles.Add(new KeyValuePair<int, KeyValuePair<int, IBrush>>(seg.Start, new KeyValuePair<int, IBrush>(seg.End, rp.Squiggle)));
            }

            var defaultRun = paragraph.Runs.Count > 0 ? paragraph.Runs[0] : new Run { Ws = paragraph.DefaultWs };
            var defaultProperties = formats.For(defaultRun);
            var baseWs = writingSystems.Get(defaultRun.Ws);
            layout.Format = pf;
            layout.ApplyBox(pf, width, paragraph.Rtl, foreground);
            if (pf.BulNumScheme != 0)
            {
                // The label takes the paragraph style's character format and the number font, not the first run's own look (its
                // superscript, underline or background).
                var lf = formats.Char(new Run { Ws = defaultRun.Ws });
                if (pf.BulNumFontInfo.Length > 0) lf.ApplyOver(BulletNumbers.ParseFontInfo(pf.BulNumFontInfo));
                if (BulletNumbers.IsBullet(pf.BulNumScheme)) lf.FontFamily = BulletNumbers.BulletFont;
                layout._labelFormat = lf;
                layout._labelWs = baseWs;
                layout._labelWsTag = defaultRun.Ws;
                layout._labelForeground = foreground;
            }
            var paragraphProperties = new ParagraphProperties(paragraph.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, defaultProperties, Alignment(pf.Align));
            double w = Math.Max(layout.ContentWidth, 10);
            double firstIndent = pf.FirstIndent;
            layout.PlaceFirstLine(w, firstIndent, source);
            double lineHeightRule = pf.LineHeight;
            if (source.Text.Length == 0)
            {
                // A line cannot be formatted from zero characters through the public API: a space in the same font gives the height a line
                // of text has, so the paragraph and its caret are as tall as a one-line paragraph's. The font's own metrics are the fallback.
                var glyphTypeface = defaultProperties.Typeface.GlyphTypeface;
                double fontSize = defaultProperties.FontRenderingEmSize;
                double lineHeight = glyphTypeface != null
                    ? (glyphTypeface.Metrics.Descent - glyphTypeface.Metrics.Ascent + glyphTypeface.Metrics.LineGap) / (double)glyphTypeface.Metrics.DesignEmHeight * fontSize
                    : fontSize * 1.3;   // our own fallback for a font with no metrics, not a FieldWorks value
                if (lineHeight <= 0) lineHeight = fontSize * 1.3;
                var spaceSource = new ParagraphSource { Text = " " };
                spaceSource.Segments.Add(new ParagraphSource.Segment { Start = 0, End = 1, Properties = defaultProperties });
                double slot;
                using (var metrics = FormatShort(spaceSource, w, paragraphProperties))
                {
                    bool measured = metrics != null && metrics.Height > 0;
                    if (measured) lineHeight = metrics!.Height;
                    slot = layout.SlotHeight(lineHeight);
                    layout.EmptyLineTop = layout.TopInset + slot - lineHeight;
                    if (measured && pf.ExactLineHeight && pf.LineHeight > 0)
                    {
                        // An exact line height keeps the baseline at the same share of the slot as on a line with text.
                        layout._exactAscentFraction = ExactFraction(metrics!);
                        layout.EmptyLineTop = layout.TopInset + layout.BaselineIn(slot) - metrics!.Baseline;
                    }
                }
                layout.EmptyLineHeight = lineHeight;
                layout.Height = layout.TopInset + slot + layout.BottomInset;
                layout.Width = 0;
                return layout;
            }
            int index = 0;
            double y = layout.TopInset + layout._labelLineHeight;
            TextLineBreak? previous = null;
            var formatter = TextFormatter.Current;
            var window = source.Segments.Count >= WindowedFormatSegments && !paragraph.Rtl && IsWindowable(source.Text) ? new WindowedSource(source) : null;
            int lastLineLength = 0;
            while (index < source.Text.Length || layout.Lines.Count == 0)
            {
                if (pf.MaxLines > 0 && layout.Lines.Count >= pf.MaxLines) { layout.Truncated = true; break; }
                bool first = layout.Lines.Count == 0;
                // The first line starts where PlaceFirstLine put it; the others use the whole text area.
                double lineWidth = first ? layout._firstWidth : w;
                double lineX = first ? layout._firstX : layout.ContentLeft;
                { var p = _probe; if (p != null) p.LinesFormatted++; }
                var line = window != null ? FormatWindowed(formatter, window, index, lineWidth, paragraphProperties, lastLineLength) : FormatLineGuarded(formatter, source, index, lineWidth, paragraphProperties, previous);
                if (line == null) break;
                double usedWidth = lineWidth;
                if (pf.MaxLines > 0 && layout.Lines.Count == pf.MaxLines - 1 && index + line.Length < source.Text.Length)
                {
                    // Text follows the last line the limit allows, so it ends with an ellipsis. When the text area is wide enough for one
                    // beside text, the line is made again narrower to keep room for it (on the left in a right-to-left paragraph).
                    var dots = layout.EllipsisLine(defaultRun, baseWs, styles, foreground);
                    if (dots != null)
                    {
                        layout._ellipsisLineIndex = layout.Lines.Count;
                        double room = dots.WidthIncludingTrailingWhitespace;
                        if (w > 3 * room)
                        {
                            { var p = _probe; if (p != null) p.LinesFormatted++; }
                            var narrower = window != null ? FormatWindowed(formatter, window, index, Math.Max(lineWidth - room, 1), paragraphProperties, lastLineLength) : FormatLineGuarded(formatter, source, index, Math.Max(lineWidth - room, 1), paragraphProperties, previous);
                            if (narrower != null && narrower.Length > 0)
                            {
                                line.Dispose();
                                line = narrower;
                                usedWidth = Math.Max(lineWidth - room, 1);
                                if (layout.Rtl) lineX += room;
                            }
                        }
                    }
                }
                var slots = RunSlots(line);
                double slot = layout.SlotHeight(line.Height + ExtentOf(line, layout._offsets, slots));
                if (first && pf.ExactLineHeight && pf.LineHeight > 0 && line.Height > 0) layout._exactAscentFraction = ExactFraction(line);
                var shown = new LineInfo(line, y, lineX, slot, layout.BaselineIn(slot), layout._offsets, layout.Rtl, slots);
                if (index + line.Length < source.Text.Length && line.NewLineLength == 0) shown.Move(layout.HangingShift(shown, pf.Align, usedWidth));
                layout.Lines.Add(shown);
                if (pf.Align == 5 && index + line.Length < source.Text.Length) layout.Justify(shown, usedWidth);
                y += slot;
                layout.Width = Math.Max(layout.Width, line.WidthIncludingTrailingWhitespace);
                if (line.Length <= 0) break;
                index += line.Length;
                lastLineLength = line.Length;
                previous = line.TextLineBreak;
            }
            if (!layout.Truncated && layout.Lines.Count > 0 && layout.Lines[layout.Lines.Count - 1].End == source.Text.Length && source.Text[source.Text.Length - 1] == HardLineBreak)
            {
                // A paragraph that ends in a hard line break has one more line, empty, and the caret can sit on it. The formatter
                // makes no line from no text, so a space gives the height and the line is never drawn.
                // At the line limit that line would be hidden, but it holds no text: nothing is truncated, and the caret simply stops
                // before the break, so typing there and Delete (which removes the break) still work.
                if (pf.MaxLines > 0 && layout.Lines.Count >= pf.MaxLines) layout.HiddenTrailingBreak = true;
                else
                {
                    var spaceSource = new ParagraphSource { Text = " " };
                    spaceSource.Segments.Add(new ParagraphSource.Segment { Start = 0, End = 1, Properties = defaultProperties });
                    var metrics = FormatShort(spaceSource, w, paragraphProperties);
                    if (metrics != null)
                    {
                        double slot = layout.SlotHeight(metrics.Height);
                        layout.Lines.Add(new LineInfo(metrics, y, layout.ContentLeft, slot, source.Text.Length, layout.AlignedX(layout.ContentLeft, w), layout.BaselineIn(slot)));
                        y += slot;
                    }
                }
            }
            layout.Height = y + layout.BottomInset;
            return layout;
        }

        /// <summary>How far a wrapped line is moved so that its text, and not the spaces it ends with, is what the alignment places in the
        /// <paramref name="available"/> width. Avalonia aligns a line by its width with those spaces: against the right edge in a left-to-right
        /// paragraph (and the left edge in a right-to-left one) the text stops a space width short of it, and a line whose text fits but whose spaces
        /// do not is kept against its leading edge, so its text reaches past the other. Both are put right, as FieldWorks leaves the spaces out of the
        /// width it aligns by. A line whose text alone is wider than the area keeps its leading edge.</summary>
        double HangingShift(LineInfo shown, int align, double available)
        {
            // Avalonia's own Width and trailing-space length are wrong for a line of several runs with right-to-left text (they count spaces that are
            // not trailing), so the text's place is taken from where its characters are drawn, the spaces the line ends with left out.
            int start = shown.Start, end = shown.End, core = end;
            while (core > start && EffectiveText[core - 1] == ' ') core--;
            if (core == end || core == start) return 0;
            double now = double.MaxValue, right = double.MinValue;
            foreach (var piece in shown.Pieces(start, core))
            {
                now = Math.Min(now, piece.Key.X - shown.X);
                right = Math.Max(right, piece.Key.Right - shown.X);
            }
            if (right <= now) return 0;
            double textWidth = right - now;
            bool left = align == 1 || (align == 4 && Rtl) || ((align == 0 || align == 5) && !Rtl);
            double target;
            if (textWidth > available) target = Rtl ? available - textWidth : 0;
            else if (align == 2) target = (available - textWidth) / 2;
            else target = left ? 0 : available - textWidth;
            return target - now;
        }

        /// <summary>The hard line break (Shift+Enter), U+2028: it ends its line, takes no room and is not drawn.</summary>
        const char HardLineBreak = '\u2028';

        // ---- paragraph formatting ----

        double _firstX, _firstWidth, _labelWidth;
        // Runs with a background colour: start, then end and brush (effective offsets).
        readonly List<KeyValuePair<int, KeyValuePair<int, IBrush>>> _backgrounds = new List<KeyValuePair<int, KeyValuePair<int, IBrush>>>();

        /// <summary>Paints the runs' background colours behind the text; <paramref name="top"/> is the paragraph's top in the control.
        /// The field paints the selection after this, so a selected run stays visibly selected.</summary>
        public void DrawBackgrounds(DrawingContext context, double top)
        {
            if (_backgrounds.Count == 0) return;
            foreach (var li in Lines)
                for (int i = FirstEndingAfter(_backgrounds, li.Start); i < _backgrounds.Count && _backgrounds[i].Key < li.End; i++)
                {
                    var bg = _backgrounds[i];
                    CountRangeProbes(1);
                    // The spaces a wrapped line ends with hang outside its text and are not painted (the paragraph's last line is all text).
                    int a = Math.Max(bg.Key, li.Start), b = Math.Min(bg.Value.Key, li == Lines[Lines.Count - 1] ? TextEnd(li) : TextBeforeTrailingSpaces(li));
                    if (b <= a) continue;
                    foreach (var piece in li.Pieces(a, b))
                        context.DrawRectangle(bg.Value.Value, null, new Rect(piece.Key.X, top + li.TextY + piece.Key.Y - piece.Value, piece.Key.Width, piece.Key.Height));
                }
        }

        readonly OffsetMap _offsets = new OffsetMap();

        /// <summary>The width of a line's text up to <paramref name="last"/> (the end of its last letter), without the spaces it wraps on.
        /// Avalonia 11.3 also leaves out a space at the end of one of the line's runs that is not the last, so the line's own width is
        /// not trusted when the runs' widths less those spaces come to more.</summary>
        double NaturalWidth(LineInfo li, int last)
        {
            double total = 0, trailing = 0;
            int to = TextEnd(li);
            foreach (var slot in li.AllSlots)
            {
                total += slot.Width;
                int a = Math.Max(last, slot.Start), b = Math.Min(to, slot.Start + slot.Length);
                if (b <= a) continue;
                if (a == slot.Start && b == slot.Start + slot.Length) trailing += slot.Width;
                else if (slot.Distance(a) is double p && slot.Distance(b) is double q)
                    trailing += Math.Abs(q - p);
            }
            return Math.Max(li.Line.Width, total - trailing);
        }

        /// <summary>Stretches a line of a justified paragraph (one that more text follows) to <paramref name="width"/>: the slack is shared equally
        /// by the spaces between its first and last letters. A line with no such space, or no slack, stays as Avalonia laid it out.</summary>
        void Justify(LineInfo li, double width)
        {
            int first = li.Start, last = TextEnd(li);
            while (first < last && EffectiveText[first] == ' ') first++;
            while (last > first && EffectiveText[last - 1] == ' ') last--;
            double slack = width - NaturalWidth(li, last);
            if (slack < 0.5) return;
            var spaces = new List<KeyValuePair<double, double>>();
            for (int i = first; i < last; i++)
            {
                if (EffectiveText[i] != ' ') continue;
                double left = double.MaxValue, right = double.MinValue;
                if (li.UsesRuns)
                    foreach (var piece in li.Pieces(i, i + 1))
                    {
                        left = Math.Min(left, piece.Key.X - li.X);
                        right = Math.Max(right, piece.Key.Right - li.X);
                    }
                else
                {
                    try
                    {
                        foreach (var tb in li.Line.GetTextBounds(i, 1))
                        {
                            left = Math.Min(left, tb.Rectangle.X);
                            right = Math.Max(right, tb.Rectangle.Right);
                        }
                    }
                    catch (InvalidOperationException)
                    {
                        // Avalonia 12 refuses a range that covers no width ("Covered length must be greater than zero"), such as a space an Arabic letter mark
                        // takes the width of: it is no gap to stretch.
                        left = double.MaxValue;
                        right = double.MinValue;
                    }
                }
                if (right - left > 0.01) spaces.Add(new KeyValuePair<double, double>(left, right));
            }
            if (spaces.Count == 0) return;
            spaces.Sort((p, q) => p.Key.CompareTo(q.Key));
            li.Justify(new Justification(spaces, slack / spaces.Count, Rtl));
        }

        /// <summary>The room a line needs for its shifted runs, above and below its own height.</summary>
        static double ExtentOf(TextLine line, OffsetMap offsets, List<RunSlot> slots)
        {
            RunOffsets(line, offsets, out var raise, out var lower, slots);
            return raise + lower;
        }

        /// <summary>A run's underline or strikethrough that the paragraph draws itself: its text range (effective offsets) and how it looks.</summary>
        sealed class DecorationRange
        {
            public int Start, End;
            public RunTextProperties Properties = null!;
        }

        readonly List<DecorationRange> _decorations = new List<DecorationRange>();

        /// <summary>How the paragraph draws the underline or strikethrough of the text at an effective offset (<see cref="RunTextProperties.LineKind"/>
        /// and its colour), or null when there is none there.</summary>
        internal RunTextProperties? DecorationAt(int effective)
        {
            for (int i = FirstDecorationEndingAfter(effective); i < _decorations.Count && _decorations[i].Start <= effective; i++)
                if (effective >= _decorations[i].Start && effective < _decorations[i].End) return _decorations[i].Properties;
            return null;
        }

        int FirstDecorationEndingAfter(int offset)
        {
            int lo = 0, hi = _decorations.Count;
            while (lo < hi)
            {
                CountRangeProbes(1);
                int mid = (lo + hi) / 2;
                if (_decorations[mid].End > offset) hi = mid; else lo = mid + 1;
            }
            return lo;
        }

        /// <summary>The index of the first range of the list (in text order, not overlapping) that ends after the offset, or the count when none does.</summary>
        static int FirstEndingAfter(List<KeyValuePair<int, KeyValuePair<int, IBrush>>> ranges, int offset)
        {
            int lo = 0, hi = ranges.Count;
            while (lo < hi)
            {
                CountRangeProbes(1);
                int mid = (lo + hi) / 2;
                if (ranges[mid].Value.Key > offset) hi = mid; else lo = mid + 1;
            }
            return lo;
        }

        /// <summary>Where a run's baseline is from the top of its line: the line's, unless the run is a superscript (top aligned) or subscript (bottom
        /// aligned), whose own font decides.</summary>
        static double RunBaseline(TextLine line, RunTextProperties run)
        {
            var metrics = run.Typeface.GlyphTypeface?.Metrics;
            if (run.BaselineAlignment == BaselineAlignment.Baseline || metrics == null || metrics.Value.DesignEmHeight <= 0) return line.Baseline;
            double scale = run.FontRenderingEmSize / metrics.Value.DesignEmHeight;
            double ascent = (-metrics.Value.Ascent + metrics.Value.LineGap / 2.0) * scale, height = (metrics.Value.Descent - metrics.Value.Ascent + metrics.Value.LineGap) * scale;
            return run.BaselineAlignment == BaselineAlignment.Superscript ? ascent : line.Height - height + ascent;
        }

        /// <summary>Draws the underlines and strikethrough of the paragraph's text (they are not given to Avalonia, whose underline leaves a gap at each
        /// descender and which would draw them in
        /// the pieces a justified line is drawn in, restarting a dotted or dashed pattern at each and scaling it in the stretched spaces). Each is a line
        /// (two for a double underline) across the whole extent of its run on the line, unbroken by the letters' descenders, in the position and pattern Avalonia would draw it in on a line that
        /// is not stretched; it stops before the spaces a wrapped line ends with. <paramref name="top"/> is the paragraph's top in the control.</summary>
        public void DrawDecorations(DrawingContext context, double top)
        {
            if (_decorations.Count == 0) return;
            for (int i = 0; i < Lines.Count; i++)
            {
                var li = Lines[i];
                if (li.IsEmpty) continue;
                int end = i == Lines.Count - 1 ? TextEnd(li) : TextBeforeTrailingSpaces(li);
                for (int d = FirstDecorationEndingAfter(li.Start); d < _decorations.Count && _decorations[d].Start < li.End; d++)
                {
                    var decoration = _decorations[d];
                    CountRangeProbes(1);
                    int a = Math.Max(decoration.Start, li.Start), b = Math.Min(decoration.End, end);
                    if (b <= a) continue;
                    // Boxes that touch are one line, so a pattern runs on across them.
                    var pieces = li.Pieces(a, b).OrderBy(p => p.Key.X).ToList();
                    for (int k = 0; k < pieces.Count; k++)
                    {
                        double left = pieces[k].Key.X, right = pieces[k].Key.Right, offset = pieces[k].Value;
                        while (k + 1 < pieces.Count && pieces[k + 1].Value == offset && pieces[k + 1].Key.X <= right + 0.75) right = Math.Max(right, pieces[++k].Key.Right);
                        if (right - left < 0.5) continue;
                        DrawDecorationLine(context, decoration.Properties, left, right, top + li.TextY + RunBaseline(li.Line, decoration.Properties) - offset);
                    }
                }
            }
        }

        static void DrawDecorationLine(DrawingContext context, RunTextProperties run, double left, double right, double baseline)
        {
            var metrics = run.Typeface.GlyphTypeface?.Metrics;
            if (metrics == null || metrics.Value.DesignEmHeight <= 0 || run.LineBrush == null) return;
            double scale = run.FontRenderingEmSize / metrics.Value.DesignEmHeight;
            double y = baseline + (run.LineKind == 5 ? metrics.Value.StrikethroughPosition : metrics.Value.UnderlinePosition) * scale;
            double thickness = run.LineFontThickness ? Math.Max(metrics.Value.UnderlineThickness * scale, 0.5) : 1;
            DashStyle? dashes = run.LineKind == 1 ? new DashStyle(new double[] { 2, 2 }, 0) : run.LineKind == 2 ? new DashStyle(new double[] { 6, 3 }, 0) : null;
            var pen = new Pen(run.LineBrush, thickness, dashes);
            context.DrawLine(pen, new Point(left, y), new Point(right, y));
            if (run.LineKind == 4) context.DrawLine(pen, new Point(left, y + 2), new Point(right, y + 2));
        }

        // Runs with a squiggle underline: start, then end and brush (effective offsets).
        readonly List<KeyValuePair<int, KeyValuePair<int, IBrush>>> _squiggles = new List<KeyValuePair<int, KeyValuePair<int, IBrush>>>();

        /// <summary>Draws the squiggle underlines (underline type 6, spelling marks): a zigzag a little below the baseline under each
        /// run's text on every line it is on, in its under colour. Drawn after the text; <paramref name="top"/> is the paragraph's top in the control.</summary>
        public void DrawSquiggles(DrawingContext context, double top)
        {
            if (_squiggles.Count == 0 && _spelling.Count == 0 && _diagnostics.Count == 0) return;
            foreach (var li in Lines)
            {
                if (li.IsEmpty) continue;
                foreach (var sq in RangesOn(li))
                {
                    int a = Math.Max(sq.Key, li.Start), b = Math.Min(sq.Value.Key, TextEnd(li));
                    if (b <= a) continue;
                    double descent = Math.Max(li.Line.Height - li.Line.Baseline, 3);
                    double amplitude = Math.Min(2, Math.Max(1, descent / 2));
                    var pen = new Pen(sq.Value.Value, 1);
                    foreach (var piece in li.Pieces(a, b))
                    {
                        double middle = top + li.TextY + li.Line.Baseline + 1 + amplitude - piece.Value;
                        double x0 = piece.Key.X, x1 = piece.Key.Right;
                        if (x1 - x0 < 1) continue;
                        var wave = new StreamGeometry();
                        using (var c = wave.Open())
                        {
                            bool down = false;
                            c.BeginFigure(new Point(x0, middle - amplitude), false);
                            for (double x = x0 + 2; x < x1; x += 2)
                            {
                                c.LineTo(new Point(x, middle + (down ? -amplitude : amplitude)));
                                down = !down;
                            }
                            c.LineTo(new Point(x1, middle + (down ? -amplitude : amplitude)));
                            c.EndFigure(false);
                        }
                        context.DrawGeometry(null, pen, wave);
                    }
                }
            }
        }

        // Spelling marks (ADR 0020): start, then end and brush (effective offsets), drawn with the squiggles.
        readonly List<KeyValuePair<int, KeyValuePair<int, IBrush>>> _spelling = new List<KeyValuePair<int, KeyValuePair<int, IBrush>>>();

        /// <summary>Sets the spelling marks drawn under the text, in model offsets: red for a misspelled or embedded word, blue for a word the
        /// dictionary cannot judge. Replaces the earlier marks; null or empty clears them. A mark that an input method's text sits inside is
        /// not drawn, and no mark is drawn under the composing text.</summary>
        /// <summary>The squiggles and spelling marks that touch a line: the lists are in text order and do not overlap, so they are searched by halving (a list
        /// that is not, because a host's marks overlap, is read whole).</summary>
        IEnumerable<KeyValuePair<int, KeyValuePair<int, IBrush>>> RangesOn(LineInfo li)
        {
            foreach (var list in new[] { _squiggles, _spelling, _diagnostics })
            {
                bool ordered = ReferenceEquals(list, _squiggles) || (ReferenceEquals(list, _spelling) ? _spellingOrdered : _diagnosticsOrdered);
                for (int i = ordered ? FirstEndingAfter(list, li.Start) : 0; i < list.Count; i++)
                {
                    if (ordered && list[i].Key >= li.End) break;
                    CountRangeProbes(1);
                    yield return list[i];
                }
            }
        }

        bool _spellingOrdered = true;

        // Diagnostics of the host's language service (ADR 0030), drawn with the spelling marks in the same way: start, then end and brush (effective offsets).
        // They are a view of the text, kept here and never in the paragraph's runs.
        readonly List<KeyValuePair<int, KeyValuePair<int, IBrush>>> _diagnostics = new List<KeyValuePair<int, KeyValuePair<int, IBrush>>>();
        bool _diagnosticsOrdered = true;

        /// <summary>Sets the diagnostic squiggles drawn under the text, in model offsets, with a brush each; they may overlap. Replaces the earlier ones; null or empty
        /// clears them. As for spelling marks, one that an input method's text sits inside is not drawn.</summary>
        internal void SetDiagnosticMarks(IReadOnlyList<(int Start, int End, IBrush Brush)>? marks)
        {
            _diagnostics.Clear();
            _diagnosticsOrdered = true;
            if (marks == null) return;
            foreach (var m in marks)
            {
                if (PreeditAt >= 0 && m.Start < PreeditAt && m.End > PreeditAt) continue;
                int start = PreeditAt >= 0 && m.Start >= PreeditAt ? m.Start + PreeditLength : m.Start;
                var mark = new KeyValuePair<int, KeyValuePair<int, IBrush>>(start, new KeyValuePair<int, IBrush>(ToEffective(m.End), m.Brush));
                if (_diagnostics.Count > 0 && (mark.Key < _diagnostics[_diagnostics.Count - 1].Key || mark.Value.Key < _diagnostics[_diagnostics.Count - 1].Value.Key)) _diagnosticsOrdered = false;
                _diagnostics.Add(mark);
            }
        }

        /// <summary>For a test: the diagnostic squiggles that will be drawn, as effective start and end offsets and brush.</summary>
        internal IReadOnlyList<(int Start, int End, IBrush Brush)> DiagnosticMarksDrawn => _diagnostics.Select(m => (m.Key, m.Value.Key, m.Value.Value)).ToList();

        internal void SetSpellingMarks(IReadOnlyList<SpellingMark>? marks)
        {
            _spelling.Clear();
            _spellingOrdered = true;
            if (marks == null) return;
            foreach (var m in marks)
            {
                if (PreeditAt >= 0 && m.Start < PreeditAt && m.End > PreeditAt) continue;
                // A mark that starts where the composing text is inserted starts after it; one that ends there ends before it.
                int start = PreeditAt >= 0 && m.Start >= PreeditAt ? m.Start + PreeditLength : m.Start;
                var mark = new KeyValuePair<int, KeyValuePair<int, IBrush>>(start, new KeyValuePair<int, IBrush>(ToEffective(m.End), m.Problem == SpellingProblem.Mixed ? Brushes.Blue : Brushes.Red));
                if (_spelling.Count > 0 && (mark.Key < _spelling[_spelling.Count - 1].Key || mark.Value.Key < _spelling[_spelling.Count - 1].Value.Key)) _spellingOrdered = false;
                _spelling.Add(mark);
            }
        }

        /// <summary>For a test: the spelling marks that will be drawn, as effective start and end offsets and brush.</summary>
        internal IReadOnlyList<(int Start, int End, IBrush Brush)> SpellingMarksDrawn => _spelling.Select(m => (m.Key, m.Value.Key, m.Value.Value)).ToList();

        int _ellipsisLineIndex = -1;
        TextLine? _ellipsis;

        /// <summary>How the ellipsis is drawn (its font, size and colour, and the underline the field draws itself under it), once there is one.</summary>
        internal RunTextProperties? EllipsisProperties { get; private set; }

        /// <summary>The "..." drawn after the last visible line of a paragraph the line limit cuts off, in the paragraph style's character format
        /// (not the last run's own look).</summary>
        TextLine? EllipsisLine(Run defaultRun, WritingSystemInfo baseWs, IStyleResolver? styles, IBrush foreground)
        {
            if (_ellipsis != null) return _ellipsis;
            var props = _formats!.For(_formats.Char(new Run { Ws = defaultRun.Ws }), baseWs, defaultRun.Ws);
            EllipsisProperties = props;
            var source = new ParagraphSource { Text = "..." };
            source.Segments.Add(new ParagraphSource.Segment { Start = 0, End = 3, Properties = props });
            _ellipsis = FormatShort(source, 100000, new ParagraphProperties(Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, props, TextAlignment.Left));
            return _ellipsis;
        }

        /// <summary>True when the last visible line ends with an ellipsis: the line limit cut the paragraph off with text still to come (a hard
        /// line break that ends the paragraph is not text).</summary>
        public bool HasEllipsis => _ellipsis != null && _ellipsisLineIndex >= 0 && _ellipsisLineIndex < Lines.Count;

        /// <summary>The ellipsis' box in paragraph coordinates: after the text of the last visible line on the trailing side (right of it, or
        /// left of it in a right-to-left paragraph), on its baseline; empty when there is none.</summary>
        public Rect EllipsisRect
        {
            get
            {
                if (!HasEllipsis) return default;
                var li = Lines[_ellipsisLineIndex];
                double width = _ellipsis!.WidthIncludingTrailingWhitespace;
                double inkLeft = li.X + li.Map(li.Line.Start + (li.Line.WidthIncludingTrailingWhitespace - li.Line.Width));
                double x = Rtl ? inkLeft - width : li.X + li.Map(li.Line.Start + li.Line.Width);
                return new Rect(x, li.TextY + li.Line.Baseline - _ellipsis.Baseline, width, _ellipsis.Height);
            }
        }

        /// <summary>Draws the ellipsis, if the paragraph has one; <paramref name="top"/> is the paragraph's top in the control.</summary>
        public void DrawEllipsis(DrawingContext context, double top)
        {
            if (!HasEllipsis) return;
            var box = EllipsisRect;
            _ellipsis!.Draw(context, new Point(box.X, top + box.Y));
            if (EllipsisProperties is RunTextProperties dots) DrawDecorationLine(context, dots, box.X, box.Right, top + box.Y + _ellipsis.Baseline);
        }

        /// <summary>The extra space a bullet or number needs after it: three points unless it ends with a space.</summary>
        const double LabelGap = 3;

        /// <summary>Works out where the first line's text starts and how wide it may be. The first indent moves it in (or, hanging,
        /// out). A bullet or number sits at the first indent; if the hanging indent is wider than the label and its gap the first
        /// line's text starts where the other lines' do, otherwise it starts after the label, as in FieldWorks. When the label (with its
        /// indent) is wider than the line, it takes a line of its own and the text starts on the next, on the full text width. However little
        /// room a narrower label leaves, the text stays beside it and wraps there.</summary>
        // Provenance: this rule follows FieldWorks' observable list layout (a 3 pt gap after the label unless it ends in a space; the label
        // goes in the hanging indent when it fits there, else the text starts after it; a label wider than the line is on its own line).
        // The code is ours.
        void PlaceFirstLine(double w, double firstIndent, ParagraphSource source)
        {
            _labelWidth = 0;
            _labelLineHeight = 0;
            var label = LabelLine(LabelText);
            if (label != null) _labelWidth = label.WidthIncludingTrailingWhitespace + (LabelText[LabelText.Length - 1] == ' ' ? 0 : LabelGap);
            if (_labelWidth > 0 && firstIndent < 0 && -firstIndent > _labelWidth)
            {
                _firstWidth = w;
                _firstX = ContentLeft;
                return;
            }
            double taken = firstIndent + _labelWidth;
            if (label != null && source.Text.Length > 0 && taken > w)
            {
                _labelLineHeight = label.Height;
                _firstWidth = w;
                _firstX = ContentLeft;
                return;
            }
            _firstWidth = Math.Max(w - taken, 1);
            _firstX = ContentLeft + (Rtl ? 0 : taken);
        }

        /// <summary>The height of the line a too-wide bullet or number takes alone above the text (the label text's own height, whatever the line height rule), or 0 when it sits beside the first line.</summary>
        double _labelLineHeight;

        /// <summary>The space above the first line (space before, margin, border, padding) and below the last.</summary>
        public double TopInset { get; private set; }
        public double BottomInset { get; private set; }
        /// <summary>Where the caret sits in an empty paragraph, vertically.</summary>
        double EmptyLineTop, EmptyLineHeight;

        static TextAlignment Alignment(int align)
        {
            switch (align)
            {
                case 1: return TextAlignment.Left;
                case 2: return TextAlignment.Center;
                case 3: return TextAlignment.Right;
                case 4: return TextAlignment.End;
                // Justify: Avalonia's TextFormatter does not stretch lines and its justifier (InterWordJustification) is internal, so
                // the lines are laid out against the leading edge, as the last line of a justified paragraph is.
                case 5: return TextAlignment.Start;
                default: return TextAlignment.Start;
            }
        }

        /// <summary>The share of a text's height that is above its baseline which an exact line height keeps on every line: the one most of the first
        /// line's characters are in, each run's share cut to a whole per cent (the first of equally popular ones winning). It is taken from the line
        /// once and used for every line, so the baselines are one slot apart.</summary>
        static double ExactFraction(TextLine line)
        {
            var shaped = line.TextRuns.OfType<ShapedTextRun>()
                .Select((run, order) => new { Percent = run.Size.Height > 0 ? (int)(run.Baseline * 100 / run.Size.Height) : 0, run.Length, order })
                .ToList();
            if (shaped.Count == 0) return line.Height > 0 ? Math.Floor(line.Baseline * 100 / line.Height) / 100 : double.NaN;
            // Each share with the characters in it, and where in the line it had them all: the last run of it that has any (or its first run).
            var shares = shaped.GroupBy(run => run.Percent)
                .Select(group => new
                {
                    Percent = group.Key,
                    Characters = group.Sum(run => run.Length),
                    Complete = group.Where(run => run.Length > 0).Select(run => run.order).DefaultIfEmpty(group.First().order).Max(),
                });
            return shares.OrderByDescending(share => share.Characters).ThenBy(share => share.Complete).First().Percent / 100.0;
        }

        /// <summary>Under an exact line height, the share of the text's height that is above its baseline, taken from the first line and used
        /// for every line so baselines are one slot apart; NaN otherwise.</summary>
        double _exactAscentFraction = double.NaN;

        /// <summary>Where the baseline sits from the top of a slot of the given height: the first line's ascent share of the slot under an
        /// exact line height, else NaN (the text sits at the bottom of the slot).</summary>
        double BaselineIn(double slot) => double.IsNaN(_exactAscentFraction) ? double.NaN : _exactAscentFraction * slot;

        /// <summary>The slot a line of the given natural height takes under the style's line height: exact fixes it, otherwise it is at least that.</summary>
        double SlotHeight(double natural)
        {
            double rule = Format.LineHeight;
            if (rule <= 0) return natural;
            return Format.ExactLineHeight ? rule : Math.Max(rule, natural);
        }

        /// <summary>Works out the margin, border and padding boxes from the format. Leading is the right side in a right-to-left paragraph.
        /// A hanging first indent is added to the leading padding, as FieldWorks does (its leading padding is the style's plus any negative first indent), so the first line starts at the padding edge.</summary>
        void ApplyBox(ParaFormat pf, double width, bool rtl, IBrush foreground)
        {
            double total = double.IsInfinity(width) ? 100000 : Math.Max(width, 10);
            double hang = Math.Max(-pf.FirstIndent, 0);
            double marginLeft = rtl ? pf.TrailingIndent : pf.LeadingIndent, marginRight = rtl ? pf.LeadingIndent : pf.TrailingIndent;
            _borderLeft = rtl ? pf.BorderTrailing : pf.BorderLeading;
            _borderRight = rtl ? pf.BorderLeading : pf.BorderTrailing;
            _borderTop = pf.BorderTop;
            _borderBottom = pf.BorderBottom;
            double padLeft = (rtl ? pf.PadTrailing : pf.PadLeading + hang), padRight = (rtl ? pf.PadLeading + hang : pf.PadTrailing);
            ContentLeft = marginLeft + _borderLeft + padLeft;
            ContentWidth = Math.Max(total - ContentLeft - marginRight - _borderRight - padRight, 10);
            // The paragraph above already left its space after; this paragraph adds what is needed to make the gap the larger of
            // the margin top and (space after + space before).
            double gap = Math.Max(pf.MarginTop - PreviousSpaceAfter, pf.SpaceBefore);
            TopInset = gap + pf.BorderTop + pf.PadTop;
            BottomInset = pf.PadBottom + pf.BorderBottom + pf.SpaceAfter;
            Box = new Rect(marginLeft, gap, Math.Max(total - marginLeft - marginRight, 0), 0);
            _fill = ColorBrush(pf.ParaColor);
            _borderBrush = _borderLeft + _borderTop + _borderRight + _borderBottom > 0 ? (ColorBrush(pf.BorderColor) ?? foreground) : null;
        }

        /// <summary>Paints the paragraph's fill colour and borders; <paramref name="top"/> is the paragraph's top in the control.</summary>
        public void DrawBox(DrawingContext context, double top)
        {
            if (_fill == null && _borderBrush == null) return;
            var box = new Rect(Box.X, top + Box.Y, Box.Width, Height - Format.SpaceAfter - Box.Y);
            if (_fill != null) context.DrawRectangle(_fill, null, box);
            if (_borderBrush == null) return;
            if (_borderTop > 0) context.DrawRectangle(_borderBrush, null, new Rect(box.X, box.Y, box.Width, _borderTop));
            if (_borderBottom > 0) context.DrawRectangle(_borderBrush, null, new Rect(box.X, box.Bottom - _borderBottom, box.Width, _borderBottom));
            if (_borderLeft > 0) context.DrawRectangle(_borderBrush, null, new Rect(box.X, box.Y, _borderLeft, box.Height));
            if (_borderRight > 0) context.DrawRectangle(_borderBrush, null, new Rect(box.Right - _borderRight, box.Y, _borderRight, box.Height));
        }

        /// <summary>The bullet or number label's line, formatted in the number font (the style's number font info over the first run's format).</summary>
        TextLine? LabelLine(string text)
        {
            if (_labelFormat == null || _labelWs == null || text.Length == 0) return null;
            if (_label != null && _labelText == text) return _label;
            _label?.Dispose();
            var props = MakeProperties(_labelFormat, _labelWs, _labelWsTag, _labelForeground ?? Brushes.Black, false, true);
            _labelRun = props;
            var source = new ParagraphSource { Text = text };
            source.Segments.Add(new ParagraphSource.Segment { Start = 0, End = text.Length, Properties = props });
            _label = FormatShort(source, 100000, new ParagraphProperties(Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, props, TextAlignment.Left));   // Left: drawn from its own x, not from the right of the 100000 wide line
            _labelText = text;
            return _label;
        }

        /// <summary>How the bullet or number is drawn (font, size, decorations), or null when there is none.</summary>
        public TextRunProperties? LabelProperties => LabelLine(LabelText)?.TextRuns[0].Properties;

        /// <summary>The bullet or number's box in paragraph coordinates, or an empty rectangle when the paragraph has none: its text only,
        /// at the first indent on the leading side (the gap that follows it is not part of it).</summary>
        public Rect LabelRect
        {
            get
            {
                var label = LabelLine(LabelText);
                if (label == null) return default;
                double lineTop = _labelLineHeight > 0 ? TopInset : Lines.Count > 0 ? Lines[0].TextY : EmptyLineTop;
                double width = label.WidthIncludingTrailingWhitespace;
                double x = Rtl ? ContentLeft + ContentWidth - Format.FirstIndent - width : ContentLeft + Format.FirstIndent;
                return new Rect(x, lineTop, width, label.Height);
            }
        }

        /// <summary>Draws the paragraph's bullet or number at the start of its first line, on the same baseline.</summary>
        public void DrawLabel(DrawingContext context, double top)
        {
            var label = LabelLine(LabelText);
            var box = LabelRect;
            if (label == null) return;
            double baseline = Lines.Count > 0 && _labelLineHeight <= 0 ? Lines[0].Line.Baseline : label.Baseline;
            label.Draw(context, new Point(box.X, top + box.Y + baseline - label.Baseline));
            if (_labelRun is RunTextProperties labelRun) DrawDecorationLine(context, labelRun, box.X, box.Right, top + box.Y + baseline);   // the label's own underline, like the text's (DrawDecorations)
        }

        // ---- model offset <-> effective offset; they differ only while a preedit is shown ----
        public int ToEffective(int offset) => PreeditAt >= 0 && offset > PreeditAt ? offset + PreeditLength : offset;

        public int ToModel(int effective)
        {
            if (PreeditAt < 0) return effective;
            if (effective >= PreeditAt + PreeditLength) return effective - PreeditLength;
            return effective > PreeditAt ? PreeditAt : effective;
        }

        /// <summary>The last caret offset of a line (effective): any newline is part of the line length (the text source ends by
        /// returning null, not a TextEndOfParagraph run: that run broke caret distances in a final right-to-left run).</summary>
        public int TextEnd(LineInfo line)
        {
            if (line.IsEmpty) return line.Start;
            int end = Math.Min(line.End - line.Line.NewLineLength, EffectiveText.Length);
            // A hard line break ends its line but is not text on it: the caret before the break is at the end of this line, the
            // caret after it at the start of the next.
            if (end > line.Start && EffectiveText[end - 1] == HardLineBreak) end--;
            return end;
        }

        /// <summary>The end of a line's text without the spaces it ends with (the ones a wrap leaves hanging past its last letter).</summary>
        int TextBeforeTrailingSpaces(LineInfo line)
        {
            int end = TextEnd(line);
            while (end > line.Start && EffectiveText[end - 1] == ' ') end--;
            return end;
        }

        LineInfo? LineOf(int effective, CharacterHit? hit = null)
        {
            if (Lines.Count == 0) return null;
            // Caret affinity at a soft wrap: the end of a wrapped line and the start of the next line are the same
            // offset. A hit that trails the last character of a line (FirstCharacterIndex inside it) means "end of that
            // line"; a plain hit at the offset means "start of the next".
            if (hit.HasValue && hit.Value.TrailingLength > 0 && hit.Value.FirstCharacterIndex + hit.Value.TrailingLength == effective)
            {
                foreach (var l in Lines)
                    if (hit.Value.FirstCharacterIndex >= l.Start && hit.Value.FirstCharacterIndex < l.End) return l;
            }
            for (int i = 0; i < Lines.Count; i++)
            {
                var l = Lines[i];
                bool last = i == Lines.Count - 1;
                if (effective >= l.Start && (effective < l.End || (last && effective <= l.End))) return l;
            }
            return Lines[Lines.Count - 1];
        }

        /// <summary>The caret hit meaning "after the last character before <paramref name="effective"/>": at a soft wrap it
        /// keeps the caret at the end of the line instead of the start of the next one.</summary>
        CharacterHit TrailingHit(int effective)
        {
            if (effective > 0 && effective <= EffectiveText.Length && EffectiveText[effective - 1] == HardLineBreak) return new CharacterHit(effective);   // after a hard line break the caret starts the next line
            int p = GraphemeClusters.Previous(EffectiveText, effective);
            return new CharacterHit(p, effective - p);
        }

        /// <summary>The caret hit that puts the caret after the character before <paramref name="modelOffset"/>, on the
        /// same line as that character (the end of a selected word at a soft wrap, say).</summary>
        public CharacterHit EndHit(int modelOffset) => modelOffset <= 0 ? new CharacterHit(0) : TrailingHit(ToEffective(modelOffset));

        /// <summary>The model offset and caret hit for the end of a line: at a soft wrap the caret stays on this line.</summary>
        public int LineEnd(int lineIndex, out CharacterHit hit)
        {
            var li = Lines[lineIndex];
            int max = TextEnd(li);
            bool last = lineIndex == Lines.Count - 1;
            hit = !last && max > li.Start ? TrailingHit(max) : new CharacterHit(max);
            return ToModel(max);
        }

        public Rect CaretRect(int modelOffset, bool effective = false, CharacterHit? hit = null)
        {
            int e = effective ? modelOffset : ToEffective(modelOffset);
            var li = LineOf(e, hit);
            if (li == null) return new Rect(EmptyCaretX(), EmptyLineTop, 1, EmptyLineHeight);
            if (li.IsEmpty) return new Rect(li.EmptyCaretX, li.TextY, 1, li.Line.Height);
            var h = hit.HasValue && hit.Value.FirstCharacterIndex + hit.Value.TrailingLength == e ? hit.Value : new CharacterHit(e);
            if (h.TrailingLength == 0 && e > li.Start && e == TextEnd(li) && e < li.End && EffectiveText[e] == HardLineBreak) h = TrailingHit(e);   // before the break: after the last character of the line
            return new Rect(CaretX(li, h), li.TextY - CaretOffset(li, e), CaretWidth, li.Line.Height);
        }

        /// <summary>Where a caret hit is drawn on a line, in paragraph coordinates. A caret in the spaces a wrapped line ends with (End puts it
        /// after them) is drawn one space width further out for each, as FieldWorks does, even past the text area; it stops at the edge of
        /// the control so that it is always seen. With no room beside the area the spaces' carets would all be at that edge, so a step across
        /// them would not show: they are held <see cref="MinCaretStep"/> apart, the End caret at the edge and the others inside it (by that default
        /// the caret still moves each step, on the text's own last letters when the text itself reaches the edge).</summary>
        double CaretX(LineInfo li, CharacterHit h)
        {
            double x = CaretXBeforeClamp(li, h);
            if (li.IsEmpty || double.IsInfinity(LayoutWidth)) return x;
            double limit = LayoutWidth - CaretWidth;
            int o = h.FirstCharacterIndex + h.TrailingLength;
            int end = TextEnd(li), core = end;
            while (core > li.Start && EffectiveText[core - 1] == ' ') core--;
            if (o > core && o <= end)
            {
                // A caret among the hanging spaces: they hang at the end of the paragraph's direction, the caret after the last at the edge.
                double room = (end - o) * MinCaretStep;
                double first = CaretXBeforeClamp(li, new CharacterHit(core));   // before the first space: where they start
                x = Rtl ? Math.Min(Math.Max(x, room), first) : Math.Max(Math.Min(x, limit - room), first);
            }
            return Math.Max(0, Math.Min(x, limit));
        }

        /// <summary>The least distance between the carets of two hanging spaces that are held at the edge of the control.</summary>
        internal const double MinCaretStep = 2;

        /// <summary>Where a caret hit is on a line before the control's edge holds it (<see cref="CaretX"/>): the place the line's layout says.</summary>
        internal double CaretXBeforeClamp(LineInfo li, CharacterHit h) => li.X + li.Map(li.DistanceOf(h));

        /// <summary>The width of the caret as the field draws it.</summary>
        internal const double CaretWidth = 1.5;

        /// <summary>How far a caret at the effective offset is raised: it follows the character before it (the one it would extend), or the one
        /// after it at the start of a line.</summary>
        double CaretOffset(LineInfo li, int effective)
        {
            if (!li.HasOffsets || li.IsEmpty) return 0;
            int probe = effective > li.Start ? effective - 1 : effective;
            if (probe >= EffectiveText.Length) return 0;
            foreach (var piece in li.Pieces(probe, probe + 1)) return piece.Value;
            return 0;
        }

        /// <summary>The model offset at a point in paragraph coordinates.</summary>
        public int HitTest(Point point) => HitTestDetailed(point).Offset;

        /// <summary>The result of hit testing: the caret offset, the caret hit that says on which side of a soft wrap or
        /// bidi boundary the caret sits, and the offset of the character under the point.</summary>
        public struct HitResult
        {
            public int Offset;
            public CharacterHit CaretHit;
            public int Character;
        }

        public HitResult HitTestDetailed(Point point)
        {
            if (Lines.Count == 0) return default;
            var li = Lines[Lines.Count - 1];
            foreach (var l in Lines)
            {
                if (point.Y < l.Y + l.Height) { li = l; break; }
            }
            if (li.IsEmpty)
                return new HitResult { Offset = Paragraph.Length, CaretHit = new CharacterHit(li.Start), Character = Paragraph.Length };   // past the last character: the empty line after a trailing break holds none
            var h = li.HitAt(li.Unmap(point.X - li.X));
            int e = h.FirstCharacterIndex + h.TrailingLength;
            int max = TextEnd(li);
            bool last = li == Lines[Lines.Count - 1];
            var caretHit = h;
            if (e > max || (h.FirstCharacterIndex >= max && li.End > max))
            {
                // The click is on the break the line ends in (or past it), beyond its text. The break stands at the line's end in the paragraph's direction, so
                // the caret goes to the visual end of the line there, which is not the logical end when the line ends in a run of the other
                // direction ("abc" and then Arabic: the Arabic run's logical end is at its left).
                e = max;
                caretHit = new CharacterHit(e);
                if (li.Length > 0)
                {
                    var stops = VisualStops(li);
                    if (stops.Count > 0)
                    {
                        var edge = Rtl ? stops[0] : stops[stops.Count - 1];
                        e = edge.Offset;
                        caretHit = edge.Hit;
                    }
                }
            }
            if (!last && e == max && max > li.Start) caretHit = TrailingHit(max);
            if (e <= max && PreeditAt < 0) DisplacedCaretAt(li, point.X, last, max, ref e, ref caretHit);
            int ch = Math.Max(0, Math.Min(Math.Max(0, EffectiveText.Length - 1), h.FirstCharacterIndex));
            var result = new HitResult
            {
                Offset = Math.Max(0, Math.Min(Paragraph.Length, ToModel(e))),
                Character = Math.Max(0, Math.Min(Math.Max(0, Paragraph.Length - 1), ToModel(ch))),
                CaretHit = PreeditAt >= 0 ? new CharacterHit(e) : caretHit,
            };
            return result;
        }

        /// <summary>Puts a click where the carets are drawn. A click beyond the End caret of a wrapped line (past the spaces it ends with, by more than a hundredth of a pixel, on the side
        /// they hang to: at the End caret itself the nearest caret decides) gives the End caret, whatever the layout says for an x past its text. Then, the carets that <see cref="CaretX"/> draws somewhere
        /// other than the layout has them (held at the edge of the control, or spaced apart inside it, over the letters before the spaces) are what the
        /// mouse follows: the click goes to the nearest of them when it is nearer than the caret the layout gave, and a tie stays with the layout.</summary>
        void DisplacedCaretAt(LineInfo li, double x, bool last, int max, ref int e, ref CharacterHit caretHit)
        {
            int core = TextBeforeTrailingSpaces(li);
            if (!last && core < max && core > li.Start)
            {
                var endHit = TrailingHit(max);
                double endX = CaretX(li, endHit);
                if (Rtl ? x < endX - 0.01 : x > endX + 0.01)
                {
                    e = max;
                    caretHit = endHit;
                    return;
                }
            }
            var near = li.DisplacedStops ??= DisplacedStopsOf(li, core);
            if (near.Count == 0) return;
            double best = Math.Abs(CaretX(li, caretHit) - x);
            foreach (var stop in near)
            {
                double away = Math.Abs(stop.X - x);
                if (away >= best) continue;
                best = away;
                e = stop.Offset;
                caretHit = stop.Hit;
            }
        }

        /// <summary>The caret stops a click can go to instead of the layout's own answer: those drawn somewhere other than the layout has them and, when
        /// there are any, every stop of the spaces the line ends with (their carets are drawn as a row, and the layout has no place for a click among them);
        /// on a line that <see cref="LineInfo.UsesRuns"/>, every stop.</summary>
        List<Stop> DisplacedStopsOf(LineInfo li, int core)
        {
            var result = new List<Stop>();
            if (li.IsEmpty || double.IsInfinity(LayoutWidth)) return result;
            var stops = VisualStops(li);
            if (li.UsesRuns)
            {
                // A line read from its runs puts a click where its runs put the carets, and where a run's end is also the end of the run beside it, at the same x, the
                // layout's answer is the end of the one it asked and not the other's, a space or two away. So every stop of such a line is a candidate: the click goes to the
                // nearest, which at a stop's own x is that stop.
                result.AddRange(stops);
                return result;
            }
            if (!stops.Any(stop => Math.Abs(CaretXBeforeClamp(li, stop.Hit) - stop.X) > 0.01)) return result;
            foreach (var stop in stops)
                if (stop.Offset >= core || Math.Abs(CaretXBeforeClamp(li, stop.Hit) - stop.X) > 0.01) result.Add(stop);
            return result;
        }

        public List<Rect> SelectionRects(int from, int to)
        {
            var result = new List<Rect>();
            if (to <= from) return result;
            int ef = ToEffective(from), et = ToEffective(to);
            foreach (var li in Lines)
            {
                int te = TextEnd(li);
                int a = Math.Max(ef, li.Start), b = Math.Min(et, te);
                if (b <= a)
                {
                    // The break is never drawn, so a selection that covers only the break gets a small block where the break is.
                    if (ef <= te && et > te && te < li.End && !li.IsEmpty && EffectiveText[te] == HardLineBreak)
                    {
                        result.Add(BreakBlock(li, te));
                    }
                    continue;
                }
                foreach (var piece in li.Pieces(a, b))
                    result.Add(new Rect(piece.Key.X, li.TextY + piece.Key.Y - piece.Value, piece.Key.Width, piece.Key.Height));
            }
            return result;
        }

        /// <summary>The small block that stands for a selected hard line break on a line: at the end of the line's text in the paragraph's
        /// direction (right of it in a left-to-right paragraph, left of it in a right-to-left one), whatever direction the last glyph runs
        /// in, so it never covers a glyph. It stays inside the field.</summary>
        Rect BreakBlock(LineInfo li, int textEnd)
        {
            var caret = CaretRect(textEnd, true);
            double w = Math.Max(3, caret.Height * 0.25);
            double edge = caret.X;
            if (textEnd > li.Start)
            {
                bool any = false;
                double left = double.MaxValue, right = double.MinValue;
                foreach (var piece in li.Pieces(li.Start, textEnd))
                {
                    any = true;
                    left = Math.Min(left, piece.Key.X);
                    right = Math.Max(right, piece.Key.Right);
                }
                if (any) edge = Rtl ? left : right;
            }
            double x = Rtl ? edge - w : edge;
            if (!double.IsInfinity(LayoutWidth)) x = Math.Min(x, LayoutWidth - w);
            return new Rect(Math.Max(x, 0), caret.Y, w, caret.Height);
        }

        public struct Stop
        {
            public CharacterHit Hit;
            public int Offset;
            public double X;
        }

        /// <summary>The distinct visual caret positions of a line, sorted by x. A bidi boundary yields two stops at
        /// different x that share logical offsets: the leading edge of one cluster and the trailing edge of the
        /// previous one are different places there.</summary>
        public List<Stop> VisualStops(LineInfo line)
        {
            if (line.IsEmpty) return new List<Stop> { new Stop { Hit = new CharacterHit(line.Start), Offset = line.Start, X = line.EmptyCaretX } };
            var candidates = new List<Stop>();
            var h = new CharacterHit(line.Start);
            int guard = 0;
            int max = TextEnd(line);
            if (line.UsesRuns)
            {
                // Every boundary between characters is two places: the leading edge of the character after it and the trailing edge of the one before.
                for (int o = line.Start; o <= max && guard++ < 10000;)
                {
                    candidates.Add(new Stop { Hit = new CharacterHit(o), Offset = o, X = CaretX(line, new CharacterHit(o)) });
                    int before = o > line.Start ? GraphemeClusters.Previous(EffectiveText, o) : o;
                    if (before < o) { var trailing = new CharacterHit(before, o - before); candidates.Add(new Stop { Hit = trailing, Offset = o, X = CaretX(line, trailing) }); }
                    int after = o < max ? GraphemeClusters.Next(EffectiveText, o) : max + 1;
                    o = after > o ? Math.Min(after, o < max ? max : after) : max + 1;
                }
            }
            else
            while (guard++ < 10000)
            {
                int o = h.FirstCharacterIndex + h.TrailingLength;
                candidates.Add(new Stop { Hit = new CharacterHit(o), Offset = o, X = CaretX(line, new CharacterHit(o)) });
                if (h.TrailingLength > 0) candidates.Add(new Stop { Hit = h, Offset = o, X = CaretX(line, h) });
                var n = line.Line.GetNextCaretCharacterHit(h);
                int no = n.FirstCharacterIndex + n.TrailingLength;
                if (no <= o || no > max) break;
                h = n;
            }
            var result = new List<Stop>();
            foreach (var c in candidates.OrderBy(s => s.X))
                if (result.Count == 0 || Math.Abs(result[result.Count - 1].X - c.X) > 0.5) result.Add(c);
            if (line != Lines[Lines.Count - 1] && max > line.Start)
            {
                // The stop at the end of a soft-wrapped line keeps the caret on this line.
                for (int i = 0; i < result.Count; i++)
                    if (result[i].Offset == max) result[i] = new Stop { Hit = TrailingHit(max), Offset = max, X = result[i].X };
            }
            return result;
        }

        /// <summary>Model offsets and x of the visual stops of a line, left to right.</summary>
        public List<KeyValuePair<int, double>> Stops(LineInfo line) => VisualStops(line).Select(s => new KeyValuePair<int, double>(s.Offset, s.X)).ToList();

        public int LineIndexOf(int modelOffset, CharacterHit? hit = null)
        {
            var li = LineOf(ToEffective(modelOffset), hit);
            return li == null ? 0 : Lines.IndexOf(li);
        }

        /// <summary>Where object runs sit in glyph mode: the bounds of the EM SPACE stand-in, with the object data.</summary>
        public List<KeyValuePair<Rect, string>> ObjectRects()
        {
            var result = new List<KeyValuePair<Rect, string>>();
            int pos = 0;
            foreach (var r in Paragraph.Runs)
            {
                if (r.IsObject && r.ObjData != null)
                {
                    int e = ToEffective(pos);
                    var li = LineOf(e);
                    if (li != null)
                        foreach (var piece in li.Pieces(e, e + 1))
                            result.Add(new KeyValuePair<Rect, string>(new Rect(piece.Key.X, li.TextY + piece.Key.Y - piece.Value, piece.Key.Width, piece.Key.Height), r.ObjData));
                }
                pos += r.Text.Length;
            }
            return result;
        }

        /// <summary>The caret's x in a paragraph with no text: where the first line would start, following the alignment.</summary>
        double EmptyCaretX() => AlignedX(_firstX, _firstWidth);

        /// <summary>Where an empty line's caret goes in a box of the given left edge and width, following the alignment.</summary>
        double AlignedX(double firstLeft, double width)
        {
            bool right = Format.Align == 3 || (Format.Align == 4 && !Rtl) || ((Format.Align == 0 || Format.Align == 5) && Rtl);
            if (Format.Align == 2) return firstLeft + width / 2;
            return right ? firstLeft + width : firstLeft;
        }

        // The same paragraph laid out again with all its text in one colour, for the selected text in a high contrast theme; made when first asked for.
        Func<IBrush, ParagraphLayout>? _recolour;
        ParagraphLayout? _recoloured;
        Color _recolouredTo;
        /// <summary>The paragraph's change stamp when this layout was made: it moves for a change of its runs, their text, writing systems and object data, a property
        /// bag changed in place, its direction and its style (issue 101), so it is all it takes to know the paragraph is not what this layout was made from.</summary>
        long _builtFrom;

        /// <summary>This paragraph laid out the same way (the same lines, so the same places) with all its text and its underlines in <paramref name="colour"/>,
        /// whatever colours the runs have: what the selected text is drawn in where the system gives it a colour of its own. Built once and kept with this
        /// layout, so selecting costs a redraw and not a layout of the text each time. Null when the paragraph is not what this layout was made from (a host
        /// changed it in place and has not called <c>RefreshFromDocument()</c>): a second layout of the new text would be drawn over this one's selection boxes.</summary>
        internal ParagraphLayout? InColour(Color colour)
        {
            if (_recolour == null || Paragraph.ChangeStamp != _builtFrom) return null;
            if (_recoloured == null || _recolouredTo != colour)
            {
                _recoloured?.Dispose();
                _recoloured = _recolour(new SolidColorBrush(colour));
                _recolouredTo = colour;
            }
            return _recoloured;
        }

        public void Dispose()
        {
            _recoloured?.Dispose();
            _label?.Dispose();
            _ellipsis?.Dispose();
            foreach (var l in Lines) { l.DisposePieces(); l.Line.Dispose(); }
            Lines.Clear();
        }
    }
}
