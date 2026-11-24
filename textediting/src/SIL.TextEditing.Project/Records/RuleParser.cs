using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SIL.LCModel;

namespace SIL.TextEditing.Project
{
    /// <summary>Reads a rule-formula text into resolved nodes.</summary>
    internal sealed class RuleParser
    {
        const string Reserved = "[](){}<>#+*_/|$~";
        readonly string _text;
        readonly RuleField _field;
        readonly LcmCache _cache;
        readonly int? _inputColumns;
        readonly RuleInventory _inventory;
        int _at;

        internal RuleParser(string text, RuleField field, LcmCache cache, int? inputColumns)
        {
            _text = text;
            _field = field;
            _cache = cache;
            _inputColumns = inputColumns;
            _inventory = new RuleInventory(cache);
        }

        bool Affix => _field == RuleField.AffixInput || _field == RuleField.AffixOutput;

        RuleNotationException Fail(string code, string why) =>
            new RuleNotationException(code, "The rule text is not valid as " + _field + ": " + why + " (at " + _at.ToString(CultureInfo.InvariantCulture) + ").");

        RuleNotationException Syntax(string why) => Fail(RuleNotation.Syntax, why);

        RuleNotationException NotAllowed(string why) => Fail(RuleNotation.ContextNotAllowed, why);

        char Peek => _at < _text.Length ? _text[_at] : '\0';

        bool AtEnd => _at >= _text.Length;

        void Skip() { while (_at < _text.Length && char.IsWhiteSpace(_text[_at])) _at++; }

        static bool IsLiteralStart(char c) => !char.IsWhiteSpace(c) && Reserved.IndexOf(c) < 0;

        internal static IEnumerable<RuleClass> Classes(IEnumerable<RuleNode> nodes)
        {
            foreach (var node in nodes)
                switch (node)
                {
                    case RuleClass c: yield return c; break;
                    case RuleModify m: yield return m.Modification; break;
                    case RuleRepeat r: foreach (var c in Classes(new[] { r.Member })) yield return c; break;
                    case RuleGroup g: foreach (var c in Classes(g.Members)) yield return c; break;
                }
        }

        internal RuleText Parse()
        {
            var nodes = new List<RuleNode>();
            Skip();
            while (!AtEnd)
            {
                nodes.Add(Item());
                Skip();
            }
            if (_field == RuleField.LeftContext || _field == RuleField.RightContext) CheckEdges(nodes);
            return new RuleText(_field, nodes);
        }

        void CheckEdges(List<RuleNode> nodes)
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                bool word = nodes[i] is RuleUnit unit && RuleInventory.IsWordBoundary(unit.Unit);
                bool inside = nodes[i] is RuleRepeat repeat && repeat.Member is RuleUnit inner && RuleInventory.IsWordBoundary(inner.Unit);
                if (inside) throw NotAllowed("the word boundary # cannot be repeated");
                if (word && !(_field == RuleField.LeftContext ? i == 0 : i == nodes.Count - 1))
                    throw NotAllowed("the word boundary # is only at the outer edge of a context");
            }
        }

        RuleNode Item()
        {
            switch (_field)
            {
                case RuleField.AffixInput: return Column();
                case RuleField.AffixOutput: return Step();
                case RuleField.StrucDesc:
                case RuleField.StrucChange:
                    return SimpleOnly("a flat list of simple contexts");
                default: return Member();
            }
        }

        RuleNode SimpleOnly(string what)
        {
            char c = Peek;
            if (c == '*' || c == '<' || c == '(' || c == '{') throw NotAllowed("this field holds only " + what);
            return Simple();
        }

        // ------------------------------------------------------------------ affix

        RuleNode Column()
        {
            char c = Peek;
            if (c == '*') { _at++; return new RuleGap(); }
            if (c == '(' || c == '{') throw NotAllowed("a repeat does not belong in an affix input");
            RuleNode node = c == '<' ? Group() : Simple();
            Skip();
            if (Peek == '{') throw NotAllowed("a repeat does not belong in an affix input");
            return node;
        }

        RuleNode Group()
        {
            _at++;
            var members = new List<RuleNode>();
            Skip();
            while (Peek != '>')
            {
                if (AtEnd) throw Syntax("a < is not closed by >");
                char c = Peek;
                if (c == '*' || c == '<' || c == '(' || c == '{') throw NotAllowed("a column holds simple contexts only");
                members.Add(Simple());
                Skip();
            }
            _at++;
            if (members.Count == 0) throw Syntax("a column <> holds nothing");
            return members.Count == 1 ? members[0] : new RuleGroup(members);
        }

        RuleNode Step()
        {
            char c = Peek;
            if (c == '$') return Copy();
            if (c == '~') throw Syntax("~ must follow $n");
            if (c == '*' || c == '(' || c == '{') throw NotAllowed("an output holds copies, classes and inserted units");
            if (c == '<')
            {
                _at++;
                var units = new List<RuleUnit>();
                Skip();
                while (Peek != '>')
                {
                    if (AtEnd) throw Syntax("a < is not closed by >");
                    if (Peek == '[' || Peek == '<' || Peek == '$' || Peek == '*') throw NotAllowed("inserted phones are phonemes and markers only");
                    units.Add(Unit());
                    Skip();
                }
                _at++;
                if (units.Count == 0) throw Syntax("an insertion <> holds nothing");
                return new RuleInsertUnits(units);
            }
            if (c == '[') return Class();
            return new RuleInsertUnits(new[] { Unit() });
        }

        RuleNode Copy()
        {
            _at++;
            int start = _at;
            while (_at < _text.Length && _text[_at] >= '0' && _text[_at] <= '9') _at++;
            if (_at == start) throw Syntax("$ is not followed by a number");
            bool numeric = int.TryParse(_text.Substring(start, _at - start), NumberStyles.None, CultureInfo.InvariantCulture, out int index);
            if (!numeric || index < 1 || _inputColumns is { } columns && index > columns)
                throw Fail(RuleNotation.IndexOutOfRange, "$" + _text.Substring(start, _at - start) + " is not a column of the input");
            if (Peek == '[' || !AtEnd && IsLiteralStart(Peek)) throw Syntax("$n must be followed by white space or ~ before a class or phoneme");
            Skip();
            if (Peek != '~') return new RuleCopy(index);
            _at++;
            Skip();
            if (Peek != '[') throw Syntax("~ must be followed by a class");
            var modification = Class();
            if (modification.NamedClass != null && !(modification.NamedClass is IPhNCFeatures))
                throw NotAllowed("a modification is a feature class");
            return new RuleModify(index, modification);
        }

        // ------------------------------------------------------------------ contexts

        RuleNode Member()
        {
            char c = Peek;
            if (c == '*' || c == '<') throw NotAllowed("a context holds simple contexts, repeated or not");
            if (c == '{') throw Syntax("{ must follow a simple context");
            if (c == '(')
            {
                _at++;
                Skip();
                if (Peek == '(' || Peek == '<' || Peek == '*' || Peek == '{') throw NotAllowed("an optional member is one simple context");
                var inner = Simple();
                Skip();
                if (Peek != ')') throw Syntax("a ( is not closed by ) after one simple context");
                _at++;
                Skip();
                if (Peek == '{') throw NotAllowed("{} follows a simple context, not an optional member");
                return new RuleRepeat(inner, 0, 1);
            }
            var simple = Simple();
            Skip();
            if (Peek != '{') return simple;
            var repeat = Count(simple);
            Skip();
            if (Peek == '{') throw NotAllowed("{} follows a simple context, not a repeat");
            return repeat;
        }

        RuleNode Count(RuleNode simple)
        {
            _at++;
            Skip();
            int min = Number();
            Skip();
            if (Peek != ',') throw Syntax("a repeat is written {min,max}");
            _at++;
            Skip();
            int max;
            if (Peek == '∞' || Peek == '*') { _at++; max = -1; }
            else max = Number();
            Skip();
            if (Peek != '}') throw Syntax("a { is not closed by }");
            _at++;
            if (max != -1 && (max < 1 || max < min)) throw Fail(RuleNotation.BadRepeat, "the repeat {" + min.ToString(CultureInfo.InvariantCulture) + "," + max.ToString(CultureInfo.InvariantCulture) + "} has a maximum below 1 or below its minimum");
            return new RuleRepeat(simple, min, max);
        }

        int Number()
        {
            int start = _at;
            while (_at < _text.Length && _text[_at] >= '0' && _text[_at] <= '9') _at++;
            if (_at == start) throw Syntax("a repeat count is a number");
            if (!int.TryParse(_text.Substring(start, _at - start), NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                throw Fail(RuleNotation.BadRepeat, "a repeat count is too large");
            return value;
        }

        // ------------------------------------------------------------------ simple contexts

        RuleNode Simple()
        {
            char c = Peek;
            if (c == '[') return Class();
            if (AtEnd) throw Syntax("a context is missing");
            return Unit();
        }

        RuleUnit Unit()
        {
            char c = Peek;
            if (c == '#' || c == '+')
            {
                _at++;
                Guid guid = c == '#' ? LangProjectTags.kguidPhRuleWordBdry : LangProjectTags.kguidPhRuleMorphBdry;
                return new RuleUnit(_inventory.Standard(guid) ?? throw Fail(RuleNotation.UnknownPhoneme, "the first phoneme set has no marker " + c));
            }
            if (!IsLiteralStart(c))
            {
                if (c == '_' || c == '/' || c == '|') throw Syntax("the character " + c + " needs a backslash in front of it");
                throw Syntax("unexpected " + (AtEnd ? "end of text" : c.ToString()));
            }
            var code = new StringBuilder();
            while (_at < _text.Length)
            {
                char d = _text[_at];
                if (d == '\\')
                {
                    if (_at + 1 >= _text.Length) throw Syntax("a backslash ends the text");
                    code.Append(_text[_at + 1]);
                    _at += 2;
                }
                else if (IsLiteralStart(d)) { code.Append(d); _at++; }
                else break;
            }
            string text = code.ToString();
            var matches = _inventory.Lookup(text);
            if (matches.Count == 0) throw Fail(RuleNotation.UnknownPhoneme, "no phoneme or marker has the code " + text);
            if (matches.Count > 1) throw Fail(RuleNotation.AmbiguousLabel, matches.Count.ToString(CultureInfo.InvariantCulture) + " phonemes or markers have the code " + text);
            return new RuleUnit(matches[0]);
        }

        RuleClass Class()
        {
            var bracket = FeatureStructureNotation.ReadBracket(_text, _at, true);
            _at = bracket.End;
            var system = _cache.LangProject.PhFeatureSystemOA;
            var specs = new List<FeatureStructureSelection>();
            var alphas = new List<RuleAlpha>();
            var seen = new HashSet<Guid>();
            foreach (var pair in bracket.Pairs)
            {
                var feature = FeatureStructureNotation.ResolveFeature(system ?? throw new FeatureStructureNotationException(FeatureStructureNotation.UnknownFeature,
                    "The project has no phonological feature system."), pair.Feature, _cache);
                if (!seen.Add(feature.Guid))
                    throw new FeatureStructureNotationException(FeatureStructureNotation.RepeatedFeature, "Feature " + pair.Feature + " is named more than once.");
                if (!pair.ValueEscaped && AlphaOf(pair.Value, out char letter, out bool disagree))
                {
                    if (Affix) throw NotAllowed("an affix process has no alpha variables");
                    alphas.Add(new RuleAlpha(feature, letter, disagree));
                }
                else specs.Add(new FeatureStructureSelection(feature, FeatureStructureNotation.ResolveValue(feature, pair.Feature, pair.Value, _cache)));
            }
            IPhNaturalClass? named = null;
            if (bracket.Name != null)
            {
                if (specs.Count > 0) throw Syntax("a named class takes alpha variables only, not feature values");
                var matches = _cache.LangProject.PhonologicalDataOA.NaturalClassesOS
                    .Where(n => FeatureStructureNotation.Best(n.Abbreviation, _cache).Normalize(NormalizationForm.FormC) == bracket.Name).ToList();
                if (matches.Count == 0) throw Fail(RuleNotation.UnknownNaturalClass, "no class has the abbreviation " + bracket.Name);
                if (matches.Count > 1) throw Fail(RuleNotation.AmbiguousLabel, matches.Count.ToString(CultureInfo.InvariantCulture) + " classes have the abbreviation " + bracket.Name);
                named = matches[0];
            }
            return new RuleClass(named, specs, alphas);
        }

        static bool AlphaOf(string value, out char letter, out bool disagree)
        {
            disagree = value.StartsWith("-", StringComparison.Ordinal);
            string bare = disagree ? value.Substring(1) : value;
            letter = bare.Length == 1 ? bare[0] : '\0';
            return letter != '\0' && RuleNotation.Letters.IndexOf(letter) >= 0;
        }
    }
}
