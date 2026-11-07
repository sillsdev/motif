using System;
using System.Text.RegularExpressions;

namespace SIL.TextEditing
{
    /// <summary>What a host's find dialog asks one field to search for (ADR 0021, ADR 0025). The pattern is compared as
    /// Unicode NFD text, so a precomposed letter and its decomposed form match each other whatever form the document stores.
    /// A match lies inside one paragraph.</summary>
    public sealed class FindOptions
    {
        /// <summary>The text to find. Empty with <see cref="WritingSystem"/> or <see cref="CharacterStyle"/> set finds each stretch
        /// of text with those properties; empty otherwise finds nothing.</summary>
        public string Pattern { get; set; } = string.Empty;

        /// <summary>Upper and lower case differ. Default false. Matching case also matches diacritics exactly, as FieldWorks'
        /// collation does, whatever <see cref="MatchDiacritics"/> says.</summary>
        public bool MatchCase { get; set; }

        /// <summary>Combining marks must match: "e" does not find the first letter of "e" plus an acute accent, and "e" plus
        /// an acute accent does not find "e". Off, marks are ignored in the text and in the pattern. Default true, as in
        /// FieldWorks. Not used with <see cref="UseRegularExpression"/>.</summary>
        public bool MatchDiacritics { get; set; } = true;

        /// <summary>A match must start and end at word boundaries (see <see cref="WordBreaks"/>). Not used with
        /// <see cref="UseRegularExpression"/>.</summary>
        public bool WholeWord { get; set; }

        /// <summary>The pattern is a regular expression (.NET syntax, over the NFD text; case follows
        /// <see cref="MatchCase"/>). A replacement may use group references such as <c>$1</c>.</summary>
        public bool UseRegularExpression { get; set; }

        /// <summary>Only text in this writing system matches (every run the match touches must have it). Null for any.</summary>
        public string? WritingSystem { get; set; }

        /// <summary>Only text with this character style matches; the empty string means text with no character style. Null
        /// for any.</summary>
        public string? CharacterStyle { get; set; }

        /// <summary>How long one regular-expression search of one paragraph may run before it is given up: a pattern that backtracks
        /// without end would freeze the window. A search that times out finds nothing and says so (<see cref="DocumentEditor.FindTimedOut"/>).
        /// Default a quarter of a second.</summary>
        public TimeSpan RegexTimeout { get; set; } = TimeSpan.FromMilliseconds(250);

        public FindOptions Clone() => (FindOptions)MemberwiseClone();

        /// <summary>False when the options cannot search, with the reason: a regular expression that does not compile.</summary>
        public bool IsValid(out string? error)
        {
            error = null;
            if (!UseRegularExpression || Pattern.Length == 0) return true;
            try { _ = new Regex(TextNormalizer.Normalize(Pattern, System.Text.NormalizationForm.FormD)); return true; }
            catch (ArgumentException ex) { error = ex.Message; return false; }
        }

        internal bool PropertiesOnly => !UseRegularExpression && Pattern.Length == 0 && (WritingSystem != null || CharacterStyle != null);
    }
}
