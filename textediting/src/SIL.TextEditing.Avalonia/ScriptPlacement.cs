using System;
using System.IO;
using System.Runtime.CompilerServices;
using Avalonia.Media;
#if NET10_0_OR_GREATER
using GlyphTypefaceType = Avalonia.Media.GlyphTypeface;
#else
using GlyphTypefaceType = Avalonia.Media.IGlyphTypeface;
#endif

namespace SIL.TextEditing.Avalonia
{
    /// <summary>How big superscript and subscript text is and how far it is raised or lowered: the font's own recommendation, from its OS/2 table (the
    /// size and the vertical offset of each), as FieldWorks reads it, and where the font gives nothing (no table, or a value of zero) FieldWorks' own
    /// fallback: two thirds of the size, a superscript raised by the rest of the height and a subscript lowered by a fifth of its own height.</summary>
    internal readonly struct ScriptPlacement
    {
        ScriptPlacement(double sizeScale, double offsetScale)
        {
            SizeScale = sizeScale;
            OffsetScale = offsetScale;
        }

        /// <summary>The size of the text against the size of the text beside it.</summary>
        public double SizeScale { get; }

        /// <summary>How far the baseline moves, as a share of the size of the text beside it: up for a superscript, down for a subscript.</summary>
        public double OffsetScale { get; }

        /// <summary>The smallest size, against the size beside it, that is taken as a font's own: a fifth.</summary>
        const double MinimumScale = 0.2;

        /// <summary>The fallback size: two thirds.</summary>
        public const double FallbackSizeScale = CharFormat.DefaultSuperscriptScale;

        /// <summary>The placement from the values of a font's OS/2 table (<paramref name="size"/> and <paramref name="offset"/> of the script asked for, in the
        /// font's design units, zero for none) and the height of the font in the same units. A value of zero or less, or one that cannot be right, or no height, is a font that does not say.</summary>
        public static ScriptPlacement From(int size, int offset, int fontHeight, bool superscript)
        {
            // A font that says something that cannot be right (a value below zero, a size outside a fifth to the whole of the size beside it, an offset of more than
            // that size: some fonts give such values) is a font that does not say. That sanity range is ours; FieldWorks falls back only on zero.
            double scale = fontHeight > 0 && size > 0 && (double)size / fontHeight >= MinimumScale && (double)size / fontHeight <= 1 ? (double)size / fontHeight : FallbackSizeScale;
            double shift = fontHeight > 0 && offset > 0 && (double)offset / fontHeight <= 1 ? (double)offset / fontHeight : superscript ? 1 - scale : scale / 5;
            return new ScriptPlacement(scale, shift);
        }

        /// <summary>The placement for a font, from its OS/2 and hhea tables; the fallback when it has no OS/2 table. The height of the font is its
        /// ascent and descent and line gap as its hhea table says, or its metrics when it has none.</summary>
        public static ScriptPlacement For(Typeface typeface, bool superscript)
        {
            var glyphs = typeface.GlyphTypeface;
            if (glyphs == null) return From(0, 0, 0, superscript);
            var values = Cache.GetValue(glyphs, g => ReadValues((GlyphTypefaceType)g));
            return From(superscript ? values.SuperscriptSize : values.SubscriptSize, superscript ? values.SuperscriptOffset : values.SubscriptOffset, values.Height, superscript);
        }

        /// <summary>What a font says, read once for each font: its OS/2 values and its height.</summary>
        sealed class FontValues
        {
            public int SubscriptSize, SubscriptOffset, SuperscriptSize, SuperscriptOffset, Height;
        }

        static readonly ConditionalWeakTable<object, FontValues> Cache = new ConditionalWeakTable<object, FontValues>();

        static FontValues ReadValues(GlyphTypefaceType glyphs)
        {
            var values = new FontValues();
            var metrics = glyphs.Metrics;
            values.Height = metrics.Descent - metrics.Ascent + metrics.LineGap;
            if (TryGetTable(glyphs, 'h', 'h', 'e', 'a', out var hhea) && hhea.Length >= 10)
                values.Height = Int16(hhea, 4) - Int16(hhea, 6) + Int16(hhea, 8);
            if (TryGetTable(glyphs, 'O', 'S', '/', '2', out var os2) && os2.Length >= 26)
            {
                values.SubscriptSize = Int16(os2, 12);
                values.SubscriptOffset = Int16(os2, 16);
                values.SuperscriptSize = Int16(os2, 20);
                values.SuperscriptOffset = Int16(os2, 24);
            }
            return values;
        }

        /// <summary>A big-endian signed 16-bit value of a font table.</summary>
        static int Int16(byte[] table, int at) => (short)((table[at] << 8) | table[at + 1]);

#if NET10_0_OR_GREATER
        // Avalonia 12 gives no access to a font's tables, only to its file (on Avalonia 11.3 a typeface gives a table by its tag): a table is found in the file's
        // table directory. In a font collection it is the table of the member whose family name is the typeface's (the first member where none is). The result is
        // only ever a fallback, so whatever goes wrong in reading the file (it is a file: it can be missing, locked, short, corrupt) is no table.
        static bool TryGetTable(GlyphTypefaceType glyphs, char a, char b, char c, char d, out byte[] table)
        {
            table = Array.Empty<byte>();
            try
            {
                Stream? stream = null;
                if (!glyphs.PlatformTypeface.TryGetStream(out stream) || stream == null) return false;
                using (stream)
                {
                    if (!stream.CanSeek) return false;   // a font file can be tens of megabytes (a CJK collection): it is not read whole for a table
                    var header = ReadAt(stream, 0, 16);
                    if (header == null) return false;
                    long start = 0;
                    if (header[0] == 't' && header[1] == 't' && header[2] == 'c' && header[3] == 'f')
                    {
                        start = (uint)Int32(header, 12);
                        int members = Int32(header, 8);
                        for (int m = 0; m < members && m < 256; m++)
                        {
                            var at = ReadAt(stream, 12 + 4 * m, 4);
                            if (at == null) break;
                            long member = (uint)Int32(at, 0);
                            if (!HasFamily(stream, member, glyphs.FamilyName)) continue;
                            start = member;
                            break;
                        }
                    }
                    var found = FindTable(stream, start, a, b, c, d);
                    if (found == null) return false;
                    table = found;
                    return true;
                }
            }
            catch (Exception) { return false; }
        }

        /// <summary>The table with a tag in the font that starts at <paramref name="start"/> of a file, or null.</summary>
        static byte[]? FindTable(Stream stream, long start, char a, char b, char c, char d)
        {
            var directory = ReadAt(stream, start, 12);
            if (directory == null) return null;
            int count = (directory[4] << 8) | directory[5];
            var entries = ReadAt(stream, start + 12, 16 * count);
            if (entries == null) return null;
            for (int i = 0; i < count; i++)
            {
                int entry = 16 * i;
                if (entries[entry] != a || entries[entry + 1] != b || entries[entry + 2] != c || entries[entry + 3] != d) continue;
                uint offset = (uint)Int32(entries, entry + 8), length = (uint)Int32(entries, entry + 12);
                if (length > 1 << 20) return null;
                return ReadAt(stream, offset, (int)length);
            }
            return null;
        }

        /// <summary>Whether the font that starts at <paramref name="start"/> has <paramref name="family"/> as its family name or its typographic family name (name ids 1
        /// and 16, in the Windows and the Unicode platforms).</summary>
        static bool HasFamily(Stream stream, long start, string family)
        {
            var names = FindTable(stream, start, 'n', 'a', 'm', 'e');
            if (names == null || names.Length < 6) return false;
            int records = (names[2] << 8) | names[3], strings = (names[4] << 8) | names[5];
            for (int i = 0; i < records; i++)
            {
                int r = 6 + 12 * i;
                if (r + 12 > names.Length) break;
                int platform = (names[r] << 8) | names[r + 1], id = (names[r + 6] << 8) | names[r + 7], length = (names[r + 8] << 8) | names[r + 9], offset = (names[r + 10] << 8) | names[r + 11];
                if ((id != 1 && id != 16) || (platform != 3 && platform != 0) || strings + offset + length > names.Length) continue;
                var text = new char[length / 2];
                for (int k = 0; k + 1 < length; k += 2) text[k / 2] = (char)((names[strings + offset + k] << 8) | names[strings + offset + k + 1]);
                if (string.Equals(new string(text), family, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        static int Int32(byte[] data, int at) => (data[at] << 24) | (data[at + 1] << 16) | (data[at + 2] << 8) | data[at + 3];

        static byte[]? ReadAt(Stream stream, long at, int length)
        {
            if (at < 0 || length < 0 || at + length > stream.Length) return null;
            stream.Position = at;
            var bytes = new byte[length];
            int done = 0;
            while (done < length)
            {
                int n = stream.Read(bytes, done, length - done);
                if (n <= 0) return null;
                done += n;
            }
            return bytes;
        }
#else
        static bool TryGetTable(GlyphTypefaceType glyphs, char a, char b, char c, char d, out byte[] table)
        {
            table = Array.Empty<byte>();
            if (!glyphs.TryGetTable(((uint)a << 24) | ((uint)b << 16) | ((uint)c << 8) | d, out var found) || found == null) return false;
            table = found;
            return true;
        }
#endif
    }
}
