using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;

namespace SIL.TextEditing.LCModel.Tests
{
    /// <summary>What liblcm's real serializers (SIL.LCModel.Core only) do with one property: the oracle every refusal rule is checked
    /// against. Two paths, as a project uses them: a paragraph's StyleRules (<c>TsStringUtils.GetXmlRep</c> then <c>GetTextProps</c>) and
    /// a string's runs (<see cref="TsStringClipboard.ToXml"/> then <see cref="TsStringClipboard.FromXml"/>, liblcm's TsStringSerializer).
    /// Each is followed by a save of what was read, because liblcm can read a value it then cannot write.</summary>
    static class LiblcmOracle
    {
        public enum Outcome
        {
            /// <summary>Written, read back and written again with the same value.</summary>
            Kept,
            /// <summary>Read back without the property: liblcm does not store it (or ignores it on read).</summary>
            Dropped,
            /// <summary>Read back with a different value, without throwing.</summary>
            Changed,
            /// <summary>The write, the read or the save after the read threw.</summary>
            Throws,
        }

        static readonly HandlesFactory Factory = new HandlesFactory(Handles.Instance);

        public static Outcome Int(bool styleRules, int tpt, int variation, int value)
        {
            var builder = TsStringUtils.MakePropsBldr();
            if (!styleRules) builder.SetIntPropValues(1, 0, Handles.En);
            builder.SetIntPropValues(tpt, variation, value);
            return Run(styleRules, builder.GetTextProps(), back =>
            {
                for (int i = 0; i < back.IntPropCount; i++)
                    if (back.GetIntProp(i, out int t, out _) == value && t == tpt) return Outcome.Kept;
                    else if (t == tpt) return Outcome.Changed;
                return Outcome.Dropped;
            });
        }

        // The whole sweep is run once, whichever test asks first: several tests read it, and the serializers are the slow part.
        static readonly Lazy<Dictionary<(bool, int, int, int), Outcome>> IntTable = new Lazy<Dictionary<(bool, int, int, int), Outcome>>(() =>
        {
            var table = new Dictionary<(bool, int, int, int), Outcome>();
            foreach (bool styleRules in new[] { true, false })
                foreach (var (tpt, variation, value) in IntCases())
                    table[(styleRules, tpt, variation, value)] = Int(styleRules, tpt, variation, value);
            return table;
        });

        static readonly Lazy<Dictionary<(bool, int, string), Outcome>> StrTable = new Lazy<Dictionary<(bool, int, string), Outcome>>(() =>
        {
            var table = new Dictionary<(bool, int, string), Outcome>();
            foreach (bool styleRules in new[] { true, false })
                foreach (var (tpt, value) in StringCases())
                    table[(styleRules, tpt, value)] = Str(styleRules, tpt, value);
            return table;
        });

        /// <summary>The outcome of one case of the swept domain (<see cref="IntCases"/>), run once.</summary>
        public static Outcome SweptInt(bool styleRules, int tpt, int variation, int value) => IntTable.Value[(styleRules, tpt, variation, value)];

        /// <summary>The outcome of one case of the swept domain (<see cref="StringCases"/>), run once.</summary>
        public static Outcome SweptStr(bool styleRules, int tpt, string value) => StrTable.Value[(styleRules, tpt, value)];

        public static Outcome Str(bool styleRules, int tpt, string value)
        {
            var builder = TsStringUtils.MakePropsBldr();
            if (!styleRules) builder.SetIntPropValues(1, 0, Handles.En);
            builder.SetStrPropValue(tpt, value);
            return Run(styleRules, builder.GetTextProps(), back =>
            {
                for (int i = 0; i < back.StrPropCount; i++)
                {
                    string v = back.GetStrProp(i, out int t);
                    if (t == tpt) return v == value ? Outcome.Kept : Outcome.Changed;
                }
                return Outcome.Dropped;
            });
        }

        /// <summary>Whether liblcm can write, read and write again what the adapter built, for either path.</summary>
        public static bool Loads(bool styleRules, ITsTextProps props) => Run(styleRules, props, _ => Outcome.Kept) == Outcome.Kept;

        public static bool LoadsString(ITsString text)
        {
            try
            {
                var back = TsStringClipboard.FromXml(TsStringClipboard.ToXml(text, Handles.Instance), Handles.Instance);
                TsStringClipboard.ToXml(back, Handles.Instance);
                return true;
            }
            catch (Exception)
            {
                return false;   // any throw is what the oracle reports as a refusal
            }
        }

        static Outcome Run(bool styleRules, ITsTextProps props, Func<ITsTextProps, Outcome> judge)
        {
            try
            {
                ITsTextProps back;
                if (styleRules)
                {
                    back = TsStringUtils.GetTextProps(XElement.Parse(TsStringUtils.GetXmlRep(props, Factory)), Factory);
                    TsStringUtils.GetXmlRep(back, Factory);
                }
                else
                {
                    var text = TsStringUtils.MakeString("text", props);
                    var read = TsStringClipboard.FromXml(TsStringClipboard.ToXml(text, Handles.Instance), Handles.Instance);
                    TsStringClipboard.ToXml(read, Handles.Instance);
                    back = read.get_Properties(0);
                }
                return judge(back);
            }
            catch (Exception)
            {
                return Outcome.Throws;   // whatever liblcm throws (XML schema, index, overflow, argument) is the refusal
            }
        }

        // ---- the swept domain ----------------------------------------------------------------------------------------------------------

        /// <summary>Integer property numbers swept. The writing system (1) and base writing system (16) are handle lookups the host owns.</summary>
        public static IEnumerable<int> IntNumbers => Enumerable.Range(2, 299).Where(n => n != 16);

        /// <summary>String property numbers swept.</summary>
        public static IEnumerable<int> StringNumbers => Enumerable.Range(1, 300);

        public static readonly int[] Variations = { -1, 0, 1, 2, 3, 4, 5, 100 };

        public static readonly int[] IntValues =
        {
            int.MinValue, int.MinValue + 1, -65536, -1000, -2, -1, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 17, 100, 256, 65536, 16777215, int.MaxValue,
        };

        public static readonly string[] StringValues = StringSamples().Distinct().ToArray();

        static IEnumerable<string> StringSamples()
        {
            foreach (var s in new[]
            {
                "", "x", "abcdefg", "abcdefgh", "abcdefghijklmnop", "abcdefghijklmnopq", "Emphasis", "  lead", "tab\tnl\nx", "A\0B", "\0", "A\0\u0001",
                "\u0001", "a\u000Bb", "\u0008", "\u000C", "\u000E", "\u001F", "\uD800", "\uDC00", "\uFFFE", "\uFFFF", "ok\uD83D\uDE00", "\u0085", "\u2028",
            }) yield return s;
            // Object data: a type character, then a payload (a GUID is eight characters), for every type and some beyond.
            for (int type = 0; type <= 255; type++)
                foreach (var payload in new[] { "", "abcdefg", "abcdefgh", "abcdefghijklmnop", "C:\\a\\b", "a\u0001b", "<x/>" })
                    yield return (char)type + payload;
            // Packed values: BulNumFontInfo (147) is records of a property character and two characters of value, or a font family;
            // WsStyle (156) is a writing system, a font-family length and name, and a property count. A well-formed one of each, then breakages.
            foreach (var s in new[]
            {
                "\u0003\u0001\u0000", "\u0003\u0001\u0000\u0001Arial\u0000", "\u0001Arial\u0000", "\u0003\u0001", "\u0003\u0001\u0000\u0002", "\u0009\u0001\u0000",
                "\u0001\u0000\u0000\u0000", "\u0001\u0000\u0005Arial\u0000", "\u0001\u0000\u0000\u0001\u0003\u0003\u0001\u0000", "\u0001\u0000\u0009Arial\u0000",
                "\u0001\u0000\u0000\u0001\u0003\u0003", "\u0001\u0000\u0000\uFFFF",
            }) yield return s;
        }

        /// <summary>The integer cases swept: every number, variation and value.</summary>
        public static IEnumerable<(int tpt, int variation, int value)> IntCases()
        {
            foreach (int tpt in IntNumbers)
                foreach (int variation in Variations)
                    foreach (int value in IntValues)
                        yield return (tpt, variation, value);
        }

        public static IEnumerable<(int tpt, string value)> StringCases()
        {
            foreach (int tpt in StringNumbers)
                foreach (var value in StringValues)
                    yield return (tpt, value);
        }
    }
}
