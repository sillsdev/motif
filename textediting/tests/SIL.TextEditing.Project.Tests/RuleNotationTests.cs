using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;
using SIL.TextEditing.Markdown.Records;
using Xunit;

namespace SIL.TextEditing.Project.Tests
{
    public class RuleNotationTests : IClassFixture<RuleGrammar>
    {
        readonly RuleGrammar _g;

        public RuleNotationTests(RuleGrammar grammar) => _g = grammar;

        LcmCache Cache => _g.Cache;

        void RoundTrips(ICmObject owner, RuleField field, string expected, int? columns = null)
        {
            Assert.Equal(expected, RuleNotation.Render(owner, field, Cache));
            var parsed = RuleNotation.Parse(expected, field, Cache, columns);
            Assert.Equal(expected, RuleNotation.Render(parsed, Cache));
        }

        // ------------------------------------------------------------------ worked examples: affix processes

        [Fact]
        public void LiblcmDefaultProcessIsAGapCopiedAsOne()
        {
            IMoAffixProcess process = null!;
            _g.Fixture.Change(() => { _g.Entry(out process); _g.Copy(process, _g.Gap(process)); });
            RoundTrips(process, RuleField.AffixInput, "*");
            RoundTrips(process, RuleField.AffixOutput, "$1", 1);
        }

        [Fact]
        public void Example1PrefixInsertsTwoPhonemesAndCopiesTheRest()
        {
            IMoAffixProcess process = null!;
            _g.Fixture.Change(() =>
            {
                _g.Entry(out process);
                var gap = _g.Gap(process);
                _g.Insert(process, _g.P("m"));
                _g.Insert(process, _g.P("a"));
                _g.Copy(process, gap);
            });
            RoundTrips(process, RuleField.AffixInput, "*");
            RoundTrips(process, RuleField.AffixOutput, "m a $1", 1);
        }

        [Fact]
        public void Example2InfixAfterTheFirstConsonant()
        {
            IMoAffixProcess process = null!;
            _g.Fixture.Change(() =>
            {
                _g.Entry(out process);
                var c = _g.Nc(_g.Class("C"));
                process.InputOS.Add(c);
                var gap = _g.Gap(process);
                _g.Copy(process, c);
                _g.Insert(process, _g.P("u"));
                _g.Insert(process, _g.P("m"));
                _g.Copy(process, gap);
            });
            RoundTrips(process, RuleField.AffixInput, "[C] *");
            RoundTrips(process, RuleField.AffixOutput, "$1 u m $2", 2);
        }

        [Fact]
        public void Example3ReduplicationHasATwoContextColumnWithOneIndex()
        {
            IMoAffixProcess process = null!;
            _g.Fixture.Change(() =>
            {
                _g.Entry(out process);
                var column = _g.Sequence(_g.Pool(_g.Nc(_g.Class("C"))), _g.Pool(_g.Nc(_g.Class("V"))));
                process.InputOS.Add(column);
                var gap = _g.Gap(process);
                _g.Copy(process, column);
                _g.Copy(process, column);
                _g.Copy(process, gap);
            });
            RoundTrips(process, RuleField.AffixInput, "<[C] [V]> *");
            RoundTrips(process, RuleField.AffixOutput, "$1 $1 $2", 2);
        }

        [Fact]
        public void Example4ModifyIsAnonymousOrNamed()
        {
            IMoAffixProcess process = null!;
            _g.Fixture.Change(() =>
            {
                _g.Entry(out process);
                var c = _g.Nc(_g.Class("C"));
                process.InputOS.Add(c);
                var gap = _g.Gap(process);
                _g.Modify(process, c, _g.FeatureClass(null, ("voi", "+")));
                _g.Copy(process, gap);
            });
            RoundTrips(process, RuleField.AffixOutput, "$1~[voi:+] $2", 2);
            IMoAffixProcess named = null!;
            _g.Fixture.Change(() =>
            {
                _g.Entry(out named);
                var c = _g.Nc(_g.Class("C"));
                named.InputOS.Add(c);
                var gap = _g.Gap(named);
                _g.Modify(named, c, (IPhNCFeatures)_g.Class("Vd"));
                _g.Copy(named, gap);
            });
            RoundTrips(named, RuleField.AffixOutput, "$1~[Vd] $2", 2);
        }

        [Fact]
        public void Example5BoundaryAndClassInsertion()
        {
            IMoAffixProcess process = null!;
            _g.Fixture.Change(() =>
            {
                _g.Entry(out process);
                var gap = _g.Gap(process);
                var c = _g.Nc(_g.Class("C"));
                process.InputOS.Add(c);
                _g.Insert(process, _g.P("a"));
                _g.Insert(process, _g.MorphemeBoundary);
                _g.Copy(process, gap);
                _g.Copy(process, c);
                _g.InsertClass(process, _g.Class("V"));
            });
            RoundTrips(process, RuleField.AffixInput, "* [C]");
            RoundTrips(process, RuleField.AffixOutput, "a + $1 $2 [V]", 2);
        }

        [Fact]
        public void AnInsertOfSeveralUnitsIsOneBracketedStep()
        {
            IMoAffixProcess process = null!;
            _g.Fixture.Change(() =>
            {
                _g.Entry(out process);
                var gap = _g.Gap(process);
                _g.Insert(process, _g.P("m"), _g.P("a"));
                _g.Copy(process, gap);
            });
            RoundTrips(process, RuleField.AffixOutput, "<m a> $1", 1);
            Assert.Equal("m a $1", RuleNotation.Render(RuleNotation.Parse("<m> <a> $1", RuleField.AffixOutput, Cache, 1), Cache).Replace("<", "").Replace(">", ""));
        }

        // ------------------------------------------------------------------ worked examples: rules

        [Fact]
        public void Example6OneSegmentRuleWithClassContexts()
        {
            IPhSegRuleRHS rhs = null!;
            IPhRegularRule rule = null!;
            _g.Fixture.Change(() =>
            {
                rule = _g.Rule(out rhs);
                rule.StrucDescOS.Add(_g.Seg("p"));
                rhs.StrucChangeOS.Add(_g.Seg("b"));
                rhs.LeftContextOA = _g.Nc(_g.Class("V"));
                rhs.RightContextOA = _g.Nc(_g.Class("V"));
            });
            RoundTrips(rule, RuleField.StrucDesc, "p");
            RoundTrips(rhs, RuleField.StrucChange, "b");
            RoundTrips(rhs, RuleField.LeftContext, "[V]");
            RoundTrips(rhs, RuleField.RightContext, "[V]");
        }

        [Fact]
        public void Example7FeatureDefinedInputAndFeatureChange()
        {
            IPhSegRuleRHS rhs = null!;
            IPhRegularRule rule = null!;
            _g.Fixture.Change(() =>
            {
                rule = _g.Rule(out rhs);
                rule.StrucDescOS.Add(_g.Nc(_g.FeatureClass(null, ("son", "-"), ("cons", "+"))));
                rhs.StrucChangeOS.Add(_g.Nc(_g.FeatureClass(null, ("voi", "+"))));
            });
            RoundTrips(rule, RuleField.StrucDesc, "[cons:+ son:-]");
            RoundTrips(rhs, RuleField.StrucChange, "[voi:+]");
            IPhRegularRule named = null!;
            _g.Fixture.Change(() => { named = _g.Rule(out _); named.StrucDescOS.Add(_g.Nc(_g.Class("Obs"))); });
            RoundTrips(named, RuleField.StrucDesc, "[Obs]");
        }

        [Fact]
        public void Example8AlphaAgreementAcrossFieldsAndTheDisagreementVariant()
        {
            IPhSegRuleRHS rhs = null!;
            IPhRegularRule rule = null!;
            _g.Fixture.Change(() =>
            {
                rule = _g.Rule(out rhs);
                var voi = _g.Constraint("voi");
                rule.StrucDescOS.Add(_g.Nc(_g.FeatureClass(null, ("cons", "+"))));
                rhs.StrucChangeOS.Add(_g.Nc(_g.FeatureClass(null), new[] { voi }));
                rhs.RightContextOA = _g.Nc(_g.FeatureClass(null, ("cons", "+")), new[] { voi });
            });
            RoundTrips(rule, RuleField.StrucDesc, "[cons:+]");
            RoundTrips(rhs, RuleField.StrucChange, "[voi:α]");
            RoundTrips(rhs, RuleField.RightContext, "[cons:+ voi:α]");
            IPhSegRuleRHS variant = null!;
            IPhRegularRule dissimilation = null!;
            _g.Fixture.Change(() =>
            {
                dissimilation = _g.Rule(out variant);
                var voi = _g.Constraint("voi");
                variant.StrucChangeOS.Add(_g.Nc(_g.FeatureClass(null), new[] { voi }));
                variant.RightContextOA = _g.Nc(_g.FeatureClass(null, ("cons", "+")), null, new[] { voi });
            });
            RoundTrips(variant, RuleField.RightContext, "[cons:+ voi:-α]");
        }

        [Fact]
        public void LettersFollowTheRuleWideConstraintOrderNotTheField()
        {
            IPhSegRuleRHS rhs = null!;
            IPhRegularRule rule = null!;
            _g.Fixture.Change(() =>
            {
                rule = _g.Rule(out rhs);
                var voi = _g.Constraint("voi");
                var nas = _g.Constraint("nas");
                rule.StrucDescOS.Add(_g.Nc(_g.FeatureClass(null), new[] { nas }));
                rhs.StrucChangeOS.Add(_g.Nc(_g.FeatureClass(null), new[] { voi }));
                rhs.RightContextOA = _g.Nc(_g.FeatureClass(null), new[] { nas, voi });
            });
            RoundTrips(rule, RuleField.StrucDesc, "[nas:α]");
            RoundTrips(rhs, RuleField.StrucChange, "[voi:β]");
            RoundTrips(rhs, RuleField.RightContext, "[nas:α voi:β]");
        }

        [Fact]
        public void Example9OptionalAndRepeatedMembersInAContext()
        {
            IPhSegRuleRHS rhs = null!;
            _g.Fixture.Change(() =>
            {
                _g.Rule(out rhs);
                var members = _g.Sequence(
                    _g.Pool(_g.Bdry(_g.WordBoundary)),
                    _g.Pool(_g.Iteration(_g.Pool(_g.Nc(_g.Class("C"))), 1, 3)),
                    _g.Pool(_g.Iteration(_g.Pool(_g.Seg("a")), 0, 1)),
                    _g.Pool(_g.Nc(_g.Class("V"))));
                rhs.LeftContextOA = members;
            });
            RoundTrips(rhs, RuleField.LeftContext, "# [C]{1,3} (a) [V]");
        }

        [Fact]
        public void UnboundedRepeatIsWrittenWithInfinityAndReadWithAStar()
        {
            IPhSegRuleRHS rhs = null!;
            _g.Fixture.Change(() =>
            {
                _g.Rule(out rhs);
                rhs.RightContextOA = _g.Iteration(_g.Pool(_g.Nc(_g.Class("C"))), 0, -1);
            });
            RoundTrips(rhs, RuleField.RightContext, "[C]{0,∞}");
            Assert.Equal("[C]{0,∞}", RuleNotation.Render(RuleNotation.Parse("[C] { 0 , * }", RuleField.RightContext, Cache), Cache));
        }

        [Fact]
        public void Example10EscapesAndANamedClassWithAConstraint()
        {
            IPhSegRuleRHS rhs = null!;
            _g.Fixture.Change(() =>
            {
                _g.Rule(out rhs);
                var voi = _g.Constraint("voi");
                rhs.RightContextOA = _g.Sequence(
                    _g.Pool(_g.Nc(_g.Class("Obs"), new[] { voi })),
                    _g.Pool(_g.Seg("!")),
                    _g.Pool(_g.Seg("+")),
                    _g.Pool(_g.Bdry(_g.WordBoundary)));
            });
            RoundTrips(rhs, RuleField.RightContext, @"[Obs voi:α] ! \+ #");
        }

        [Fact]
        public void AnAbsentContextIsEmptyText()
        {
            IPhSegRuleRHS rhs = null!;
            _g.Fixture.Change(() => _g.Rule(out rhs));
            Assert.Equal("", RuleNotation.Render(rhs, RuleField.LeftContext, Cache));
            Assert.Empty(RuleNotation.Parse("  ", RuleField.LeftContext, Cache).Nodes);
        }

        [Fact]
        public void AOneMemberSequenceRendersAsTheBareMember()
        {
            IPhSegRuleRHS rhs = null!;
            _g.Fixture.Change(() =>
            {
                _g.Rule(out rhs);
                rhs.LeftContextOA = _g.Sequence(_g.Pool(_g.Seg("a")));
            });
            Assert.Equal("a", RuleNotation.Render(rhs, RuleField.LeftContext, Cache));
        }

        // ------------------------------------------------------------------ reading is white-space and escape tolerant

        [Theory]
        [InlineData(RuleField.AffixInput, "  <[C]   [V]>\t*  ", "<[C] [V]> *")]
        [InlineData(RuleField.AffixOutput, "$1 ~ [ voi:+ ]   $2", "$1~[voi:+] $2")]
        [InlineData(RuleField.AffixOutput, "$1~[voi:+] m", "$1~[voi:+] m")]
        [InlineData(RuleField.AffixOutput, "\\m \\a", "m a")]
        [InlineData(RuleField.LeftContext, "[C] {1 , 3}", "[C]{1,3}")]
        [InlineData(RuleField.LeftContext, "( a )", "(a)")]
        [InlineData(RuleField.LeftContext, "[ C ]{0,1}", "[C]{0,1}")]
        [InlineData(RuleField.RightContext, "[cons:+ son:-]", "[cons:+ son:-]")]
        [InlineData(RuleField.RightContext, "[son:- cons:+]", "[cons:+ son:-]")]
        [InlineData(RuleField.RightContext, "[Obs  voi:\\+]", null)]
        public void ReadingNormalizesToTheCanonicalText(RuleField field, string text, string? canonical)
        {
            if (canonical == null)
            {
                Assert.ThrowsAny<InvalidOperationException>(() => RuleNotation.Parse(text, field, Cache, 3));
                return;
            }
            Assert.Equal(canonical, RuleNotation.Render(RuleNotation.Parse(text, field, Cache, 3), Cache));
        }

        // ------------------------------------------------------------------ refusals the parser makes

        void Refuses(string code, string text, RuleField field, int? columns = null)
        {
            var failure = Assert.ThrowsAny<InvalidOperationException>(() => RuleNotation.Parse(text, field, Cache, columns));
            string actual = failure is RuleNotationException rule ? rule.Code : ((FeatureStructureNotationException)failure).Code;
            Assert.Equal(code, actual);
        }

        [Theory]
        [InlineData(RuleField.AffixInput, "<[C] [V]")]
        [InlineData(RuleField.AffixInput, "<>")]
        [InlineData(RuleField.AffixInput, "a\\")]
        [InlineData(RuleField.AffixInput, "a_b")]
        [InlineData(RuleField.AffixInput, ")")]
        [InlineData(RuleField.AffixInput, "$1")]
        [InlineData(RuleField.AffixOutput, "$")]
        [InlineData(RuleField.AffixOutput, "$x")]
        [InlineData(RuleField.AffixOutput, "$1[V]")]
        [InlineData(RuleField.AffixOutput, "$1a")]
        [InlineData(RuleField.AffixOutput, "$1~")]
        [InlineData(RuleField.AffixOutput, "~[voi:+]")]
        [InlineData(RuleField.AffixOutput, "<a")]
        [InlineData(RuleField.LeftContext, "[C]{1;3}")]
        [InlineData(RuleField.LeftContext, "[C]{1,3")]
        [InlineData(RuleField.LeftContext, "(a")]
        [InlineData(RuleField.LeftContext, "{1,2}")]
        [InlineData(RuleField.LeftContext, "a / b")]
        public void MalformedTextIsASyntaxRefusal(RuleField field, string text) => Refuses(RuleNotation.Syntax, text, field, 1);

        [Theory]
        [InlineData("[C]{0,0}")]
        [InlineData("[C]{3,2}")]
        [InlineData("[C]{2,0}")]
        [InlineData("[C]{99999999999,∞}")]
        public void ARepeatNeedsAMaximumOfOneAndAtLeastItsMinimum(string text) => Refuses(RuleNotation.BadRepeat, text, RuleField.RightContext);

        [Fact]
        public void AnUnknownCodeOrClassIsRefused()
        {
            Refuses(RuleNotation.UnknownPhoneme, "z", RuleField.StrucDesc);
            Refuses(RuleNotation.UnknownPhoneme, "ta", RuleField.LeftContext);
            Refuses(RuleNotation.UnknownNaturalClass, "[Q]", RuleField.StrucDesc);
            Refuses(FeatureStructureNotation.UnknownFeature, "[zzz:+]", RuleField.StrucDesc);
            Refuses(FeatureStructureNotation.UnknownValue, "[voi:maybe]", RuleField.StrucDesc);
            Refuses(FeatureStructureNotation.Syntax, "[voi", RuleField.StrucDesc);
            Refuses(FeatureStructureNotation.RepeatedFeature, "[voi:+ voi:-]", RuleField.StrucDesc);
            Refuses(FeatureStructureNotation.RepeatedFeature, "[voi:+ voi:α]", RuleField.StrucDesc);
        }

        [Fact]
        public void AnEscapedAlphaLetterIsAValueLabelNotAVariable()
        {
            Refuses(FeatureStructureNotation.UnknownValue, "[voi:\\α]", RuleField.StrucDesc);
            Refuses(FeatureStructureNotation.UnknownValue, "[voi:\\-α]", RuleField.StrucDesc);
        }

        [Fact]
        public void AlphaLettersMustNameOneFeatureEach()
        {
            var texts = new[] { RuleNotation.Parse("[voi:α]", RuleField.StrucChange, Cache), RuleNotation.Parse("[nas:α]", RuleField.RightContext, Cache) };
            Assert.Equal(RuleNotation.AlphaMismatch, Assert.Throws<RuleNotationException>(() => RuleNotation.CheckAlpha(texts)).Code);
            texts = new[] { RuleNotation.Parse("[voi:α]", RuleField.StrucChange, Cache), RuleNotation.Parse("[voi:-β]", RuleField.RightContext, Cache) };
            Assert.Equal(RuleNotation.AlphaMismatch, Assert.Throws<RuleNotationException>(() => RuleNotation.CheckAlpha(texts)).Code);
            Refuses(RuleNotation.AlphaMismatch, "[voi:α nas:α]", RuleField.StrucDesc);
            texts = new[] { RuleNotation.Parse("[voi:α]", RuleField.StrucChange, Cache), RuleNotation.Parse("[voi:-α]", RuleField.RightContext, Cache) };
            RuleNotation.CheckAlpha(texts);
        }

        [Theory]
        [InlineData("$0", 2)]
        [InlineData("$3", 2)]
        [InlineData("m $2 $3", 2)]
        [InlineData("$99999999999", 2)]
        public void ACopyNamesAColumnOfTheInput(string text, int columns) => Refuses(RuleNotation.IndexOutOfRange, text, RuleField.AffixOutput, columns);

        [Theory]
        [InlineData(RuleField.StrucDesc, "(a)")]
        [InlineData(RuleField.StrucDesc, "[C]{1,2}")]
        [InlineData(RuleField.StrucDesc, "<[C] [V]>")]
        [InlineData(RuleField.StrucDesc, "*")]
        [InlineData(RuleField.StrucChange, "(a)")]
        [InlineData(RuleField.AffixInput, "[C]{1,3}")]
        [InlineData(RuleField.AffixInput, "(a)")]
        [InlineData(RuleField.AffixInput, "[voi:α]")]
        [InlineData(RuleField.AffixInput, "[Obs voi:α]")]
        [InlineData(RuleField.AffixInput, "<* a>")]
        [InlineData(RuleField.AffixOutput, "*")]
        [InlineData(RuleField.AffixOutput, "(a)")]
        [InlineData(RuleField.AffixOutput, "<[C] a>")]
        [InlineData(RuleField.AffixOutput, "$1~[V]")]
        [InlineData(RuleField.LeftContext, "a #")]
        [InlineData(RuleField.LeftContext, "# #")]
        [InlineData(RuleField.RightContext, "# a")]
        [InlineData(RuleField.LeftContext, "*")]
        [InlineData(RuleField.LeftContext, "<a b>")]
        [InlineData(RuleField.LeftContext, "(a){1,2}")]
        [InlineData(RuleField.LeftContext, "a{1,2}{1,2}")]
        [InlineData(RuleField.LeftContext, "((a))")]
        [InlineData(RuleField.LeftContext, "#{1,2}")]
        public void AFieldRefusesAConstructItCannotHold(RuleField field, string text) => Refuses(RuleNotation.ContextNotAllowed, text, field, 2);

        [Fact]
        public void TheWordBoundaryIsAtTheOuterEdgeOnly()
        {
            RuleNotation.Parse("# a b", RuleField.LeftContext, Cache);
            RuleNotation.Parse("a b #", RuleField.RightContext, Cache);
            RuleNotation.Parse("a + b", RuleField.LeftContext, Cache);
        }

        [Fact]
        public void ANamedClassTakesAlphaVariablesNotFeatureValues()
            => Refuses(RuleNotation.Syntax, "[Obs voi:+]", RuleField.RightContext);

        [Fact]
        public void ACodeLookingLikeABoundaryNeedsAnEscape()
        {
            var plus = RuleNotation.Parse("\\+", RuleField.LeftContext, Cache);
            Assert.Equal("!", RuleNotation.Render(RuleNotation.Parse("\\!", RuleField.StrucDesc, Cache), Cache));
            Assert.Equal("\\+", RuleNotation.Render(plus, Cache));
            Assert.NotEqual(RuleNotation.Render(RuleNotation.Parse("+", RuleField.LeftContext, Cache), Cache), RuleNotation.Render(plus, Cache));
        }

        // ------------------------------------------------------------------ refusals the renderer makes

        [Fact]
        public void AnUnrenderableValueIsRefusedWithAnUnrenderableCode()
        {
            IMoAffixProcess process = null!;
            IPhSegRuleRHS rhs = null!;
            IPhRegularRule rule = null!;
            _g.Fixture.Change(() =>
            {
                _g.Entry(out process);
                var gap = _g.Gap(process);
                _g.Copy(process, null);
                _g.Copy(process, gap);
                rule = _g.Rule(out rhs);
                rule.StrucDescOS.Add(_g.Nc(null));
                rhs.StrucChangeOS.Add(_g.Nc(_g.Class("V"), new[] { _g.Constraint("voi") }));
                rhs.LeftContextOA = _g.Sequence();
            });
            string Code(ICmObject owner, RuleField field) => Assert.Throws<RuleNotationException>(() => RuleNotation.Render(owner, field, Cache)).Code;
            Assert.Equal(RuleNotation.Unrenderable, Code(process, RuleField.AffixOutput));
            Assert.Equal(RuleNotation.Unrenderable, Code(rule, RuleField.StrucDesc));
            Assert.Equal(RuleNotation.Unrenderable, Code(rhs, RuleField.LeftContext));
            Assert.Equal("[V voi:α]", RuleNotation.Render(rhs, RuleField.StrucChange, Cache));
        }

        [Fact]
        public void AnAffixClassWithAConstraintIsUnrenderable()
        {
            IMoAffixProcess process = null!;
            _g.Fixture.Change(() =>
            {
                _g.Entry(out process);
                process.InputOS.Add(_g.Nc(_g.Class("V"), new[] { _g.Constraint("voi") }));
            });
            Assert.Equal(RuleNotation.Unrenderable, Assert.Throws<RuleNotationException>(() => RuleNotation.Render(process, RuleField.AffixInput, Cache)).Code);
        }

        [Fact]
        public void ACopyOfAnotherProcessesColumnIsUnrenderable()
        {
            IMoAffixProcess first = null!, second = null!;
            _g.Fixture.Change(() =>
            {
                _g.Entry(out first);
                _g.Entry(out second);
                var foreign = _g.Gap(first);
                _g.Gap(second);
                _g.Copy(second, foreign);
            });
            Assert.Equal(RuleNotation.Unrenderable, Assert.Throws<RuleNotationException>(() => RuleNotation.Render(second, RuleField.AffixOutput, Cache)).Code);
        }

        [Fact]
        public void TwoConstraintsOnOneFeatureAreUnrenderable()
        {
            IPhSegRuleRHS rhs = null!;
            _g.Fixture.Change(() =>
            {
                _g.Rule(out rhs);
                rhs.RightContextOA = _g.Nc(_g.FeatureClass(null), new[] { _g.Constraint("voi"), _g.Constraint("voi") });
            });
            Assert.Equal(RuleNotation.Unrenderable, Assert.Throws<RuleNotationException>(() => RuleNotation.Render(rhs, RuleField.RightContext, Cache)).Code);
        }

        [Fact]
        public void MoreThanTwentyFourConstraintsAreUnrenderable()
        {
            IPhSegRuleRHS rhs = null!;
            _g.Fixture.Change(() =>
            {
                _g.Rule(out rhs);
                var constraints = Enumerable.Range(0, 25).Select(_ => _g.Constraint("voi")).ToArray();
                rhs.RightContextOA = _g.Nc(_g.FeatureClass(null), constraints);
            });
            Assert.Equal(RuleNotation.Unrenderable, Assert.Throws<RuleNotationException>(() => RuleNotation.Render(rhs, RuleField.RightContext, Cache)).Code);
        }

        [Fact]
        public void AMisplacedWordBoundaryAndARepeatedOneAreUnrenderable()
        {
            IPhSegRuleRHS rhs = null!;
            _g.Fixture.Change(() =>
            {
                _g.Rule(out rhs);
                rhs.LeftContextOA = _g.Sequence(_g.Pool(_g.Seg("a")), _g.Pool(_g.Bdry(_g.WordBoundary)));
            });
            Assert.Equal(RuleNotation.Unrenderable, Assert.Throws<RuleNotationException>(() => RuleNotation.Render(rhs, RuleField.LeftContext, Cache)).Code);
        }

        [Fact]
        public void AMetathesisRuleHasNoStructuralDescriptionText()
        {
            IPhMetathesisRule rule = null!;
            _g.Fixture.Change(() =>
            {
                rule = Cache.ServiceLocator.GetInstance<IPhMetathesisRuleFactory>().Create();
                _g.Data.PhonRulesOS.Add(rule);
            });
            Assert.Equal(RuleNotation.Unrenderable, Assert.Throws<RuleNotationException>(() => RuleNotation.Render(rule, RuleField.StrucDesc, Cache)).Code);
        }

        [Fact]
        public void AFieldNeedsItsOwnerClass()
            => Assert.Throws<ArgumentException>(() => RuleNotation.Render(_g.P("a"), RuleField.AffixInput, Cache));

        // ------------------------------------------------------------------ ambiguity

        [Fact]
        public void TwoPhonemesSharingACodeOrTwoClassesSharingAnAbbreviationAreAmbiguous()
        {
            using var other = new RuleGrammar();
            IPhRegularRule rule = null!;
            other.Fixture.Change(() =>
            {
                rule = other.Rule(out _);
                rule.StrucDescOS.Add(other.Seg("k"));
                other.AddCode(other.P("t"), "k");
            });
            Assert.Equal(RuleNotation.AmbiguousLabel, Assert.Throws<RuleNotationException>(() => RuleNotation.Render(rule, RuleField.StrucDesc, other.Cache)).Code);
            Assert.Equal(RuleNotation.AmbiguousLabel, Assert.Throws<RuleNotationException>(() => RuleNotation.Parse("k", RuleField.StrucDesc, other.Cache)).Code);
            using var classes = new RuleGrammar();
            IPhRegularRule classRule = null!;
            classes.Fixture.Change(() =>
            {
                classRule = classes.Rule(out _);
                classRule.StrucDescOS.Add(classes.Nc(classes.Class("V")));
                classes.FeatureClass("V", ("voi", "+"));
            });
            Assert.Equal(RuleNotation.AmbiguousLabel, Assert.Throws<RuleNotationException>(() => RuleNotation.Render(classRule, RuleField.StrucDesc, classes.Cache)).Code);
            Assert.Equal(RuleNotation.AmbiguousLabel, Assert.Throws<RuleNotationException>(() => RuleNotation.Parse("[V]", RuleField.StrucDesc, classes.Cache)).Code);
        }

        [Fact]
        public void ASecondaryCodeIsReadAndTheFirstIsWritten()
        {
            using var other = new RuleGrammar();
            IPhRegularRule rule = null!;
            other.Fixture.Change(() =>
            {
                rule = other.Rule(out _);
                rule.StrucDescOS.Add(other.Seg("p"));
                other.AddCode(other.P("p"), "ph");
            });
            Assert.Equal("p", RuleNotation.Render(rule, RuleField.StrucDesc, other.Cache));
            Assert.Equal("p", RuleNotation.Render(RuleNotation.Parse("ph", RuleField.StrucDesc, other.Cache), other.Cache));
        }

        // ------------------------------------------------------------------ the field table

        [Theory]
        [InlineData("MoAffixProcess", "Input", true)]
        [InlineData("MoAffixProcess", "Output", true)]
        [InlineData("PhSegmentRule", "StrucDesc", true)]
        [InlineData("PhSegRuleRHS", "StrucChange", true)]
        [InlineData("PhSegRuleRHS", "LeftContext", true)]
        [InlineData("PhSegRuleRHS", "RightContext", true)]
        [InlineData("PhSegRuleRHS", "InputPOSes", false)]
        [InlineData("MoInsertPhones", "Content", false)]
        public void SixFieldsAreRuleFormulaText(string cls, string field, bool expected) =>
            Assert.Equal(expected, RuleNotation.TryFieldOf(cls, field, out _));

        // ------------------------------------------------------------------ the feature-structure text does not change

        [Fact]
        public void FeatureStructureTextOfAnotherSystemUsesTheSameForm()
        {
            IFsFeatStruc structure = null!;
            _g.Fixture.Change(() => structure = _g.FeatureClass(null, ("voi", "+"), ("cons", "-")).FeaturesOA);
            Assert.Equal("[cons:- voi:+]", FeatureStructureNotation.Render(structure, Cache, Cache.LangProject.PhFeatureSystemOA));
            var failure = Assert.Throws<FeatureStructureNotationException>(() => FeatureStructureNotation.Render(structure, Cache));
            Assert.Equal(FeatureStructureNotation.Unrenderable, failure.Code);
            Assert.Contains("morphosyntactic", failure.Message);
        }

        // ------------------------------------------------------------------ record export

        static string Scalar(RecordObject owner, string key) => ((RecordScalar)owner[key]!).Text;

        ProjectRecordExporter Exporter() => new ProjectRecordExporter(Cache, ProjectRecordSchema.Generate(Cache, ProjectRecordSchema.LexiconEntryOverlay));

        static RecordObject Process(ProjectRecordExport export) => ((RecordList)export.Document.Data["allomorphs"]!).Items.OfType<RecordObject>().Single();

        [Fact]
        public void AnAffixProcessExportsItsInputAndOutputAsText()
        {
            ILexEntry entry = null!;
            _g.Fixture.Change(() =>
            {
                entry = _g.Entry(out var process);
                var c = _g.Nc(_g.Class("C"));
                process.InputOS.Add(c);
                var gap = _g.Gap(process);
                _g.Modify(process, c, _g.FeatureClass(null, ("voi", "+")));
                _g.Insert(process, _g.P("a"), _g.MorphemeBoundary);
                _g.Copy(process, gap);
            });
            var exporter = Exporter();
            var export = exporter.Export(entry.Guid);
            Assert.DoesNotContain(export.Unexported, u => u.Field == "Input" || u.Field == "Output");
            var process2 = Process(export);
            Assert.Equal("[C] *", Scalar(process2, "input"));
            Assert.Equal("$1~[voi:+] <a +> $2", Scalar(process2, "output"));
            var read = RecordFileReader.Read(export.Text!, exporter.Schema, exporter.CreateReadOptions());
            Assert.DoesNotContain(read.Diagnostics, d => d.Severity == SIL.TextEditing.Markdown.MarkdownSeverity.Error);
            Assert.True(RecordDocument.AreEquivalent(export.Document, read.Document!, exporter.Schema));
        }

        [Fact]
        public void AnUnrenderableProcessFieldIsListedAndTheOthersExport()
        {
            ILexEntry entry = null!;
            _g.Fixture.Change(() =>
            {
                entry = _g.Entry(out var process);
                _g.Gap(process);
                _g.Copy(process, null);
            });
            var export = Exporter().Export(entry.Guid);
            var unexported = Assert.Single(export.Unexported, u => u.Field == "Output");
            Assert.Contains(RuleNotation.Unrenderable, unexported.Reason);
            var process2 = Process(export);
            Assert.Equal("*", Scalar(process2, "input"));
            Assert.Null(process2["output"]);
        }

        [Fact]
        public void AnEmptyFieldIsLeftOut()
        {
            ILexEntry entry = null!;
            _g.Fixture.Change(() => entry = _g.Entry(out _));
            var process = Process(Exporter().Export(entry.Guid));
            Assert.Null(process["input"]);
            Assert.Null(process["output"]);
        }
    }
}
