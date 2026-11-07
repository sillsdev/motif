namespace SIL.TextEditing.Playground
{
    /// <summary>A host's word rule for a writing system that makes the hyphen a letter (FieldWorks' valid characters can): "well-known" is one word for a
    /// double-click, Ctrl+Left and Right, find and the spell checker.</summary>
    public sealed class SampleWordRules : IWordRules
    {
        public static readonly SampleWordRules HyphenIsLetter = new SampleWordRules();

        SampleWordRules() { }

        public bool IsWordForming(string ws, int codePoint) => codePoint == '-' || SpellScanner.DefaultIsWordForming(codePoint);

        public bool JoinsWords(string ws, int codePoint) => SpellScanner.DefaultJoinsWords(codePoint);
    }
}
