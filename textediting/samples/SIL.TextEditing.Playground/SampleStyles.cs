using System;
using System.Collections.Generic;
using System.Linq;
using SIL.TextEditing;

namespace SIL.TextEditing.Playground
{
    /// <summary>The playground's stylesheet: a few named paragraph and character styles, with one writing-system override, answering the
    /// host side of ADR 0004. A real host (FieldWorks, Motif) keeps its styles in the project; the field only asks.</summary>
    public sealed class SampleStyles : IStyleResolver
    {
        public const string None = "(none)";

        // Colours are FieldWorks BGR integers.
        public const int Red = 0x0000FF;
        public const int Blue = 0xFF0000;
        public const int PaleYellow = 0x99FFFF;
        public const int PaleGrey = 0xEEEEEE;
        public const int Green = 0x008000;

        static int Mpt(double pt) => (int)Math.Round(pt * 1000);

        static PropertyBag Bag() => new PropertyBag();

        static readonly Dictionary<string, PropertyBag> Character = new Dictionary<string, PropertyBag>
        {
            ["Emphasis"] = Bag().SetInt(TextPropertyNumbers.Italic, TextPropertyNumbers.ToggleInvert),
            ["Strong"] = Bag().SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn),
            ["Light"] = Bag().SetInt(TextPropertyNumbers.Bold, 300),
            ["Warning"] = Bag().SetInt(TextPropertyNumbers.ForeColor, Red).SetInt(TextPropertyNumbers.BackColor, PaleYellow),
            ["Misspelled"] = Bag().SetInt(TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineDouble).SetInt(TextPropertyNumbers.UnderColor, Red),
            ["Deleted"] = Bag().SetInt(TextPropertyNumbers.Underline, TextPropertyNumbers.UnderlineStrikethrough),
            ["Superscript"] = Bag().SetInt(TextPropertyNumbers.Superscript, 1),
            ["Subscript"] = Bag().SetInt(TextPropertyNumbers.Superscript, 2),
            ["Large"] = Bag().SetInt(TextPropertyNumbers.FontSize, 4, TextPropertyNumbers.VariationEnum),
            ["Typewriter"] = Bag().SetStr(TextPropertyNumbers.FontFamily, "<default monospace>"),
        };

        static readonly Dictionary<string, PropertyBag> Paragraphs = new Dictionary<string, PropertyBag>
        {
            ["Heading 1"] = Bag().SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn).SetInt(TextPropertyNumbers.FontSize, Mpt(22), TextPropertyNumbers.VariationMilliPoint)
                .SetInt(TextPropertyNumbers.SpaceBefore, Mpt(12), 1).SetInt(TextPropertyNumbers.SpaceAfter, Mpt(6), 1),
            ["Block Quote"] = Bag().SetInt(TextPropertyNumbers.Italic, TextPropertyNumbers.ToggleOn).SetInt(TextPropertyNumbers.LeadingIndent, Mpt(36), 1)
                .SetInt(TextPropertyNumbers.TrailingIndent, Mpt(36), 1).SetInt(TextPropertyNumbers.SpaceBefore, Mpt(6), 1).SetInt(TextPropertyNumbers.SpaceAfter, Mpt(6), 1),
            ["Hanging"] = Bag().SetInt(TextPropertyNumbers.LeadingIndent, Mpt(36), 1).SetInt(TextPropertyNumbers.FirstIndent, Mpt(-36), 1),
            ["First Line Indent"] = Bag().SetInt(TextPropertyNumbers.FirstIndent, Mpt(24), 1),
            ["Centered"] = Bag().SetInt(TextPropertyNumbers.Align, 2),
            ["Right"] = Bag().SetInt(TextPropertyNumbers.Align, 3),
            ["Trailing"] = Bag().SetInt(TextPropertyNumbers.Align, 4),
            ["Justified"] = Bag().SetInt(TextPropertyNumbers.Align, 5),
            ["Double Spaced"] = Bag().SetInt(TextPropertyNumbers.LineHeight, 20000, TextPropertyNumbers.VariationRelative),
            ["Exact 30"] = Bag().SetInt(TextPropertyNumbers.LineHeight, Mpt(-30), 1),
            ["Exact 10"] = Bag().SetInt(TextPropertyNumbers.LineHeight, Mpt(-10), 1),
            ["Boxed"] = Bag().SetInt(TextPropertyNumbers.BorderTop, Mpt(1), 1).SetInt(TextPropertyNumbers.BorderBottom, Mpt(1), 1)
                .SetInt(TextPropertyNumbers.BorderLeading, Mpt(3), 1).SetInt(TextPropertyNumbers.BorderTrailing, Mpt(1), 1).SetInt(TextPropertyNumbers.BorderColor, Blue)
                .SetInt(TextPropertyNumbers.PadTop, Mpt(4), 1).SetInt(TextPropertyNumbers.PadBottom, Mpt(4), 1).SetInt(TextPropertyNumbers.PadLeading, Mpt(6), 1).SetInt(TextPropertyNumbers.PadTrailing, Mpt(6), 1)
                .SetInt(TextPropertyNumbers.ParaColor, PaleGrey).SetInt(TextPropertyNumbers.LeadingIndent, Mpt(12), 1).SetInt(TextPropertyNumbers.TrailingIndent, Mpt(12), 1),
            ["Two Lines Only"] = Bag().SetInt(TextPropertyNumbers.MaxLines, 2),
            ["List Bullet"] = Bag().SetInt(TextPropertyNumbers.LeadingIndent, Mpt(36), 1).SetInt(TextPropertyNumbers.FirstIndent, Mpt(-18), 1).SetInt(TextPropertyNumbers.BulNumScheme, 101),
            ["List Number"] = Bag().SetInt(TextPropertyNumbers.LeadingIndent, Mpt(36), 1).SetInt(TextPropertyNumbers.FirstIndent, Mpt(-24), 1).SetInt(TextPropertyNumbers.BulNumScheme, 10)
                .SetStr(TextPropertyNumbers.BulNumTxtAfter, "."),
            ["List Flush"] = Bag().SetInt(TextPropertyNumbers.LeadingIndent, Mpt(36), 1).SetInt(TextPropertyNumbers.BulNumScheme, 10).SetStr(TextPropertyNumbers.BulNumTxtAfter, "."),
            ["List Roman"] = Bag().SetInt(TextPropertyNumbers.LeadingIndent, Mpt(36), 1).SetInt(TextPropertyNumbers.FirstIndent, Mpt(-30), 1).SetInt(TextPropertyNumbers.BulNumScheme, 12)
                .SetStr(TextPropertyNumbers.BulNumTxtBefore, "(").SetStr(TextPropertyNumbers.BulNumTxtAfter, ")"),
            ["List Wide Label"] = Bag().SetInt(TextPropertyNumbers.LeadingIndent, Mpt(36), 1).SetInt(TextPropertyNumbers.FirstIndent, Mpt(-24), 1).SetInt(TextPropertyNumbers.BulNumScheme, 10)
                .SetStr(TextPropertyNumbers.BulNumTxtBefore, "Chapter number ").SetStr(TextPropertyNumbers.BulNumTxtAfter, "."),
            ["Table Header"] = Bag().SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn).SetInt(TextPropertyNumbers.ParaColor, PaleGrey),
            ["Table Row"] = Bag(),
            ["List Letter"] = Bag().SetInt(TextPropertyNumbers.LeadingIndent, Mpt(36), 1).SetInt(TextPropertyNumbers.FirstIndent, Mpt(-24), 1).SetInt(TextPropertyNumbers.BulNumScheme, 14)
                .SetStr(TextPropertyNumbers.BulNumTxtAfter, ")"),
        };

        public static SampleStyles Instance { get; } = new SampleStyles();

        /// <summary>How the playground's fields tell a table row, a list item and a heading from a plain paragraph (ADR 0033): by these style names, which are the host's.</summary>
        public static IBlockRecognizer Recognizer { get; } = new StyleBlockRecognizer(
            new[] { "Heading 1" },
            new[] { "List Bullet", "List Number", "List Flush", "List Roman", "List Wide Label", "List Letter" },
            new[] { "Table Header", "Table Row" },
            plainStyle: string.Empty);

        public static IReadOnlyList<string> CharacterStyles { get; } = new[] { None }.Concat(Character.Keys).ToList();
        public static IReadOnlyList<string> ParagraphStyles { get; } = new[] { None }.Concat(Paragraphs.Keys).ToList();

        /// <summary>The style's properties, with the one writing-system override the sample has: "Strong" is a little larger in Arabic.</summary>
        public PropertyBag? Resolve(string styleName, string ws)
        {
            PropertyBag? found;
            if (!Character.TryGetValue(styleName, out found) && !Paragraphs.TryGetValue(styleName, out found)) return null;
            if (styleName == "Strong" && ws == "ar")
            {
                var bag = found.Clone();
                bag.SetInt(TextPropertyNumbers.FontSize, 11000, TextPropertyNumbers.VariationRelative);
                return bag;
            }
            return found;
        }
    }
}
