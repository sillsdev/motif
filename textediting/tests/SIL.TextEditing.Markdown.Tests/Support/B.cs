using System;

namespace SIL.TextEditing.Markdown.Tests
{
    /// <summary>Builders for expected models.</summary>
    internal static class B
    {
        public static Run R(string ws, string text, Action<PropertyBag>? props = null, string? objData = null)
        {
            var run = new Run { Ws = ws, Text = text, ObjData = objData };
            props?.Invoke(run.Props);
            return run;
        }

        public static Run En(string text, Action<PropertyBag>? props = null) => R("en", text, props);

        public static Run BoldRun(string ws, string text) => R(ws, text, p => p.SetInt(TextPropertyNumbers.Bold, TextPropertyNumbers.ToggleOn, TextPropertyNumbers.VariationEnum));

        public static Run ItalicRun(string ws, string text) => R(ws, text, p => p.SetInt(TextPropertyNumbers.Italic, TextPropertyNumbers.ToggleOn, TextPropertyNumbers.VariationEnum));

        public static Run Styled(string ws, string text, string charStyle) => R(ws, text, p => p.SetStr(TextPropertyNumbers.NamedStyle, charStyle));

        /// <summary>A paragraph the way the LCModel adapter builds one: the style name is also property 133.</summary>
        public static Paragraph P(string style, params Run[] runs)
        {
            var paragraph = new Paragraph { Style = style };
            if (style.Length > 0) paragraph.Props.StrProps[TextPropertyNumbers.NamedStyle] = style;
            paragraph.Runs.AddRange(runs);
            paragraph.DefaultWs = runs.Length > 0 ? runs[0].Ws : "en";
            return paragraph;
        }

        public static Paragraph Plain(string text) => P("Normal", En(text));

        public static Paragraph Empty(string style = "Normal", string ws = "en")
        {
            var paragraph = new Paragraph { Style = style, DefaultWs = ws };
            if (style.Length > 0) paragraph.Props.StrProps[TextPropertyNumbers.NamedStyle] = style;
            return paragraph;
        }

        public static Paragraph WithProps(this Paragraph paragraph, Action<PropertyBag> props)
        {
            props(paragraph.Props);
            return paragraph;
        }

        public static Document Doc(params Paragraph[] paragraphs)
        {
            var document = new Document();
            document.Paragraphs.AddRange(paragraphs);
            return document;
        }
    }
}
