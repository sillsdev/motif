using System;
using System.Collections.Generic;
using System.Linq;
using SIL.LCModel;

namespace SIL.TextEditing.Project
{
    /// <summary>A failure to render or read a rule-formula text, with a stable code (<c>rule-notation.*</c>) a host can show or map.</summary>
    public sealed class RuleNotationException : InvalidOperationException
    {
        internal RuleNotationException(string code, string message) : base(message) => Code = code;

        /// <summary>One of the <see cref="RuleNotation"/> code constants.</summary>
        public string Code { get; }
    }

    /// <summary>The six text fields of the rule-formula notation; the field chooses the start symbol of the grammar.</summary>
    public enum RuleField
    {
        /// <summary><c>MoAffixProcess.Input</c>: columns, a gap <c>*</c> in any of them.</summary>
        AffixInput,
        /// <summary><c>MoAffixProcess.Output</c>: copies, modifications, inserted classes and inserted units.</summary>
        AffixOutput,
        /// <summary><c>PhSegmentRule.StrucDesc</c>: a flat list of simple contexts.</summary>
        StrucDesc,
        /// <summary><c>PhSegRuleRHS.StrucChange</c>: a flat list of simple contexts.</summary>
        StrucChange,
        /// <summary><c>PhSegRuleRHS.LeftContext</c>: nothing, one context, or a sequence.</summary>
        LeftContext,
        /// <summary><c>PhSegRuleRHS.RightContext</c>: nothing, one context, or a sequence.</summary>
        RightContext,
    }

    /// <summary>One node of a parsed rule-formula text. Labels in the text are already resolved against the project.</summary>
    public abstract class RuleNode { internal RuleNode() { } }

    /// <summary>A phoneme or boundary marker, by code; <c>#</c> and <c>+</c> are the standard markers.</summary>
    public sealed class RuleUnit : RuleNode
    {
        internal RuleUnit(IPhTerminalUnit unit) => Unit = unit;

        /// <summary>The phoneme or boundary marker.</summary>
        public IPhTerminalUnit Unit { get; }
    }

    /// <summary>The stem variable <c>*</c> of an affix input column.</summary>
    public sealed class RuleGap : RuleNode { internal RuleGap() { } }

    /// <summary>Several simple contexts in one affix input column <c>&lt;a b&gt;</c>.</summary>
    public sealed class RuleGroup : RuleNode
    {
        internal RuleGroup(IReadOnlyList<RuleNode> members) => Members = members;

        /// <summary>The members, each a <see cref="RuleUnit"/> or <see cref="RuleClass"/>.</summary>
        public IReadOnlyList<RuleNode> Members { get; }
    }

    /// <summary>A member repeated between <see cref="Minimum"/> and <see cref="Maximum"/> times; <c>(x)</c> is 0 to 1.</summary>
    public sealed class RuleRepeat : RuleNode
    {
        internal RuleRepeat(RuleNode member, int minimum, int maximum) { Member = member; Minimum = minimum; Maximum = maximum; }

        /// <summary>The repeated simple context.</summary>
        public RuleNode Member { get; }
        /// <summary>The least count, 0 or more.</summary>
        public int Minimum { get; }
        /// <summary>The greatest count, or -1 for no limit.</summary>
        public int Maximum { get; }
    }

    /// <summary>One alpha variable on a feature: <c>voi:α</c> agrees and <c>voi:-α</c> disagrees.</summary>
    public sealed class RuleAlpha
    {
        internal RuleAlpha(IFsClosedFeature feature, char letter, bool disagree) { Feature = feature; Letter = letter; Disagree = disagree; }

        /// <summary>The phonological feature the variable constrains.</summary>
        public IFsClosedFeature Feature { get; }
        /// <summary>The Greek letter as written; the canonical letter is the constraint's place in the rule.</summary>
        public char Letter { get; }
        /// <summary>True for <c>-α</c> (a minus constraint).</summary>
        public bool Disagree { get; }
    }

    /// <summary>A bracket: a named class (<c>[V]</c>, <c>[Obs voi:α]</c>) or a feature class (<c>[cons:+ voi:-]</c>).</summary>
    public sealed class RuleClass : RuleNode
    {
        internal RuleClass(IPhNaturalClass? named, IReadOnlyList<FeatureStructureSelection> features, IReadOnlyList<RuleAlpha> alphas)
        { NamedClass = named; Features = features; Alphas = alphas; }

        /// <summary>The class the abbreviation names, or null for a feature class written out.</summary>
        public IPhNaturalClass? NamedClass { get; }
        /// <summary>The closed phonological feature values of an unnamed feature class.</summary>
        public IReadOnlyList<FeatureStructureSelection> Features { get; }
        /// <summary>The alpha variables, in the order written.</summary>
        public IReadOnlyList<RuleAlpha> Alphas { get; }
    }

    /// <summary><c>$n</c>: the nth column of the process's input.</summary>
    public sealed class RuleCopy : RuleNode
    {
        internal RuleCopy(int index) => Index = index;

        /// <summary>The 1-based column.</summary>
        public int Index { get; }
    }

    /// <summary><c>$n~[class]</c>: column n, changed by a feature class.</summary>
    public sealed class RuleModify : RuleNode
    {
        internal RuleModify(int index, RuleClass modification) { Index = index; Modification = modification; }

        /// <summary>The 1-based column.</summary>
        public int Index { get; }
        /// <summary>The feature class that alters it.</summary>
        public RuleClass Modification { get; }
    }

    /// <summary>One inserted object: the units of a single <c>MoInsertPhones</c>.</summary>
    public sealed class RuleInsertUnits : RuleNode
    {
        internal RuleInsertUnits(IReadOnlyList<RuleUnit> units) => Units = units;

        /// <summary>The inserted phonemes or markers, in order.</summary>
        public IReadOnlyList<RuleUnit> Units { get; }
    }

    /// <summary>A parsed rule-formula text. The nodes are the top-level items in order; two or more context members are a sequence.</summary>
    public sealed class RuleText
    {
        internal RuleText(RuleField field, IReadOnlyList<RuleNode> nodes) { Field = field; Nodes = nodes; }

        /// <summary>The field the text was read as.</summary>
        public RuleField Field { get; }
        /// <summary>The items in order.</summary>
        public IReadOnlyList<RuleNode> Nodes { get; }
    }

    /// <summary>The text form of a rule formula: an affix process's input and output, and a phonological rule's structural description, structural
    /// change and left and right contexts (<c>notation="rule-formula"</c>).
    /// <para>Tokens are separated by white space on reading and by one space on writing, and no meaning depends on how much white space there is.
    /// Outside brackets a phoneme or marker is written by its code (the first non-blank code in the default vernacular writing system, then the other
    /// current vernacular writing systems), <c>#</c> and <c>+</c> alone are the standard word and morpheme markers, and a literal escapes with a backslash
    /// white space and each of <c>\ [ ] ( ) { } &lt; &gt; # + * _ / | $ ~</c>. Inside brackets the text is the feature-structure notation: the escapes
    /// are a backslash, <c>[</c>, <c>]</c>, <c>:</c> and white space, plus a value label that is a lone Greek letter or a minus and one, which writes its
    /// first character escaped. A reader accepts a redundant escape in either place and a writer never writes one.</para>
    /// <para>Grammar: an affix input is columns (<c>*</c>, a simple context, or <c>&lt;a b&gt;</c> for several in one column); an affix output is steps
    /// (<c>$n</c> copy, <c>$n~[class]</c> modify, <c>[class]</c> insert a class, a unit or <c>&lt;u v&gt;</c> insert phones); a structural description or
    /// change is simple contexts; a left or right context is members, where a member is a simple context, <c>(x)</c> for 0 to 1, or <c>x{m,n}</c> with
    /// <c>∞</c> (read <c>*</c> too) for no limit. A simple context is a unit or a bracket. A bracket is an optional class abbreviation then
    /// <c>feature:value</c> pairs in ordinal order of the feature label; a pair's value may be an alpha variable <c>α</c> to <c>ω</c>, or <c>-α</c> to
    /// <c>-ω</c> for disagreement.</para>
    /// <para>Alpha letters follow the rule's feature-constraint list, the order a FieldWorks rule editor shows, so a letter depends on the whole rule.
    /// On reading each letter must name one feature and each feature one letter in the rule, and the canonical letters are written again on the next render.
    /// A named class with a constraint, <c>[Obs voi:α]</c>, shows constraints the FieldWorks editor hides.</para>
    /// <para>One text has two readings that are not told apart: a sequence of one member and the bare member, and a one-column group and its bare member.
    /// Reading creates the bare member.</para>
    /// <para>Codes (<c>rule-notation.*</c>): <see cref="Syntax"/>, <see cref="BadRepeat"/>, <see cref="UnknownPhoneme"/>, <see cref="UnknownNaturalClass"/>,
    /// <see cref="AmbiguousLabel"/>, <see cref="AlphaMismatch"/>, <see cref="IndexOutOfRange"/>, <see cref="ContextNotAllowed"/>, <see cref="Unrenderable"/>,
    /// <see cref="ReadOnly"/>. A bracket's own failures keep the <c>fs-notation.*</c> codes of <see cref="FeatureStructureNotation"/>.</para></summary>
    public static class RuleNotation
    {
        /// <summary>The notation name a record schema's <c>notation=</c> attribute gives.</summary>
        public const string Name = "rule-formula";

        /// <summary>The text is not well formed outside a bracket.</summary>
        public const string Syntax = "rule-notation.syntax";
        /// <summary>A repeat count is not a usable minimum and maximum.</summary>
        public const string BadRepeat = "rule-notation.bad-repeat";
        /// <summary>No phoneme or marker of the first phoneme set has the code.</summary>
        public const string UnknownPhoneme = "rule-notation.unknown-phoneme";
        /// <summary>No natural class has the abbreviation.</summary>
        public const string UnknownNaturalClass = "rule-notation.unknown-natural-class";
        /// <summary>Two phonemes or markers share a code, or two classes share an abbreviation.</summary>
        public const string AmbiguousLabel = "rule-notation.ambiguous-label";
        /// <summary>One alpha letter names two features, or one feature has two letters, in a rule.</summary>
        public const string AlphaMismatch = "rule-notation.alpha-mismatch";
        /// <summary><c>$n</c> names no column of the input.</summary>
        public const string IndexOutOfRange = "rule-notation.index-out-of-range";
        /// <summary>The field cannot hold the construct the text uses.</summary>
        public const string ContextNotAllowed = "rule-notation.context-not-allowed";
        /// <summary>The project's value holds something the text cannot show.</summary>
        public const string Unrenderable = "rule-notation.unrenderable";
        /// <summary>The field is shown as text and an import does not write it.</summary>
        public const string ReadOnly = "rule-notation.read-only";

        /// <summary>The notation field a metadata class and field name stand for, or false.</summary>
        public static bool TryFieldOf(string declaringClass, string field, out RuleField result)
        {
            switch (declaringClass + "." + field)
            {
                case "MoAffixProcess.Input": result = RuleField.AffixInput; return true;
                case "MoAffixProcess.Output": result = RuleField.AffixOutput; return true;
                case "PhSegmentRule.StrucDesc": result = RuleField.StrucDesc; return true;
                case "PhSegRuleRHS.StrucChange": result = RuleField.StrucChange; return true;
                case "PhSegRuleRHS.LeftContext": result = RuleField.LeftContext; return true;
                case "PhSegRuleRHS.RightContext": result = RuleField.RightContext; return true;
                default: result = default; return false;
            }
        }

        /// <summary>The canonical text of one field of <paramref name="owner"/>: a <c>MoAffixProcess</c> for the two affix fields, a <c>PhSegmentRule</c> for
        /// <see cref="RuleField.StrucDesc"/>, a <c>PhSegRuleRHS</c> for the other three. An empty text is a field that holds nothing.</summary>
        /// <exception cref="RuleNotationException">The value holds something the text cannot show (<see cref="Unrenderable"/>) or two things share a label
        /// (<see cref="AmbiguousLabel"/>).</exception>
        public static string Render(ICmObject owner, RuleField field, LcmCache cache)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            return new RuleRenderer(cache).Render(owner, field);
        }

        /// <summary>The canonical text of each element of an affix process's <see cref="RuleField.AffixInput"/> (one per context or variable) or
        /// <see cref="RuleField.AffixOutput"/> (one per mapping), in order; the elements joined by one space are the text <see cref="Render(ICmObject, RuleField, LcmCache)"/> gives.</summary>
        /// <exception cref="RuleNotationException">As <see cref="Render(ICmObject, RuleField, LcmCache)"/>.</exception>
        public static IReadOnlyList<string> RenderElements(ICmObject owner, RuleField field, LcmCache cache)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            return new RuleRenderer(cache).Elements(owner, field);
        }

        /// <summary>The canonical text of each top-level item of a parsed text, in order, as <see cref="RenderElements(ICmObject, RuleField, LcmCache)"/> gives it for the same item stored.</summary>
        /// <exception cref="RuleNotationException">As <see cref="Render(RuleText, LcmCache)"/>.</exception>
        public static IReadOnlyList<string> RenderElements(RuleText text, LcmCache cache)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            return new RuleRenderer(cache).Elements(text);
        }

        /// <summary>The canonical text of a parsed text, with its alpha letters as written; reading then writing a canonical text gives it back.</summary>
        /// <exception cref="RuleNotationException">A phoneme or class of the text has no usable label (<see cref="Unrenderable"/>).</exception>
        public static string Render(RuleText text, LcmCache cache)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            return new RuleRenderer(cache).Nodes(text);
        }

        /// <summary>Reads <paramref name="text"/> as <paramref name="field"/> against the project. <paramref name="inputColumns"/> bounds <c>$n</c> in an
        /// affix output: pass the number of input columns as they will stand.</summary>
        /// <exception cref="RuleNotationException">The text is not well formed, names something the project lacks, or holds a construct the field cannot.</exception>
        /// <exception cref="FeatureStructureNotationException">A bracket is not well formed or names an unknown or ambiguous feature or value.</exception>
        public static RuleText Parse(string text, RuleField field, LcmCache cache, int? inputColumns = null)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            var result = new RuleParser(text, field, cache, inputColumns).Parse();
            CheckAlpha(new[] { result });
            return result;
        }

        /// <summary>Checks the alpha letters of the texts of one rule together: each letter names one feature and each feature has one letter.</summary>
        /// <exception cref="RuleNotationException"><see cref="AlphaMismatch"/>.</exception>
        public static void CheckAlpha(IEnumerable<RuleText> texts)
        {
            if (texts == null) throw new ArgumentNullException(nameof(texts));
            var byLetter = new Dictionary<char, Guid>();
            var byFeature = new Dictionary<Guid, char>();
            foreach (var alpha in texts.SelectMany(t => RuleParser.Classes(t.Nodes)).SelectMany(c => c.Alphas))
            {
                if (byLetter.TryGetValue(alpha.Letter, out var feature) && feature != alpha.Feature.Guid)
                    throw new RuleNotationException(AlphaMismatch, "The letter " + alpha.Letter + " names more than one feature in the rule.");
                if (byFeature.TryGetValue(alpha.Feature.Guid, out var letter) && letter != alpha.Letter)
                    throw new RuleNotationException(AlphaMismatch, "A feature has the letters " + letter + " and " + alpha.Letter + " in the rule.");
                byLetter[alpha.Letter] = alpha.Feature.Guid;
                byFeature[alpha.Feature.Guid] = alpha.Letter;
            }
        }

        internal const string Letters = "αβγδεζηθικλμνξοπρστυφχψω";
    }
}
