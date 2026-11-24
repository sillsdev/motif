using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SIL.LCModel;

namespace SIL.TextEditing.Project
{
    /// <summary>Writes the canonical rule-formula text of a field.</summary>
    internal sealed class RuleRenderer
    {
        const string LiteralReserved = "\\[](){}<>#+*_/|$~";
        const int MostLetters = 24;
        readonly LcmCache _cache;
        readonly RuleInventory _inventory;
        List<IPhFeatureConstraint>? _constraints;
        IFsFeatureSystem? PhonologicalSystem => _cache.LangProject.PhFeatureSystemOA;

        internal RuleRenderer(LcmCache cache)
        {
            _cache = cache;
            _inventory = new RuleInventory(cache);
        }

        static RuleNotationException Bad(string why) => new RuleNotationException(RuleNotation.Unrenderable, why);

        internal string Render(ICmObject owner, RuleField field)
        {
            try
            {
                switch (field)
                {
                    case RuleField.AffixInput: return AffixInput(Require<IMoAffixProcess>(owner, field));
                    case RuleField.AffixOutput: return AffixOutput(Require<IMoAffixProcess>(owner, field));
                    case RuleField.StrucDesc:
                        var rule = Require<IPhSegmentRule>(owner, field);
                        UseRule(rule);
                        return string.Join(" ", rule.StrucDescOS.Select(Simple));
                    case RuleField.StrucChange:
                        var change = Require<IPhSegRuleRHS>(owner, field);
                        UseRule(change.Owner);
                        return string.Join(" ", change.StrucChangeOS.Select(Simple));
                    case RuleField.LeftContext:
                        var left = Require<IPhSegRuleRHS>(owner, field);
                        UseRule(left.Owner);
                        return Context(left.LeftContextOA, true);
                    case RuleField.RightContext:
                        var right = Require<IPhSegRuleRHS>(owner, field);
                        UseRule(right.Owner);
                        return Context(right.RightContextOA, false);
                    default: throw new ArgumentOutOfRangeException(nameof(field));
                }
            }
            catch (FeatureStructureNotationException e)
            {
                throw new RuleNotationException(e.Code == FeatureStructureNotation.AmbiguousLabel ? RuleNotation.AmbiguousLabel : RuleNotation.Unrenderable, e.Message);
            }
        }

        static T Require<T>(ICmObject owner, RuleField field) where T : class =>
            owner as T ?? throw new ArgumentException("A " + field + " field belongs to a " + typeof(T).Name + ", not a " + owner.ClassName + ".", nameof(owner));

        void UseRule(ICmObject? owner)
        {
            if (!(owner is IPhRegularRule rule))
                throw Bad("Only a regular rule has text for this field.");
            var constraints = rule.FeatureConstraints.ToList();
            if (constraints.Count > MostLetters)
                throw Bad("The rule has " + constraints.Count.ToString(CultureInfo.InvariantCulture) + " feature constraints and the text has " + MostLetters.ToString(CultureInfo.InvariantCulture) + " letters.");
            var features = new HashSet<Guid>();
            foreach (var constraint in constraints)
            {
                if (constraint.FeatureRA == null) throw Bad("A feature constraint has no feature.");
                if (!features.Add(constraint.FeatureRA.Guid)) throw Bad("The rule has two constraints on one feature.");
            }
            _constraints = constraints;
        }

        // ------------------------------------------------------------------ affix process

        string AffixInput(IMoAffixProcess process) => string.Join(" ", process.InputOS.Select(Column));

        string Column(IPhContextOrVar item)
        {
            switch (item)
            {
                case IPhVariable _: return "*";
                case IPhSequenceContext sequence:
                    if (sequence.MembersRS.Count == 0) throw Bad("An input column holds an empty sequence.");
                    var members = sequence.MembersRS.Select(m => m is IPhSimpleContext simple ? Simple(simple) : throw Bad("An input column holds a " + m.ClassName + ".")).ToList();
                    return members.Count == 1 ? members[0] : "<" + string.Join(" ", members) + ">";
                case IPhSimpleContext simpleContext: return Simple(simpleContext);
                default: throw Bad("An input column holds a " + item.ClassName + ", which the text does not show.");
            }
        }

        string AffixOutput(IMoAffixProcess process)
        {
            var input = process.InputOS.ToList();
            var steps = new List<string>();
            foreach (var step in process.OutputOS)
            {
                switch (step)
                {
                    case IMoCopyFromInput copy:
                        steps.Add("$" + Index(input, copy.ContentRA));
                        break;
                    case IMoModifyFromInput modify:
                        if (modify.ModificationRA == null) throw Bad("A modification names no class.");
                        steps.Add("$" + Index(input, modify.ContentRA) + "~" + ClassText(modify.ModificationRA, new List<(string, string)>()));
                        break;
                    case IMoInsertNC insertClass:
                        if (insertClass.ContentRA == null) throw Bad("An insertion names no class.");
                        steps.Add(ClassText(insertClass.ContentRA, new List<(string, string)>()));
                        break;
                    case IMoInsertPhones insert:
                        if (insert.ContentRS.Count == 0) throw Bad("An insertion has no phonemes.");
                        var units = insert.ContentRS.Select(Unit).ToList();
                        steps.Add(units.Count == 1 ? units[0] : "<" + string.Join(" ", units) + ">");
                        break;
                    default: throw Bad("An output holds a " + step.ClassName + ", which the text does not show.");
                }
            }
            return string.Join(" ", steps);
        }

        static string Index(List<IPhContextOrVar> input, IPhContextOrVar? content)
        {
            if (content == null) throw Bad("A copy names no input column.");
            int at = input.IndexOf(content);
            if (at < 0) throw Bad("A copy names something that is not a column of this process's input.");
            return (at + 1).ToString(CultureInfo.InvariantCulture);
        }

        // ------------------------------------------------------------------ contexts

        string Context(IPhPhonContext? context, bool left)
        {
            if (context == null) return string.Empty;
            var members = context is IPhSequenceContext sequence ? sequence.MembersRS.Cast<IPhPhonContext>().ToList() : new List<IPhPhonContext> { context };
            if (members.Count == 0) throw Bad("A context holds an empty sequence.");
            var texts = new List<string>();
            for (int i = 0; i < members.Count; i++)
            {
                if (members[i] is IPhSimpleContextBdry boundary && boundary.FeatureStructureRA is { } marker && RuleInventory.IsWordBoundary(marker)
                    && !(left ? i == 0 : i == members.Count - 1))
                    throw Bad("The word boundary is not at the outer edge of the context.");
                texts.Add(Member(members[i]));
            }
            return string.Join(" ", texts);
        }

        string Member(IPhPhonContext member)
        {
            switch (member)
            {
                case IPhSimpleContext simple: return Simple(simple);
                case IPhIterationContext iteration:
                    if (!(iteration.MemberRA is IPhSimpleContext inner)) throw Bad("A repeat holds something other than a simple context.");
                    if (inner is IPhSimpleContextBdry innerBoundary && innerBoundary.FeatureStructureRA is { } m && RuleInventory.IsWordBoundary(m))
                        throw Bad("A repeat holds the word boundary.");
                    int min = iteration.Minimum, max = iteration.Maximum;
                    if (min < 0 || max == 0 || max < -1 || max != -1 && max < min) throw Bad("A repeat has the counts " + min.ToString(CultureInfo.InvariantCulture) + " to " + max.ToString(CultureInfo.InvariantCulture) + ".");
                    string text = Simple(inner);
                    if (min == 0 && max == 1) return "(" + text + ")";
                    return text + "{" + min.ToString(CultureInfo.InvariantCulture) + "," + (max == -1 ? "∞" : max.ToString(CultureInfo.InvariantCulture)) + "}";
                default: throw Bad("A context holds a " + member.ClassName + ", which the text does not show.");
            }
        }

        string Simple(IPhContextOrVar context)
        {
            switch (context)
            {
                case IPhSimpleContextSeg segment:
                    return Unit(segment.FeatureStructureRA ?? throw Bad("A segment context has no phoneme."));
                case IPhSimpleContextBdry boundary:
                    return Unit(boundary.FeatureStructureRA ?? throw Bad("A boundary context has no marker."));
                case IPhSimpleContextNC nc:
                    var alphas = new List<(string Feature, string Value)>();
                    foreach (var constraint in nc.PlusConstrRS) alphas.Add(Alpha(constraint, false));
                    foreach (var constraint in nc.MinusConstrRS) alphas.Add(Alpha(constraint, true));
                    return ClassText(nc.FeatureStructureRA ?? throw Bad("A class context has no class."), alphas);
                default: throw Bad("A context holds a " + context.ClassName + ", which the text does not show.");
            }
        }

        (string Feature, string Value) Alpha(IPhFeatureConstraint constraint, bool disagree)
        {
            if (_constraints == null) throw Bad("A class in an affix process has an alpha constraint, which the text does not show.");
            int at = _constraints.IndexOf(constraint);
            if (at < 0 || at >= MostLetters) throw Bad("A constraint is not in the rule's list of feature constraints.");
            string label = FeatureStructureNotation.FeatureLabel(constraint.FeatureRA ?? throw Bad("A feature constraint has no feature."), _cache, PhonologicalSystem, "phonological");
            return (label, (disagree ? "-" : "") + RuleNotation.Letters[at]);
        }

        internal string Nodes(RuleText text)
        {
            try { return string.Join(" ", text.Nodes.Select(Node)); }
            catch (FeatureStructureNotationException e)
            {
                throw new RuleNotationException(e.Code == FeatureStructureNotation.AmbiguousLabel ? RuleNotation.AmbiguousLabel : RuleNotation.Unrenderable, e.Message);
            }
        }

        string Node(RuleNode node)
        {
            switch (node)
            {
                case RuleUnit unit: return Unit(unit.Unit);
                case RuleGap _: return "*";
                case RuleGroup group: return "<" + string.Join(" ", group.Members.Select(Node)) + ">";
                case RuleRepeat repeat:
                    string inner = Node(repeat.Member);
                    return repeat.Minimum == 0 && repeat.Maximum == 1 ? "(" + inner + ")"
                        : inner + "{" + repeat.Minimum.ToString(CultureInfo.InvariantCulture) + "," + (repeat.Maximum == -1 ? "∞" : repeat.Maximum.ToString(CultureInfo.InvariantCulture)) + "}";
                case RuleCopy copy: return "$" + copy.Index.ToString(CultureInfo.InvariantCulture);
                case RuleModify modify: return "$" + modify.Index.ToString(CultureInfo.InvariantCulture) + "~" + Node(modify.Modification);
                case RuleInsertUnits units:
                    var written = units.Units.Select(u => Unit(u.Unit)).ToList();
                    return written.Count == 1 ? written[0] : "<" + string.Join(" ", written) + ">";
                case RuleClass c:
                    var alphas = c.Alphas.Select(a => (FeatureStructureNotation.FeatureLabel(a.Feature, _cache, PhonologicalSystem, "phonological"),
                        (a.Disagree ? "-" : "") + a.Letter)).ToList();
                    var specs = c.Features.Select(f => (FeatureStructureNotation.FeatureLabel(f.Feature, _cache, PhonologicalSystem, "phonological"),
                        FeatureStructureNotation.Label(f.Value.Abbreviation, f.Value.Name, _cache) ?? throw Bad("A value has no usable label."))).ToList();
                    return c.NamedClass != null ? ClassText(c.NamedClass, alphas) : Bracket(null, alphas, specs);
                default: throw Bad("An unknown node.");
            }
        }

        string ClassText(IPhNaturalClass naturalClass, List<(string Feature, string Value)> alphas)
        {
            string? name = AbbreviationOf(naturalClass);
            var specs = new List<(string Feature, string Value)>();
            if (name == null)
            {
                if (naturalClass is IPhNCFeatures features)
                {
                    if (features.FeaturesOA != null)
                        specs.AddRange(FeatureStructureNotation.Collect(features.FeaturesOA, _cache, PhonologicalSystem, "phonological"));
                }
                else throw Bad("A segment class has no abbreviation.");
            }
            else
            {
                int same = _cache.LangProject.PhonologicalDataOA.NaturalClassesOS.Count(n => AbbreviationOf(n) == name);
                if (same > 1) throw new RuleNotationException(RuleNotation.AmbiguousLabel, same.ToString(CultureInfo.InvariantCulture) + " classes share the abbreviation " + name + ".");
            }
            return Bracket(name, alphas, specs);
        }

        static string Bracket(string? name, List<(string Feature, string Value)> alphas, List<(string Feature, string Value)> specs)
        {
            var pairs = new List<(string Feature, string Value, bool IsAlpha)>(alphas.Select(a => (a.Feature, a.Value, true)));
            pairs.AddRange(specs.Select(p => (p.Feature, p.Value, false)));
            if (pairs.Select(p => p.Feature).Distinct(StringComparer.Ordinal).Count() != pairs.Count)
                throw Bad("A class gives one feature more than once.");
            pairs.Sort((a, b) => string.CompareOrdinal(a.Feature, b.Feature));
            var text = new StringBuilder("[");
            if (name != null) text.Append(FeatureStructureNotation.Escape(name));
            foreach (var pair in pairs)
            {
                if (text.Length > 1) text.Append(' ');
                text.Append(FeatureStructureNotation.Escape(pair.Feature)).Append(':');
                text.Append(pair.IsAlpha ? pair.Value : ValueText(pair.Value));
            }
            return text.Append(']').ToString();
        }

        static string ValueText(string value)
        {
            string escaped = FeatureStructureNotation.Escape(value);
            string bare = value.StartsWith("-", StringComparison.Ordinal) ? value.Substring(1) : value;
            return bare.Length == 1 && RuleNotation.Letters.IndexOf(bare[0]) >= 0 ? "\\" + escaped : escaped;
        }

        string? AbbreviationOf(IPhNaturalClass naturalClass)
        {
            string text = FeatureStructureNotation.Best(naturalClass.Abbreviation, _cache).Normalize(NormalizationForm.FormC);
            if (text.Length == 0) return null;
            if (text.Any(char.IsControl)) throw Bad("A class abbreviation has a control character.");
            return text;
        }

        string Unit(IPhTerminalUnit unit)
        {
            if (!_inventory.Contains(unit)) throw Bad("A phoneme or marker is not in the project's first phoneme set.");
            if (RuleInventory.IsStandard(unit)) return RuleInventory.IsWordBoundary(unit) ? "#" : "+";
            string code = _inventory.Written(unit) ?? throw Bad("A phoneme or marker has no vernacular code.");
            if (code.Any(char.IsControl)) throw Bad("A phoneme or marker code has a control character.");
            int same = _inventory.Lookup(code).Count;
            if (same > 1) throw new RuleNotationException(RuleNotation.AmbiguousLabel, same.ToString(CultureInfo.InvariantCulture) + " phonemes or markers share the code " + code + ".");
            return EscapeLiteral(code);
        }

        internal static string EscapeLiteral(string code)
        {
            var text = new StringBuilder();
            foreach (char c in code)
            {
                if (LiteralReserved.IndexOf(c) >= 0 || char.IsWhiteSpace(c)) text.Append('\\');
                text.Append(c);
            }
            return text.ToString();
        }
    }
}
