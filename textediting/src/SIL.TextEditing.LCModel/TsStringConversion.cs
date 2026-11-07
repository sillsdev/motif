using System;
using System.Collections.Generic;
using System.Text;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;

namespace SIL.TextEditing.LCModel
{
    /// <summary>One paragraph as an IStText holds it: the StTxtPara key (its GUID in FieldWorks), the Contents string and
    /// the StyleRules properties.</summary>
    public sealed class TextParagraph
    {
        public TextParagraph(object? key, ITsString contents, ITsTextProps? styleRules)
        {
            Key = key;
            Contents = contents;
            StyleRules = styleRules;
        }

        /// <summary>The host's key. The adapter only carries it; it never creates one.</summary>
        public object? Key { get; }
        public ITsString Contents { get; }
        public ITsTextProps? StyleRules { get; }
    }

    /// <summary>Converts between ITsString (and an IStText-shaped list of paragraphs) and the run model.
    /// Every integer and string property of every ITsTextProps goes into a <see cref="PropertyBag"/> by property
    /// number, with its variation, and comes back unchanged. Only two properties leave the bag: the writing system
    /// (ktptWs, int 1) becomes <see cref="Run.Ws"/> and the object data (ktptObjData, string 6) becomes
    /// <see cref="Run.ObjData"/>.</summary>
    public static class TsStringConversion
    {
        const int PropWs = (int)FwTextPropType.ktptWs;
        const int PropObjData = (int)FwTextPropType.ktptObjData;
        const int PropNamedStyle = (int)FwTextPropType.ktptNamedStyle;
        const int PropRightToLeft = (int)FwTextPropType.ktptRightToLeft;

        /// <summary>The properties of one run as a bag, without the writing system and object data.</summary>
        public static PropertyBag ToBag(ITsTextProps? props)
        {
            var bag = new PropertyBag();
            if (props == null) return bag;
            for (int i = 0; i < props.IntPropCount; i++)
            {
                int value = props.GetIntProp(i, out int tpt, out int variation);
                if (tpt != PropWs) bag.IntProps[tpt] = new IntPropValue(variation, value);
            }
            for (int i = 0; i < props.StrPropCount; i++)
            {
                string value = props.GetStrProp(i, out int tpt);
                if (tpt != PropObjData) bag.StrProps[tpt] = value ?? string.Empty;
            }
            return bag;
        }

        /// <summary>Whether liblcm refuses the integer property by number, which would make the project load as a corrupted record;
        /// <paramref name="inStyleRules"/> says it is a paragraph's StyleRules, where liblcm also refuses ktptEditable (155). This is the one
        /// number check, for hosts that ask before they build; the conversions below also refuse values (ADR 0016).</summary>
        public static bool IsRefusedByLiblcm(int tpt, bool inStyleRules) => LiblcmRefusals.IsPropertyRefused(tpt, inStyleRules);

        // A builder of what the bag holds that liblcm can load back. Refused properties and values are reported and left out; the bag is not touched.
        static ITsPropsBldr FillBuilder(PropertyBag bag, bool inStyleRules, int? paragraphIndex, int? runIndex, Action<DroppedProperty>? onDropped)
        {
            var builder = TsStringUtils.MakePropsBldr();
            foreach (var kv in bag.IntProps)
            {
                var refusal = LiblcmRefusals.ForInt(kv.Key, kv.Value.Variation, kv.Value.Value, inStyleRules);
                if (refusal != null)
                {
                    onDropped?.Invoke(new DroppedProperty(kv.Key, false, paragraphIndex, runIndex, refusal.Reason, refusal.Detail));
                    continue;
                }
                builder.SetIntPropValues(kv.Key, kv.Value.Variation, kv.Value.Value);
            }
            foreach (var kv in bag.StrProps)
            {
                var refusal = LiblcmRefusals.ForString(kv.Key, kv.Value, inStyleRules);
                if (refusal != null)
                {
                    onDropped?.Invoke(new DroppedProperty(kv.Key, true, paragraphIndex, runIndex, refusal.Reason, refusal.Detail));
                    continue;
                }
                builder.SetStrPropValue(kv.Key, kv.Value);
            }
            return builder;
        }

        /// <summary>The runs of a string. An empty string yields one empty run, so its writing system survives.</summary>
        public static List<Run> ToRuns(ITsString text, IWritingSystemHandles writingSystems)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            var runs = new List<Run>();
            for (int i = 0; i < text.RunCount; i++)
            {
                text.GetBoundsOfRun(i, out int min, out int lim);
                var props = text.get_Properties(i);
                var run = new Run
                {
                    Text = text.GetChars(min, lim) ?? string.Empty,
                    Props = ToBag(props),
                };
                run.Ws = writingSystems.GetId(TryGetInt(props, PropWs));
                if (TryGetStr(props, PropObjData, out var objData)) run.ObjData = objData;
                runs.Add(run);
            }
            return runs;
        }

        static int TryGetInt(ITsTextProps props, int tpt)
        {
            for (int i = 0; i < props.IntPropCount; i++)
            {
                int value = props.GetIntProp(i, out int t, out _);
                if (t == tpt) return value;
            }
            return 0;
        }

        static bool TryGetStr(ITsTextProps props, int tpt, out string value)
        {
            for (int i = 0; i < props.StrPropCount; i++)
            {
                string v = props.GetStrProp(i, out int t);
                if (t == tpt)
                {
                    value = v ?? string.Empty;
                    return true;
                }
            }
            value = string.Empty;
            return false;
        }

        /// <summary>The string for a list of runs.</summary>
        public static ITsString ToTsString(IReadOnlyList<Run> runs, IWritingSystemHandles writingSystems, string defaultWs,
            Action<DroppedProperty>? onDropped = null)
            => ToTsString(runs, writingSystems, defaultWs, null, onDropped);

        static ITsString ToTsString(IReadOnlyList<Run> runs, IWritingSystemHandles writingSystems, string defaultWs,
            int? paragraphIndex, Action<DroppedProperty>? onDropped)
        {
            if (runs.Count == 0)
                return TsStringUtils.MakeString(string.Empty, writingSystems.GetHandle(defaultWs));
            if (runs.Count == 1 && runs[0].Text.Length == 0)
                return TsStringUtils.TsStrFactory.MakeStringWithPropsRgch(string.Empty, 0, MakeRunProps(runs[0], writingSystems, paragraphIndex, 0, onDropped));
            var builder = TsStringUtils.MakeStrBldr();
            for (int i = 0; i < runs.Count; i++)
            {
                var run = runs[i];
                if (run.Text.Length == 0) continue;
                builder.Replace(builder.Length, builder.Length, run.Text, MakeRunProps(run, writingSystems, paragraphIndex, i, onDropped));
            }
            return builder.GetString();
        }

        static ITsTextProps MakeRunProps(Run run, IWritingSystemHandles writingSystems, int? paragraphIndex, int runIndex, Action<DroppedProperty>? onDropped)
        {
            var builder = FillBuilder(run.Props, false, paragraphIndex, runIndex, onDropped);
            int ws = writingSystems.GetHandle(run.Ws);
            if (ws != 0) builder.SetIntPropValues(PropWs, 0, ws);
            if (run.ObjData != null)
            {
                var refusal = LiblcmRefusals.ForString(PropObjData, run.ObjData, false);
                if (refusal != null) onDropped?.Invoke(new DroppedProperty(PropObjData, true, paragraphIndex, runIndex, refusal.Reason, refusal.Detail));
                else builder.SetStrPropValue(PropObjData, run.ObjData);
            }
            return builder.GetTextProps();
        }

        /// <summary>A paragraph from its string, its StyleRules and the host's key.</summary>
        public static Paragraph ToParagraph(ITsString contents, ITsTextProps? styleRules, object? key, IWritingSystemHandles writingSystems)
        {
            var paragraph = new Paragraph { Key = key, Props = ToBag(styleRules), Runs = new RunList(ToRuns(contents, writingSystems)) };
            if (paragraph.Props.StrProps.TryGetValue(PropNamedStyle, out var style)) paragraph.Style = style;
            if (paragraph.Props.TryGetInt(PropRightToLeft, out int rtl)) paragraph.Rtl = rtl != 0;
            if (paragraph.Runs.Count > 0) paragraph.DefaultWs = paragraph.Runs[0].Ws;
            return paragraph;
        }

        /// <summary>The string of a paragraph.</summary>
        public static ITsString ToTsString(Paragraph paragraph, IWritingSystemHandles writingSystems, Action<DroppedProperty>? onDropped = null)
            => ToTsString(paragraph.Runs, writingSystems, paragraph.DefaultWs, null, onDropped);

        /// <summary>The StyleRules of a paragraph, or null when it has none. The bag is the base; the two properties the
        /// field edits, <see cref="Paragraph.Style"/> and <see cref="Paragraph.Rtl"/>, are written over it, and an
        /// untouched bag comes back exactly.</summary>
        public static ITsTextProps? ToStyleRules(Paragraph paragraph, Action<DroppedProperty>? onDropped = null)
            => ToStyleRules(paragraph, null, onDropped);

        static ITsTextProps? ToStyleRules(Paragraph paragraph, int? paragraphIndex, Action<DroppedProperty>? onDropped)
        {
            var bag = paragraph.Props.Clone();
            if (paragraph.Style.Length > 0) bag.StrProps[PropNamedStyle] = paragraph.Style;
            else bag.StrProps.Remove(PropNamedStyle);
            bool hadRtl = bag.TryGetInt(PropRightToLeft, out int rtl);
            if (paragraph.Rtl != (hadRtl && rtl != 0))
                bag.IntProps[PropRightToLeft] = new IntPropValue(0, paragraph.Rtl ? 1 : 0);
            // The bag may hold a direction liblcm refuses (a negative one) that agrees with Rtl. The editor shows Rtl, so store that.
            if (bag.IntProps.TryGetValue(PropRightToLeft, out var held)
                && LiblcmRefusals.ForInt(PropRightToLeft, held.Variation, held.Value, true) != null)
            {
                int stored = paragraph.Rtl ? 1 : 0;
                onDropped?.Invoke(new DroppedProperty(PropRightToLeft, false, paragraphIndex, null, DroppedReason.ValueReplaced,
                    "stored " + stored + " to match the paragraph direction"));
                bag.IntProps[PropRightToLeft] = new IntPropValue(0, stored);
            }
            if (bag.Count == 0) return null;
            var rules = FillBuilder(bag, true, paragraphIndex, null, onDropped).GetTextProps();
            return rules.IntPropCount == 0 && rules.StrPropCount == 0 ? null : rules;
        }

        /// <summary>An IStText-shaped list of paragraphs as a document. The document is marked NFD, the form FieldWorks
        /// stores, unless <paramref name="normalization"/> says otherwise.</summary>
        public static Document ToDocument(IEnumerable<TextParagraph> paragraphs, IWritingSystemHandles writingSystems,
            NormalizationForm? normalization = NormalizationForm.FormD)
        {
            var document = new Document { Normalization = normalization };
            foreach (var p in paragraphs)
                document.Paragraphs.Add(ToParagraph(p.Contents, p.StyleRules, p.Key, writingSystems));
            return document;
        }

        /// <summary>A document as paragraphs for the host to store: the key each paragraph carries (null for a new one,
        /// which the host then assigns), its string and its StyleRules. A property or value liblcm cannot load back is left out and
        /// reported to <paramref name="onDropped"/> (ADR 0016).</summary>
        public static List<TextParagraph> ToTextParagraphs(Document document, IWritingSystemHandles writingSystems,
            Action<DroppedProperty>? onDropped = null)
        {
            var result = new List<TextParagraph>();
            for (int i = 0; i < document.Paragraphs.Count; i++)
            {
                var p = document.Paragraphs[i];
                result.Add(new TextParagraph(p.Key, ToTsString(p.Runs, writingSystems, p.DefaultWs, i, onDropped), ToStyleRules(p, i, onDropped)));
            }
            return result;
        }
    }
}
