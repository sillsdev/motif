using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;

namespace SIL.TextEditing.LCModel
{
    /// <summary>The XML of one ITsString, through <see cref="TsStringSerializer"/>: the payload of <see cref="TsStringClipboardForm"/>, under the field's own
    /// format name <see cref="XmlFormatName"/>. This is NOT FieldWorks' "TsString" clipboard format: FieldWorks stores a .NET-serialised TsStringWrapper (a class in its
    /// own SimpleRootSite assembly, which this package neither references nor copies) under that name. A FieldWorks host registers its own form under the id and format
    /// name "TsString" that wraps and unwraps this XML (<c>FragmentToXml</c> and <c>FromXmlToFragment</c>) with its wrapper (ADR 0029).
    /// A selection over several paragraphs is joined with <see cref="FieldWorksParagraphSeparator"/> first, in the XML and in
    /// the plain text, as FieldWorks' EditingHelper.CopySelection does (its GetSelectionString fallback), so a FieldWorks
    /// paste of the payload agrees with its own copy. The paragraph keys stay out of the clipboard.
    /// <see cref="TsStringClipboardForm"/> registers it on a field.</summary>
    public static class TsStringClipboard
    {
        /// <summary>The clipboard format name of the field's own TsString-XML form. Never "TsString", which belongs to FieldWorks' wrapper.</summary>
        public const string XmlFormatName = "SIL.TextEditing.TsStringXml";

        /// <summary>What FieldWorks puts between paragraphs of a copied selection.</summary>
        public const string FieldWorksParagraphSeparator = "; ";

        public static string ToXml(ITsString text, IWritingSystemHandles writingSystems)
            => TsStringSerializer.SerializeTsStringToXml(text, new HandlesFactory(writingSystems), 0, true, false);

        public static ITsString FromXml(string xml, IWritingSystemHandles writingSystems)
            => TsStringSerializer.DeserializeTsStringFromXml(xml, new HandlesFactory(writingSystems));

        /// <summary>The XML for a fragment, as <c>DocumentEditor.GetFragment</c> returns it, paragraphs joined with the separator.</summary>
        public static string FragmentToXml(IReadOnlyList<Paragraph> fragment, IWritingSystemHandles writingSystems,
            string paragraphSeparator = FieldWorksParagraphSeparator, Action<DroppedProperty>? onDropped = null)
        {
            var joined = new List<Run>();
            for (int i = 0; i < fragment.Count; i++)
            {
                if (i > 0) joined.Add(new Run { Text = paragraphSeparator, Ws = fragment[i].DefaultWs });
                joined.AddRange(fragment[i].Runs);
            }
            string defaultWs = fragment.Count > 0 ? fragment[0].DefaultWs : "en";
            return ToXml(TsStringConversion.ToTsString(joined, writingSystems, defaultWs, onDropped), writingSystems);
        }

        /// <summary>The paragraphs a pasted payload holds, ready for <c>DocumentEditor.Paste</c>; none carries a key. With the
        /// default (null) the payload is one paragraph, as FieldWorks pastes it; give a separator to split at it.</summary>
        public static List<Paragraph> FromXmlToFragment(string xml, IWritingSystemHandles writingSystems, string? paragraphSeparator = null)
        {
            var text = FromXml(xml, writingSystems);
            var all = TsStringConversion.ToRuns(text, writingSystems);
            var result = new List<Paragraph>();
            var current = new Paragraph();
            foreach (var run in all)
            {
                if (string.IsNullOrEmpty(paragraphSeparator) || run.IsObject || run.Text.IndexOf(paragraphSeparator, StringComparison.Ordinal) < 0)
                {
                    current.Runs.Add(run);
                    continue;
                }
                var parts = run.Text.Split(new[] { paragraphSeparator! }, StringSplitOptions.None);
                for (int i = 0; i < parts.Length; i++)
                {
                    if (i > 0)
                    {
                        result.Add(current);
                        current = new Paragraph();
                    }
                    if (parts[i].Length > 0) current.Runs.Add(run.CloneWithText(parts[i]));
                }
            }
            result.Add(current);
            foreach (var p in result.Where(p => p.Runs.Count > 0)) p.DefaultWs = p.Runs[0].Ws;
            return result;
        }
    }
}
