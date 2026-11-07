using SIL.TextEditing;

namespace SIL.TextEditing.Tests
{
    /// <summary>Small builders shared by the model and surface tests.</summary>
    public static class TestDocs
    {
        /// <summary>A paragraph from alternating (ws, text) arguments; defaultWs is used for typing when it is empty.</summary>
        public static Paragraph Para(string defaultWs, params string[] wsAndText)
        {
            var p = new Paragraph { DefaultWs = defaultWs };
            for (int i = 0; i < wsAndText.Length; i += 2)
                p.Runs.Add(new Run { Ws = wsAndText[i], Text = wsAndText[i + 1] });
            return p;
        }

        public static Document Doc(params Paragraph[] paragraphs)
        {
            var d = new Document();
            d.Paragraphs.AddRange(paragraphs);
            // The host assigns keys; give each test paragraph one so the split and join rules show.
            for (int i = 0; i < paragraphs.Length; i++) paragraphs[i].Key = "key" + i;
            return d;
        }

        public static Document OneParagraph(string text) => Doc(Para("en", "en", text));

        /// <summary>"Hello bonjour world" with English, French, English runs, and an Arabic paragraph.</summary>
        public static Document Basic() => Doc(
            Para("en", "en", "Hello ", "fr", "bonjour", "en", " world"),
            Para("ar", "en", "Mixed: ", "ar", "مرحبا بالعالم", "en", " end"));
    }
}
