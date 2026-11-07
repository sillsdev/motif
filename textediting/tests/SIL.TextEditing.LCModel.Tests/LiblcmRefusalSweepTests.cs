using System.Collections.Generic;
using System.Linq;
using SIL.LCModel.Core.Text;
using Xunit;
using static SIL.TextEditing.LCModel.Tests.LiblcmOracle;

namespace SIL.TextEditing.LCModel.Tests
{
    /// <summary>The one place the adapter's refusal rules (ADR 0016, amended for issues 136 and 139) are tied to what liblcm does. Every
    /// property number 2 to 300 is run through liblcm's real serializers with a spread of variations and values, on both paths (a paragraph's
    /// StyleRules and a string's runs), and <see cref="LiblcmRefusals"/> must refuse exactly what liblcm throws on. When liblcm changes, a
    /// failure here names the property and value; then the rule, the ADR and the tests that name the property change together.</summary>
    public class LiblcmRefusalSweepTests
    {
        static string Describe(bool styleRules, int tpt, string what) => (styleRules ? "StyleRules " : "run ") + tpt + " " + what;

        [Fact]
        public void The_integer_rules_refuse_exactly_what_liblcm_throws_on()
        {
            var mismatches = new List<string>();
            foreach (bool styleRules in new[] { true, false })
                foreach (var (tpt, variation, value) in IntCases())
                {
                    bool liblcmThrows = SweptInt(styleRules, tpt, variation, value) == Outcome.Throws;
                    bool refused = LiblcmRefusals.ForInt(tpt, variation, value, styleRules) != null;
                    if (liblcmThrows != refused)
                        mismatches.Add(Describe(styleRules, tpt, $"var {variation} value {value}: liblcm {(liblcmThrows ? "throws" : "loads")}, rules {(refused ? "refuse" : "accept")}"));
                }
            Assert.True(mismatches.Count == 0, mismatches.Count + " mismatches: " + string.Join(" | ", mismatches));
        }

        [Fact]
        public void The_string_rules_refuse_exactly_what_liblcm_throws_on()
        {
            var mismatches = new List<string>();
            foreach (bool styleRules in new[] { true, false })
                foreach (var (tpt, value) in StringCases())
                {
                    bool liblcmThrows = SweptStr(styleRules, tpt, value) == Outcome.Throws;
                    bool refused = LiblcmRefusals.ForString(tpt, value, styleRules) != null;
                    if (liblcmThrows != refused)
                        mismatches.Add(Describe(styleRules, tpt, $"'{Show(value)}': liblcm {(liblcmThrows ? "throws" : "loads")}, rules {(refused ? "refuse" : "accept")}"));
                }
            Assert.True(mismatches.Count == 0, mismatches.Count + " mismatches: " + string.Join(" | ", mismatches));
        }

        static string Show(string s) => string.Concat(s.Select(c => c < 32 || c > 126 ? "\\u" + ((int)c).ToString("X4") : c.ToString()));

        [Fact]
        public void Properties_refused_by_number_are_exactly_those_liblcm_throws_on_for_every_value()
        {
            // A property is refused by number when no variation or value of it loads: the reader does not know its attribute.
            var byNumber = new Dictionary<bool, List<int>>();
            foreach (bool styleRules in new[] { true, false })
                byNumber[styleRules] = IntNumbers
                    .Where(tpt => IntCases().Where(c => c.tpt == tpt && !(c.variation == -1 && c.value == -1)).All(c => SweptInt(styleRules, c.tpt, c.variation, c.value) == Outcome.Throws))
                    .ToList();
            foreach (bool styleRules in new[] { true, false })
                Assert.Equal(IntNumbers.Where(n => TsStringConversion.IsRefusedByLiblcm(n, styleRules)).ToList(), byNumber[styleRules]);
        }

        // What liblcm stores and loads back, with a value it accepts. Everything else in 2..300 is written without the property or read
        // back without it; ADR 0016 says those cannot corrupt a project. If liblcm starts keeping one, this fails: take it out of the
        // ADR's list of silently lost properties (and out of the Dropped pass-through test) and add it here.
        static readonly int[] KeptInts =
            Enumerable.Range(2, 9).Concat(Enumerable.Range(17, 6)).Concat(new[] { 24, 25, 26, 128 }).Concat(Enumerable.Range(134, 11)).Concat(new[] { 148, 149, 162 }).ToArray();

        // Strings: fontFamily, tags, object data, fontVariations, namedStyle and the two bullet texts, and in StyleRules only the packed
        // BulNumFontInfo and WsStyle (a run does not write them). charStyle, paraStyle and tabList are written and then ignored on read.
        static readonly int[] KeptStringsInRuns = { 1, 5, 6, 130, 133, 145, 146 };
        static readonly int[] KeptStringsInStyleRules = { 1, 5, 6, 130, 133, 145, 146, 147, 156 };

        [Fact]
        public void The_integer_properties_liblcm_keeps_are_exactly_the_expected_ones()
        {
            foreach (bool styleRules in new[] { true, false })
            {
                var kept = IntNumbers.Where(tpt => Int(styleRules, tpt, 1, 1) == Outcome.Kept).ToList();
                Assert.Equal(KeptInts.ToList(), kept);
            }
        }

        [Fact]
        public void The_string_properties_liblcm_keeps_are_exactly_the_expected_ones()
        {
            // A packed value is rewritten on the way through, so "kept" for it means stored in some form.
            string[] samples = { "x", "abcdefgh", "\u0004abcdefgh", "\u0003\u0001\u0000", "\u0001\u0000\u0000\u0000" };
            foreach (bool styleRules in new[] { true, false })
            {
                var stored = StringNumbers.Where(tpt => samples.Any(s => Str(styleRules, tpt, s) is Outcome.Kept or Outcome.Changed)).ToList();
                Assert.Equal((styleRules ? KeptStringsInStyleRules : KeptStringsInRuns).ToList(), stored);
            }
        }
    }
}