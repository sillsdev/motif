using System;
using System.Collections.Generic;

namespace SIL.TextEditing
{
    /// <summary>Resolves and toggles bold, italic and underline on selected text, leaving undo ownership with the host.</summary>
    public static class SelectionFormatting
    {
        /// <summary>True when every included run in the selection has the resolved appearance. An empty or mixed selection
        /// is false. Underline passes over strikethrough runs, which FieldWorks stores in the same property. By default object runs are excluded; a host may supply its own run filter.</summary>
        public static bool IsOn(DocumentEditor editor, IStyleResolver? styles, int property, Func<Run, bool>? include = null)
        {
            Validate(property);
            bool any = false;
            foreach (var target in Targets(editor, styles, property, include))
            {
                any = true;
                if (!IsOn(FormatResolver.ResolveChar(target.Key, target.Value, styles, 10), property)) return false;
            }
            return any;
        }

        /// <summary>Creates one unapplied edit, or null when formatting is refused or there are no included runs. Turns
        /// everything off if all selected runs resolve on, otherwise on. Direct properties equal to the inherited
        /// appearance are removed. The host applies the delta and records it in its undo history.</summary>
        public static EditDelta? Toggle(DocumentEditor editor, IStyleResolver? styles, int property, Func<Run, bool>? include = null)
        {
            Validate(property);
            if (!editor.CanFormatSelection) return null;
            using (var targets = Targets(editor, styles, property, include).GetEnumerator())
                if (!targets.MoveNext()) return null;
            bool enable = !IsOn(editor, styles, property, include);
            editor.Ordered(out var start, out var end);
            var delta = new EditDelta { Start = start.Paragraph, Label = "format", CaretBefore = editor.Caret, CaretAfter = end };
            for (int pi = start.Paragraph; pi <= end.Paragraph; pi++)
            {
                var original = editor.Document.Paragraphs[pi];
                var edited = original.Clone();
                int first = edited.SplitRunAt(pi == start.Paragraph ? start.Offset : 0);
                int last = edited.SplitRunAt(pi == end.Paragraph ? end.Offset : edited.Length);
                for (int i = first; i < last; i++)
                {
                    var run = edited.Runs[i];
                    if (!Included(run, include) || IsStrikethrough(FormatResolver.ResolveChar(edited, run, styles, 10), property)) continue;
                    run.Props.IntProps.Remove(property);
                    bool inherited = IsOn(FormatResolver.ResolveChar(edited, run, styles, 10), property);
                    if (inherited != enable)
                        run.Props.SetInt(property, enable ? OnValue(property) : TextPropertyNumbers.ToggleOff, TextPropertyNumbers.VariationEnum);
                }
                edited.Normalize();
                delta.Before.Add(original.Clone());
                delta.After.Add(edited);
            }
            return delta;
        }

        static IEnumerable<KeyValuePair<Paragraph, Run>> Targets(DocumentEditor editor, IStyleResolver? styles, int property, Func<Run, bool>? include)
        {
            if (!editor.HasSelection) yield break;
            editor.Ordered(out var start, out var end);
            for (int pi = start.Paragraph; pi <= end.Paragraph; pi++)
            {
                var paragraph = editor.Document.Paragraphs[pi];
                int from = pi == start.Paragraph ? start.Offset : 0;
                int to = pi == end.Paragraph ? end.Offset : paragraph.Length;
                int offset = 0;
                foreach (var run in paragraph.Runs)
                {
                    int next = offset + run.Text.Length;
                    if (offset < to && next > from && Included(run, include)
                        && !IsStrikethrough(FormatResolver.ResolveChar(paragraph, run, styles, 10), property))
                        yield return new KeyValuePair<Paragraph, Run>(paragraph, run);
                    offset = next;
                }
            }
        }

        static bool Included(Run run, Func<Run, bool>? include) => include != null ? include(run) : !run.IsObject;
        /// <summary>FieldWorks stores strikethrough as one more value of the underline property, so the underline toggle must
        /// pass over those runs: they are neither on nor off, and they keep their strikethrough.</summary>
        static bool IsStrikethrough(CharFormat format, int property) =>
            property == TextPropertyNumbers.Underline && format.Underline == TextPropertyNumbers.UnderlineStrikethrough;
        static int OnValue(int property) => property == TextPropertyNumbers.Underline ? TextPropertyNumbers.UnderlineSingle : TextPropertyNumbers.ToggleOn;
        static bool IsOn(CharFormat format, int property) => property == TextPropertyNumbers.Bold ? format.Bold :
            property == TextPropertyNumbers.Italic ? format.Italic :
            format.Underline >= TextPropertyNumbers.UnderlineDotted && format.Underline <= TextPropertyNumbers.UnderlineSquiggle &&
            format.Underline != TextPropertyNumbers.UnderlineStrikethrough;

        static void Validate(int property)
        {
            if (property != TextPropertyNumbers.Bold && property != TextPropertyNumbers.Italic && property != TextPropertyNumbers.Underline)
                throw new ArgumentOutOfRangeException(nameof(property));
        }
    }
}
