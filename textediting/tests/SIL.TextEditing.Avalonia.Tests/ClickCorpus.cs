using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Xunit;
using static SIL.TextEditing.Tests.TestDocs;

namespace SIL.TextEditing.Avalonia.Tests
{
    /// <summary>Paragraphs of several runs, mixed directions and spaces, found by a fuzz scan, in which a click at the x of a caret stop (or beyond the End of a
    /// wrapped line) gave a caret somewhere else. A case is a paragraph and the width of the field that lays it out.</summary>
    static class ClickCorpus
    {
        internal sealed class Case
        {
            public Case(bool rtl, int align, double width, params (string Ws, bool Bold, string Text)[] runs)
            {
                Rtl = rtl;
                Align = align;
                Width = width;
                Runs = runs;
            }

            public bool Rtl { get; }
            public int Align { get; }
            public double Width { get; }
            public (string Ws, bool Bold, string Text)[] Runs { get; }

            public override string ToString() => (Rtl ? "rtl" : "ltr") + " align " + Align + " width " + Width + " " + Runs.Sum(run => run.Text.Length) + " characters";

            public ParagraphLayout Layout()
            {
                var p = Para("en");
                foreach (var run in Runs)
                {
                    var r = new Run { Ws = run.Ws, Text = run.Text };
                    if (run.Bold) r.Props.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn);
                    p.Runs.Add(r);
                }
                p.Rtl = Rtl;
                p.Props.SetInt(TextPropertyNumbers.Align, Align);
                return Fixtures.Field(Doc(p), Width).ParagraphLayoutFor(0);
            }
        }

        static (string, bool, string) R(string ws, bool bold, string text) => (ws, bold, text);

        internal static readonly Case[] EdgeCases = new[]
        {
            new Case(true, 5, 349, R("en", true, "ill l   i dog   it هذه it l   طويلة dog ill 12 ill  "), R("en", false, " fox (جمل) i"), R("en", true, "   12 فقرة l 12 12 i it   طويلة فقرة   l   (جمل) il"), R("en", false, "l Wonderful ill ")),
            new Case(true, 5, 111, R("ar", false, "فقرة ill   i ét   fox طويلة ét طويلة ill   dog فقر"), R("ar", false, "ة i jumps l 12 dog i"), R("ar", true, "ll l it   jumps   12   هذه   ét Wonderful هذه   ét ét it "), R("en", false, "  Wonderful Wonderful فقرة هذه fox   it ")),
            new Case(false, 5, 138, R("ar", true, "fox ét fox"), R("en", false, " l هذه "), R("en", false, "  (جمل) هذه   فقرة jumps (جمل) "), R("ar", false, "dog fox (جمل) 12 it ét dog فقرة   fox dog 12 "))
        };

        internal static readonly Case[] HangingCases = new[]
        {
            new Case(false, 5, 168, R("en", false, "(ج"), R("en", false, "مل) dog   12 فقرة  "), R("en", false, " فقرة   jumps fox فقرة طويلة 12   i jumps هذه فقرة dog   فقرة   ")),
            new Case(true, 1, 389, R("ar", false, "فقرة it فقرة (جمل) fox   ét (جمل)   طويلة   dog dog Wonderful dog (جمل) it Wonderful ét Wonderful i طويلة ét ill 12   ill dog فقرة"), R("en", false, " فقرة "), R("en", true, "jumps (جمل) l  "), R("en", true, " jumps   dog ")),
            new Case(true, 5, 301, R("ar", true, "Wonderful ét Wonderful   Wonderful "), R("en", false, "  jumps (جمل) Wonderful "), R("en", false, "فقرة i   طويلة طويلة   fox   12 fox طويلة   l it   it   dog ill Wonderful   fox   ill jumps fox Wonderful   dog ill fox   12 i هذه ")),
            new Case(true, 5, 107, R("ar", false, "fox   ط"), R("ar", true, "ويلة jumps هذه   it   ét l l هذه (جمل) i ét فقرة i jumps l طويلة i طو"), R("en", false, "يلة   fox jumps هذه   هذه l   هذه   (جمل)   dog jumps i i  "), R("en", false, " Wonderful dog ")),
            new Case(true, 5, 152, R("en", false, "ét dog "), R("en", false, "  "), R("ar", false, "فقرة   Wonderful dog 12 it  "), R("en", false, " i طويلة it   dog   طويلة ill   "))
        };

        internal static readonly Case[] BeyondCases = new[]
        {
            new Case(false, 1, 102, R("en", false, "(جمل) fox ill fox   fox ill jumps ét   Wonderful طويلة فقرة jumps i طويلة هذه (جمل) l l هذه 12   طوي"), R("en", true, "لة dog il"), R("en", true, "l   fox Wonderful "))
        };

        /// <summary>A no-break space is written "~" in the cases below, so that it can be told from a space.</summary>
        static (string, bool, string) B(string ws, bool bold, string text) => (ws, bold, text.Replace('~', ' '));

        /// <summary>Left-to-right paragraphs whose line has a right-to-left run that ends in spaces (or is followed by one): a click at the x where the run before it ends,
        /// which is where the right-to-left run's own end is too, gave the end of the right-to-left run, one or two spaces to the right (issue 109).</summary>
        internal static readonly Case[] BoundaryCases = new[]
        {
            new Case(false, 2, 390, B("ar", false, "Wonderful~itit~فقرة   (جمل)   12~i   12عربية  "), B("en", false, " فقرة~Wonderful~")),
            new Case(false, 0, 147, B("en", false, "fo"), B("en", true, "x   l~هذه طويلة "), B("fr", false, "  هذه 12do"), B("fr", true, "g 12it فقرة"), B("en", false, "~(جمل) ét")),
            new Case(false, 5, 303, B("ar", false, "ill   it   ét   فقرة فقرةطويلة "), B("fr", false, "  هذهjumps~jumps   Wonderful é~ét   fox~(جمل) 12 12ét"), B("en", false, " lعربية 12~i~طويلة   طويلة"), B("ar", false, "~é dog~")),
            new Case(false, 4, 198, B("en", true, "dogفقرة   طويلة~Wonderful   é ét~fox   it   هذه~illهذه(جمل)  "), B("fr", false, " طويلة~ill do"), B("en", false, "g")),
        };

        /// <summary>The numbers of the cases in <paramref name="cases"/>, for a theory (a case itself is not something xunit can carry).</summary>
        public static IEnumerable<object[]> Numbers(Case[] cases) => Enumerable.Range(0, cases.Length).Select(i => new object[] { i });

        public static IEnumerable<object[]> EdgeNumbers() => Numbers(EdgeCases);

        public static IEnumerable<object[]> HangingNumbers() => Numbers(HangingCases);

        public static IEnumerable<object[]> BeyondNumbers() => Numbers(BeyondCases);

        public static IEnumerable<object[]> BoundaryNumbers() => Numbers(BoundaryCases);
    }
}
