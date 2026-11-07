namespace SIL.TextEditing
{
    /// <summary>What the host says makes a word, for the writing system of each character (ADR 0026, issue 102). One rule serves the double-click, Ctrl+Left and
    /// Ctrl+Right, the whole-word find, the word of a context request and the spell checker, as FieldWorks' views use the writing system's own rule
    /// everywhere. An <see cref="ISpellCheckHost"/> is one; so is <see cref="DefaultWordRules"/>, the answer of a host with no opinion.</summary>
    public interface IWordRules
    {
        /// <summary>True when the code point forms words in the writing system. FieldWorks asks the writing system (its valid
        /// characters can make an apostrophe or a hyphen a letter); a host with no opinion returns
        /// <see cref="SpellScanner.DefaultIsWordForming"/>. Digits, which this is not asked about, always form words. (FieldWorks asks about
        /// a UTF-16 unit; the field asks about a code point, so a supplementary-plane letter arrives whole.)</summary>
        bool IsWordForming(string ws, int codePoint);

        /// <summary>True when the code point, between two word-forming characters, stays in the word: the apostrophes and the zero-width
        /// joiners, so "don't" is one word. A host with no opinion returns <see cref="SpellScanner.DefaultJoinsWords"/>; false for every
        /// code point turns the rule off, and then "don't" is "don" and "t" unless <see cref="IsWordForming"/> says the apostrophe forms words.</summary>
        bool JoinsWords(string ws, int codePoint);
    }

    /// <summary>The word rule of a host with no opinion: <see cref="SpellScanner.DefaultIsWordForming"/> and <see cref="SpellScanner.DefaultJoinsWords"/>
    /// for every writing system.</summary>
    public sealed class DefaultWordRules : IWordRules
    {
        public static readonly DefaultWordRules Instance = new DefaultWordRules();

        DefaultWordRules() { }

        public bool IsWordForming(string ws, int codePoint) => SpellScanner.DefaultIsWordForming(codePoint);
        public bool JoinsWords(string ws, int codePoint) => SpellScanner.DefaultJoinsWords(codePoint);
    }
}
