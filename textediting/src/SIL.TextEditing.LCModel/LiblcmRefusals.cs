using System;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;
using SIL.LCModel.Core.KernelInterfaces;
using SIL.LCModel.Core.Text;

namespace SIL.TextEditing.LCModel
{
    /// <summary>What liblcm's own text-properties XML serializer cannot take back, as facts about its format: which properties its
    /// reader refuses by number, and which values of the others it refuses (its reader throws on read, its writer throws on write, or
    /// a load followed by a save throws). The adapter leaves such a property out of what it hands liblcm (ADR 0016, amended for issues
    /// 136 and 139). Nothing here copies liblcm code; the rules are what a sweep of the real serializers found, and
    /// <c>LiblcmRefusalSweepTests</c> re-runs that sweep and fails when a rule and liblcm disagree.
    /// <para>A value liblcm changes without throwing (a colour out of range, a relative line height written negative, a NUL in text) is
    /// lossy, not refused: it still loads. The writing system (ktptWs 1, ktptBaseWs 16) depends on the host's handles and is not judged here.</para></summary>
    internal static class LiblcmRefusals
    {
        // Integer properties liblcm's text-props writer writes and its reader refuses ("Unknown Prop attribute"): hyphenate, maxLines,
        // cellBorderWidth, cellSpacing, cellPadding, setRowDefaults, relLineHeight, directionDepth, and ktptMarginTop (written "MarginTop",
        // read only as "marginTop").
        static readonly HashSet<int> RefusedInts = new HashSet<int> { 50, 129, 150, 151, 152, 153, 154, 159, 160 };
        const int Editable = 155;

        // Properties whose enum is a small range of values.
        const int Italic = 2, Bold = 3, Superscript = 4, Underline = 5, FontSize = 6, Offset = 7, Align = 17, LineHeight = 24, SpellCheck = 26,
            BulNumStartAt = 144, Tags = 5, ObjData = 6, BulNumFontInfo = 147, WsStyle = 156;

        // Whole numbers liblcm's reader refuses below zero: fontsize, the leading and trailing margins, rightToLeft, the paddings and
        // borders, bulNumScheme, keepWithNext, keepTogether, widowOrphan.
        static readonly HashSet<int> NonNegativeInts = new HashSet<int> { 6, 19, 20, 128, 134, 135, 136, 137, 138, 139, 140, 141, 143, 148, 149, 162 };

        // String properties liblcm's writer writes as an XML attribute holding the text: fontFamily, charStyle, paraStyle, tabList,
        // fontVariations, namedStyle, bulNumTxtBef, bulNumTxtAft. (charStyle, paraStyle and tabList are then ignored on read.)
        static readonly HashSet<int> TextStrings = new HashSet<int> { 1, 2, 3, 4, 130, 133, 145, 146 };

        const int GuidChars = 8;   // a GUID is stored as eight UTF-16 units

        /// <summary>Whether liblcm refuses the integer property by number, which would make the project load as a corrupted record. Hosts ask
        /// through <see cref="TsStringConversion.IsRefusedByLiblcm"/>, the one public number check.
        /// <paramref name="inStyleRules"/> says it is a paragraph's StyleRules, where liblcm also refuses ktptEditable (155); on a run it
        /// ignores it. A property liblcm silently drops (ktptTableRule 161, unknown numbers, tabList, tabDef) is not refused.</summary>
        internal static bool IsPropertyRefused(int tpt, bool inStyleRules) => RefusedInts.Contains(tpt) || (inStyleRules && tpt == Editable);

        /// <summary>Why liblcm cannot load back this integer property with this variation and value, or null when it can.
        /// <paramref name="inStyleRules"/> is true for a paragraph's StyleRules and false for a run.</summary>
        public static Refusal? ForInt(int tpt, int variation, int value, bool inStyleRules)
        {
            // The property builder takes this pair to mean "delete the property", so nothing reaches liblcm.
            if (variation == -1 && value == -1) return null;
            if (IsPropertyRefused(tpt, inStyleRules)) return Refusal.Property;
            string? why = ValueProblem(tpt, variation, value, inStyleRules);
            return why == null ? null : Refusal.Value(why);
        }

        static string? ValueProblem(int tpt, int variation, int value, bool inStyleRules)
        {
            switch (tpt)
            {
                case Italic:
                case Bold:
                case Superscript:
                case SpellCheck:
                    return value < 0 || value > 2 ? "this property takes 0, 1 or 2" : null;
                case Underline:
                    return value < 0 || value > 6 ? "underline takes 0 to 6" : null;
                case Align:
                    return value < 0 || value > 5 ? "alignment takes 0 to 5" : null;
                case FontSize:
                    if (variation < 0 || variation > 2) return "font size takes a default, millipoint or relative variation";
                    return value < 0 ? "font size cannot be negative" : null;
                case Offset:
                    if (variation != 1 && variation != 2) return "offset takes a millipoint or relative variation";
                    return value < 0 && !inStyleRules ? "a run's offset cannot be negative" : null;
                case LineHeight:
                    if (variation != 1 && variation != 2) return "line height takes a millipoint or relative variation";
                    return value == int.MinValue ? "line height cannot be the minimum integer" : null;
                case BulNumStartAt:
                    return value < 0 && value != int.MinValue ? "bullet start number cannot be negative" : null;
            }
            return NonNegativeInts.Contains(tpt) && value < 0 ? "this property cannot be negative" : null;
        }

        /// <summary>Why liblcm cannot load back this string property value, or null when it can. A string property liblcm does not write
        /// at all is never refused.</summary>
        public static Refusal? ForString(int tpt, string value, bool inStyleRules)
        {
            string? why = StringProblem(tpt, value ?? string.Empty, inStyleRules);
            return why == null ? null : Refusal.Value(why);
        }

        static string? StringProblem(int tpt, string value, bool inStyleRules)
        {
            if (TextStrings.Contains(tpt)) return HasInvalidXmlChar(UpToNul(value)) ? "it holds a character XML cannot carry" : null;
            switch (tpt)
            {
                case Tags:
                    // Tags are GUIDs; fewer than one makes liblcm's save throw after a load.
                    return value.Length < GuidChars ? "tags are 16-byte GUIDs, so fewer than eight characters is not a tag" : null;
                case ObjData:
                    return ObjDataProblem(value);
                case BulNumFontInfo:
                case WsStyle:
                    // Packed records, written by the text-props writer only: a run does not write them. liblcm is the one that knows
                    // whether this one is well formed, so ask it.
                    return !inStyleRules || LiblcmRoundTrips(tpt, value) ? null : "liblcm cannot read or write this packed value";
            }
            return null;
        }

        // liblcm normalizes text through a native routine that stops at the first NUL; what follows is lost, which is a loss, not a refusal.
        static string UpToNul(string value)
        {
            int nul = value.IndexOf('\0');
            return nul < 0 ? value : value.Substring(0, nul);
        }

        // Object data starts with a type character. The GUID types need their GUID; the path and embedded types are written as text.
        static string? ObjDataProblem(string value)
        {
            if (value.Length == 0) return null;
            switch ((FwObjDataTypes)value[0])
            {
                case FwObjDataTypes.kodtNameGuidHot:
                case FwObjDataTypes.kodtOwnNameGuidHot:
                case FwObjDataTypes.kodtContextString:
                case FwObjDataTypes.kodtGuidMoveableObjDisp:
                    return value.Length - 1 < GuidChars ? "object data of this type holds a GUID, eight characters after the type" : null;
                case FwObjDataTypes.kodtExternalPathName:
                case FwObjDataTypes.kodtEmbeddedObjectData:
                    return HasInvalidXmlChar(UpToNul(value.Substring(1))) ? "it holds a character XML cannot carry" : null;
            }
            return null;
        }

        static bool HasInvalidXmlChar(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (XmlConvert.IsXmlChar(c)) continue;
                if (i + 1 < text.Length && XmlConvert.IsXmlSurrogatePair(text[i + 1], c)) { i++; continue; }
                return true;
            }
            return false;
        }

        static bool LiblcmRoundTrips(int tpt, string value)
        {
            try
            {
                var builder = TsStringUtils.MakePropsBldr();
                builder.SetStrPropValue(tpt, value);
                var factory = new HandlesFactory(AnyWritingSystem.Instance);
                var props = builder.GetTextProps();
                var back = TsStringUtils.GetTextProps(XElement.Parse(TsStringUtils.GetXmlRep(props, factory)), factory);
                TsStringUtils.GetXmlRep(back, factory);
                return true;
            }
            catch (Exception)
            {
                // Whatever liblcm throws on this value (an XML schema error on read, an index error on write) is the refusal.
                return false;
            }
        }

        /// <summary>Every writing-system handle names a writing system, so a packed value is judged by its shape alone.</summary>
        sealed class AnyWritingSystem : IWritingSystemHandles
        {
            public static readonly AnyWritingSystem Instance = new AnyWritingSystem();
            public int GetHandle(string wsId) => 0;
            public string GetId(int handle) => handle > 0 ? "ws" + handle : string.Empty;
        }
    }

    /// <summary>A reason liblcm cannot load a property back: the whole property (by number) or this value of it. It cannot name
    /// <see cref="DroppedReason.ValueReplaced"/>, which is the adapter's choice, not liblcm's.</summary>
    internal sealed class Refusal
    {
        Refusal(string? detail) { Detail = detail; }

        public static readonly Refusal Property = new Refusal(null);

        public static Refusal Value(string detail) => new Refusal(detail);

        /// <summary>What is wrong with the value, in words; null when the whole property is refused.</summary>
        public string? Detail { get; }

        public DroppedReason Reason => Detail == null ? DroppedReason.PropertyNotLoadable : DroppedReason.ValueNotLoadable;
    }
}
