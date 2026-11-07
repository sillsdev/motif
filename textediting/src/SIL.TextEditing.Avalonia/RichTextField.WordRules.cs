using System;

namespace SIL.TextEditing.Avalonia
{
    // One word rule for the double-click, Ctrl+Left and Right, the whole-word find, the word of a context request and the spell checker (ADR 0026, issue 102).
    public partial class RichTextField
    {
        IWordRules? _wordRules;
        bool _wordRulesFailed;

        /// <summary>The host's word rule: which characters form words in each writing system, and which join the words on each side of them (apostrophes,
        /// zero-width joiners). FieldWorks asks the writing system, so a writing system that makes a hyphen a letter makes "foo-bar" one word for a double-click and
        /// for Ctrl+Left and Right as well as for spell checking. Null, the default, takes the rule of <see cref="SpellChecking"/> when the host set one (an
        /// <see cref="ISpellCheckHost"/> is an <see cref="IWordRules"/>), else <see cref="DefaultWordRules"/>. A rule set here wins over the spell checker's
        /// for everything, the squiggled words included, so there is one rule; setting it starts spell checking over. A rule that throws is traced once and the
        /// default answers in its place.</summary>
        public IWordRules? WordRules
        {
            get => _wordRules;
            set
            {
                _wordRules = value;
                _wordRulesFailed = false;
                UpdateWordRules();
                if (_spellHost != null) RestartSpelling();
            }
        }

        /// <summary>Gives the editor the rule in force: the host's <see cref="WordRules"/>, else its spell checker's, else none (the default).</summary>
        void UpdateWordRules()
        {
            Editor.WordRules = _wordRules != null ? new GuardedRules(this, _wordRules) : _spellGuard;
        }

        /// <summary>The word rule as the field's own questions see it: a host that throws is not allowed to stop a double-click.</summary>
        sealed class GuardedRules : IWordRules
        {
            readonly RichTextField _field;
            readonly IWordRules _inner;

            public GuardedRules(RichTextField field, IWordRules inner)
            {
                _field = field;
                _inner = inner;
            }

            public bool IsWordForming(string ws, int codePoint)
            {
                try { return _inner.IsWordForming(ws, codePoint); }
                catch (Exception e)
                {
                    _field.WordRulesFailed(e);
                    return SpellScanner.DefaultIsWordForming(codePoint);
                }
            }

            public bool JoinsWords(string ws, int codePoint)
            {
                try { return _inner.JoinsWords(ws, codePoint); }
                catch (Exception e)
                {
                    _field.WordRulesFailed(e);
                    return SpellScanner.DefaultJoinsWords(codePoint);
                }
            }
        }

        void WordRulesFailed(Exception error)
        {
            if (_wordRulesFailed) return;
            _wordRulesFailed = true;
            Trace("The host's word rule threw and the default answers in its place: " + error);
        }
    }
}
