using System;
using System.Collections.Generic;
using System.Globalization;

namespace SIL.TextEditing.Markdown.Notation
{
    internal enum PropShape { Toggle, Enum, Measure, LineHeight, Color, Plain, Text }

    internal sealed class PropDef
    {
        public PropDef(int number, bool isString, string key, PropShape shape, params string[] names)
        {
            Number = number;
            IsString = isString;
            Key = key;
            Shape = shape;
            Names = names;
        }

        public int Number { get; }
        public bool IsString { get; }
        public string Key { get; }
        public PropShape Shape { get; }
        /// <summary>For toggles and enums, the name of value 0, 1, 2, ...</summary>
        public string[] Names { get; }
        public string UnitKey => Key + "Unit";
        public string TypeKey => Key + "Type";
    }

    /// <summary>Receives what the property reader finds wrong with an item.</summary>
    internal interface IAttrIssues
    {
        void Report(string id, MarkdownSeverity severity, string message, AttrItem item, string? replacement = null);
    }

    /// <summary>The number-to-key map of spec 3.3, and the conversion of a <see cref="PropertyBag"/> to attribute items and back.
    /// Properties without a named key, and values a named key cannot spell exactly, use <c>pN="value:variant"</c> and <c>sN="..."</c>,
    /// so no property is ever lost.</summary>
    internal static class PropertyKeys
    {
        public const int VarDefault = 0, VarMilli = 1, VarRel = 2, VarEnum = 3, VarNinch = 15;
        const int ColorTransparent = unchecked((int)0xC0000000);

        static readonly PropDef[] Defs =
        {
            new PropDef(2, false, "italic", PropShape.Toggle, "off", "on", "invert"),
            new PropDef(3, false, "bold", PropShape.Toggle, "off", "on", "invert"),
            new PropDef(4, false, "superscript", PropShape.Enum, "off", "super", "sub"),
            new PropDef(5, false, "underline", PropShape.Enum, "none", "dotted", "dashed", "single", "double", "strikethrough", "squiggle"),
            new PropDef(17, false, "align", PropShape.Enum, "leading", "left", "center", "right", "trailing", "justify"),
            new PropDef(26, false, "spellcheck", PropShape.Enum, "normal", "donotcheck", "forcecheck"),
            new PropDef(155, false, "editable", PropShape.Enum, "no", "yes", "semi"),
            new PropDef(6, false, "fontsize", PropShape.Measure),
            new PropDef(7, false, "offset", PropShape.Measure),
            new PropDef(8, false, "forecolor", PropShape.Color),
            new PropDef(9, false, "backcolor", PropShape.Color),
            new PropDef(10, false, "undercolor", PropShape.Color),
            new PropDef(25, false, "paracolor", PropShape.Color),
            new PropDef(142, false, "borderColor", PropShape.Color),
            new PropDef(18, false, "firstIndent", PropShape.Measure),
            new PropDef(19, false, "leadingIndent", PropShape.Measure),
            new PropDef(20, false, "trailingIndent", PropShape.Measure),
            new PropDef(21, false, "spaceBefore", PropShape.Measure),
            new PropDef(22, false, "spaceAfter", PropShape.Measure),
            new PropDef(23, false, "tabDef", PropShape.Measure),
            new PropDef(24, false, "lineHeight", PropShape.LineHeight),
            new PropDef(50, false, "MarginTop", PropShape.Measure),
            new PropDef(134, false, "padLeading", PropShape.Measure),
            new PropDef(135, false, "padTrailing", PropShape.Measure),
            new PropDef(136, false, "padTop", PropShape.Measure),
            new PropDef(137, false, "padBottom", PropShape.Measure),
            new PropDef(138, false, "borderTop", PropShape.Measure),
            new PropDef(139, false, "borderBottom", PropShape.Measure),
            new PropDef(140, false, "borderLeading", PropShape.Measure),
            new PropDef(141, false, "borderTrailing", PropShape.Measure),
            new PropDef(128, false, "rightToLeft", PropShape.Plain),
            new PropDef(129, false, "directionDepth", PropShape.Plain),
            new PropDef(143, false, "bulNumScheme", PropShape.Plain),
            new PropDef(144, false, "bulNumStartAt", PropShape.Plain),
            new PropDef(148, false, "keepWithNext", PropShape.Plain),
            new PropDef(149, false, "keepTogether", PropShape.Plain),
            new PropDef(162, false, "widowOrphan", PropShape.Plain),
            new PropDef(1, true, "fontFamily", PropShape.Text),
            new PropDef(130, true, "fontVariations", PropShape.Text),
            new PropDef(4, true, "tabList", PropShape.Text),
            new PropDef(5, true, "tags", PropShape.Text),
            new PropDef(145, true, "bulNumTxtBef", PropShape.Text),
            new PropDef(146, true, "bulNumTxtAft", PropShape.Text),
        };

        static readonly Dictionary<int, PropDef> IntByNumber = new Dictionary<int, PropDef>();
        static readonly Dictionary<int, PropDef> StrByNumber = new Dictionary<int, PropDef>();
        static readonly Dictionary<string, PropDef> ByKey = BuildByKey();

        // The property numbers FieldWorks defines (ktpt*); a pN or sN outside these is kept but reported as a warning.
        static readonly HashSet<int> KnownInts = new HashSet<int>
        {
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 50, 128, 129, 130, 133, 134, 135, 136, 137, 138, 139,
            140, 141, 142, 143, 144, 148, 149, 150, 151, 152, 153, 154, 155, 156, 159, 160, 161, 162, 9998, 9999,
        };
        static readonly HashSet<int> KnownStrings = new HashSet<int> { 1, 2, 3, 4, 5, 6, 7, 130, 133, 145, 146, 147, 156, 9998 };

        static Dictionary<string, PropDef> BuildByKey()
        {
            var d = new Dictionary<string, PropDef>(StringComparer.Ordinal);
            foreach (var def in Defs)
            {
                d[def.Key] = def;
                if (def.Key == "MarginTop") d["marginTop"] = def; // liblcm's reader spells it this way (spec 3.3)
                if (def.IsString) StrByNumber[def.Number] = def; else IntByNumber[def.Number] = def;
            }
            return d;
        }

        /// <summary>Every key the property reader understands, for "did you mean" hints.</summary>
        public static IEnumerable<string> AllKeys()
        {
            foreach (var def in Defs)
            {
                yield return def.Key;
                if (def.Shape == PropShape.Measure || def.Shape == PropShape.LineHeight) yield return def.UnitKey;
                if (def.Shape == PropShape.LineHeight) yield return def.TypeKey;
            }
        }

        public static string VariationName(int variation)
        {
            switch (variation)
            {
                case VarDefault: return "default";
                case VarMilli: return "mpt";
                case VarRel: return "rel";
                case VarEnum: return "enum";
                case VarNinch: return "ninch";
                default: return variation.ToString(CultureInfo.InvariantCulture);
            }
        }

        static bool TryVariation(string text, out int variation)
        {
            switch (text)
            {
                case "default": variation = VarDefault; return true;
                case "mpt": variation = VarMilli; return true;
                case "rel": variation = VarRel; return true;
                case "enum": variation = VarEnum; return true;
                case "ninch": variation = VarNinch; return true;
            }
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out variation);
        }

        static string Int(int v) => v.ToString(CultureInfo.InvariantCulture);

        // ---------------------------------------------------------------- writing

        /// <summary>One written item: a key and its unescaped value (null for a flag).</summary>
        internal struct Item
        {
            public Item(string key, string? value) { Key = key; Value = value; }
            public string Key;
            public string? Value;
        }

        /// <summary>The items for a bag, in the canonical order of spec 3.3: named keys alphabetically, then <c>pN</c> ascending, then
        /// <c>sN</c> and <c>sNx</c> ascending by N. A string that holds a control character or a lone surrogate is written as <c>sNx</c>, whatever
        /// its name. <paramref name="skipStrings"/> lists string numbers the caller spells itself (style, object data).</summary>
        public static List<Item> ToItems(PropertyBag bag, params int[] skipStrings)
        {
            var named = new List<Item>();
            var ints = new List<KeyValuePair<int, Item>>();
            var strs = new List<KeyValuePair<int, Item>>();
            foreach (var kv in bag.IntProps)
            {
                if (IntByNumber.TryGetValue(kv.Key, out var def) && TryNamed(def, kv.Value, named)) continue;
                ints.Add(new KeyValuePair<int, Item>(kv.Key, new Item("p" + Int(kv.Key), Int(kv.Value.Value) + ":" + VariationName(kv.Value.Variation))));
            }
            foreach (var kv in bag.StrProps)
            {
                if (Array.IndexOf(skipStrings, kv.Key) >= 0) continue;
                if (AttributeText.NeedsHex(kv.Value)) strs.Add(new KeyValuePair<int, Item>(kv.Key, new Item("s" + Int(kv.Key) + "x", AttributeText.Hex(kv.Value))));
                else if (StrByNumber.TryGetValue(kv.Key, out var def)) named.Add(new Item(def.Key, kv.Value));
                else strs.Add(new KeyValuePair<int, Item>(kv.Key, new Item("s" + Int(kv.Key), kv.Value)));
            }
            named.Sort((a, b) =>
            {
                int c = string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase);
                return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
            });
            ints.Sort((a, b) => a.Key.CompareTo(b.Key));
            strs.Sort((a, b) => a.Key.CompareTo(b.Key));
            foreach (var kv in ints) named.Add(kv.Value);
            foreach (var kv in strs) named.Add(kv.Value);
            return named;
        }

        static bool TryNamed(PropDef def, IntPropValue v, List<Item> into)
        {
            switch (def.Shape)
            {
                case PropShape.Toggle:
                case PropShape.Enum:
                    if (v.Variation != VarEnum || v.Value < 0 || v.Value >= def.Names.Length) return false;
                    into.Add(new Item(def.Key, def.Names[v.Value]));
                    return true;
                case PropShape.Measure:
                    if (v.Variation == VarMilli) { into.Add(new Item(def.Key, Int(v.Value))); return true; }
                    if (v.Variation == VarRel)
                    {
                        into.Add(new Item(def.Key, Int(v.Value)));
                        into.Add(new Item(def.UnitKey, "rel"));
                        return true;
                    }
                    return false;
                case PropShape.LineHeight:
                    if ((v.Variation != VarMilli && v.Variation != VarRel) || v.Value == int.MinValue) return false;
                    into.Add(new Item(def.Key, Int(Math.Abs(v.Value))));
                    if (v.Variation == VarRel) into.Add(new Item(def.UnitKey, "rel"));
                    if (v.Value < 0) into.Add(new Item(def.TypeKey, "exact"));
                    return true;
                case PropShape.Color:
                    if (v.Variation != VarDefault) return false;
                    if (v.Value == ColorTransparent) { into.Add(new Item(def.Key, "transparent")); return true; }
                    if (v.Value < 0 || v.Value > 0xFFFFFF) return false;
                    into.Add(new Item(def.Key, ColorHex(v.Value)));
                    return true;
                case PropShape.Plain:
                    if (v.Variation != VarDefault) return false;
                    into.Add(new Item(def.Key, Int(v.Value)));
                    return true;
            }
            return false;
        }

        // A COLORREF holds red in the low byte; the notation spells it rrggbb.
        static string ColorHex(int colorRef)
        {
            int r = colorRef & 0xFF, g = (colorRef >> 8) & 0xFF, b = (colorRef >> 16) & 0xFF;
            return "#" + r.ToString("x2", CultureInfo.InvariantCulture) + g.ToString("x2", CultureInfo.InvariantCulture) + b.ToString("x2", CultureInfo.InvariantCulture);
        }

        // ---------------------------------------------------------------- reading

        static readonly Dictionary<string, int> ColorNames = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["black"] = 0, ["red"] = 255, ["green"] = 65280, ["yellow"] = 65535, ["blue"] = 16711680, ["magenta"] = 16711935,
            ["cyan"] = 16776960, ["white"] = 16777215, ["transparent"] = ColorTransparent,
        };

        /// <summary>Applies property items to a bag. Items that are not property keys are reported through <paramref name="unknown"/>
        /// (so the caller can offer a hint) and left alone.</summary>
        public static void Apply(IReadOnlyList<AttrItem> items, PropertyBag bag, IAttrIssues issues, Action<AttrItem> unknown)
        {
            var seen = new Dictionary<string, AttrItem>(StringComparer.Ordinal);
            var used = new HashSet<AttrItem>();
            var assignedInts = new HashSet<int>();
            var assignedStrs = new HashSet<int>();
            foreach (var item in items)
            {
                if (item.IsClass) continue;
                if (seen.TryGetValue(item.Key, out _))
                {
                    issues.Report(MarkdownDiagnosticIds.DuplicateAttribute, MarkdownSeverity.Error, "'" + item.Key + "' appears twice. Keep one.", item);
                    used.Add(item);
                    continue;
                }
                seen[item.Key] = item;
            }
            foreach (var item in items)
            {
                if (used.Contains(item) || item.IsClass) continue;
                if (ByKey.TryGetValue(item.Key, out var def))
                {
                    used.Add(item);
                    if (item.IsFlag) { NeedsValue(issues, item); continue; }
                    ApplyNamed(def, item, seen, used, bag, issues, assignedInts, assignedStrs);
                    continue;
                }
                int number;
                if (TryNumbered(item.Key, 'p', out number))
                {
                    used.Add(item);
                    if (item.IsFlag) { NeedsValue(issues, item); continue; }
                    ApplyNumberedInt(number, item, bag, issues, assignedInts);
                    continue;
                }
                bool hexKey = item.Key.Length > 2 && item.Key[item.Key.Length - 1] == 'x' && TryNumbered(item.Key.Substring(0, item.Key.Length - 1), 's', out number);
                if (hexKey || TryNumbered(item.Key, 's', out number))
                {
                    used.Add(item);
                    if (item.IsFlag) { NeedsValue(issues, item); continue; }
                    string stringValue = item.Value!;
                    if (hexKey && !AttributeText.TryUnhex(stringValue, out stringValue))
                    {
                        Malformed(issues, item, item.Key + " takes four hex digits for each UTF-16 code unit");
                        continue;
                    }
                    if (!assignedStrs.Add(number)) { Duplicate(issues, item); continue; }
                    if (!KnownStrings.Contains(number))
                        issues.Report(MarkdownDiagnosticIds.PropUnknownNumber, MarkdownSeverity.Warning, "s" + Int(number) + " is not a known string property; it is kept as written.", item);
                    bag.StrProps[number] = stringValue;
                    continue;
                }
            }
            foreach (var item in items)
            {
                if (used.Contains(item)) continue;
                if (item.IsClass) { unknown(item); continue; }
                // A unit or type key whose base key was used is consumed by that key.
                if (IsCompanionOfUsed(item.Key, seen, used)) continue;
                if (Companions.TryGetValue(item.Key, out var companionOf))
                {
                    Malformed(issues, item, item.Key + " goes with " + companionOf.Key + ", which is missing");
                    continue;
                }
                unknown(item);
            }
        }

        static readonly Dictionary<string, PropDef> Companions = BuildCompanions();

        static Dictionary<string, PropDef> BuildCompanions()
        {
            var d = new Dictionary<string, PropDef>(StringComparer.Ordinal);
            foreach (var def in Defs)
            {
                if (def.Shape != PropShape.Measure && def.Shape != PropShape.LineHeight) continue;
                d[def.UnitKey] = def;
                if (def.Shape == PropShape.LineHeight) d[def.TypeKey] = def;
            }
            return d;
        }

        static bool IsCompanionOfUsed(string key, Dictionary<string, AttrItem> seen, HashSet<AttrItem> used)
            => Companions.TryGetValue(key, out var def) && seen.TryGetValue(def.Key, out var baseItem) && used.Contains(baseItem);

        static bool TryNumbered(string key, char prefix, out int number)
        {
            number = 0;
            if (key.Length < 2 || key[0] != prefix) return false;
            for (int i = 1; i < key.Length; i++) if (key[i] < '0' || key[i] > '9') return false;
            return int.TryParse(key.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out number);
        }

        /// <summary>A key written as a bare word is not a flag the grammar knows (spec 3.3: the flags are v and broken).</summary>
        static void NeedsValue(IAttrIssues issues, AttrItem item)
            => issues.Report(MarkdownDiagnosticIds.UnknownAttribute, MarkdownSeverity.Error, "'" + item.Key + "' is not a flag; it takes a value: " + item.Key + "=...", item);

        static void Malformed(IAttrIssues issues, AttrItem item, string message) => issues.Report(MarkdownDiagnosticIds.MalformedAttribute, MarkdownSeverity.Error, message, item);
        static void Duplicate(IAttrIssues issues, AttrItem item) => issues.Report(MarkdownDiagnosticIds.DuplicateAttribute, MarkdownSeverity.Error, "'" + item.Key + "' sets a property that is already set. Keep one.", item);

        static bool TryInt(string text, out int value) => int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

        static void ApplyNumberedInt(int number, AttrItem item, PropertyBag bag, IAttrIssues issues, HashSet<int> assigned)
        {
            string text = item.Value!;
            int variation = VarDefault;
            int colon = text.IndexOf(':');
            string valueText = colon < 0 ? text : text.Substring(0, colon);
            if (colon >= 0 && !TryVariation(text.Substring(colon + 1), out variation))
            {
                Malformed(issues, item, "'" + text.Substring(colon + 1) + "' is not a variant (default, mpt, rel, enum, ninch or a number)");
                return;
            }
            if (!TryInt(valueText, out int value)) { Malformed(issues, item, item.Key + " takes a whole number, optionally followed by :variant"); return; }
            if (!assigned.Add(number)) { Duplicate(issues, item); return; }
            if (!KnownInts.Contains(number))
                issues.Report(MarkdownDiagnosticIds.PropUnknownNumber, MarkdownSeverity.Warning, "p" + Int(number) + " is not a known property; it is kept as written.", item);
            bag.IntProps[number] = new IntPropValue(variation, value);
        }

        static void ApplyNamed(PropDef def, AttrItem item, Dictionary<string, AttrItem> seen, HashSet<AttrItem> used, PropertyBag bag, IAttrIssues issues,
            HashSet<int> assignedInts, HashSet<int> assignedStrs)
        {
            string text = item.Value!;
            if (def.IsString)
            {
                if (!assignedStrs.Add(def.Number)) { Duplicate(issues, item); return; }
                bag.StrProps[def.Number] = text;
                return;
            }
            int variation = VarDefault, value;
            switch (def.Shape)
            {
                case PropShape.Toggle:
                case PropShape.Enum:
                    int index = Array.IndexOf(def.Names, text);
                    if (index < 0) { Malformed(issues, item, "'" + text + "' is not a value of " + def.Key + " (" + string.Join(", ", def.Names) + ")"); return; }
                    value = index;
                    variation = VarEnum;
                    break;
                case PropShape.Color:
                    if (!TryColor(text, out value)) { Malformed(issues, item, def.Key + " takes rrggbb or a colour name"); return; }
                    break;
                case PropShape.Plain:
                    if (!TryInt(text, out value)) { Malformed(issues, item, def.Key + " takes a whole number"); return; }
                    break;
                default: // Measure, LineHeight
                    if (!TryInt(text, out value)) { Malformed(issues, item, def.Key + " takes a whole number"); return; }
                    variation = VarMilli;
                    if (seen.TryGetValue(def.UnitKey, out var unit))
                    {
                        used.Add(unit);
                        if (unit.IsFlag || (unit.Value != "mpt" && unit.Value != "rel")) { Malformed(issues, unit, def.UnitKey + " is mpt or rel"); return; }
                        variation = unit.Value == "rel" ? VarRel : VarMilli;
                    }
                    if (def.Shape == PropShape.LineHeight && seen.TryGetValue(def.TypeKey, out var type))
                    {
                        used.Add(type);
                        if (type.IsFlag || (type.Value != "exact" && type.Value != "atLeast")) { Malformed(issues, type, def.TypeKey + " is atLeast or exact"); return; }
                        if (type.Value == "exact") value = -value;
                    }
                    break;
            }
            if (!assignedInts.Add(def.Number)) { Duplicate(issues, item); return; }
            bag.IntProps[def.Number] = new IntPropValue(variation, value);
        }

        static bool TryColor(string text, out int colorRef)
        {
            if (ColorNames.TryGetValue(text, out colorRef)) return true;
            string hex = text.Length > 0 && text[0] == '#' ? text.Substring(1) : text;
            colorRef = 0;
            if (hex.Length != 6) return false;
            int rgb = 0;
            foreach (char c in hex)
            {
                int d = AttributeScanner.HexValue(c);
                if (d < 0) return false;
                rgb = rgb * 16 + d;
            }
            int r = (rgb >> 16) & 0xFF, g = (rgb >> 8) & 0xFF, b = rgb & 0xFF;
            colorRef = r | (g << 8) | (b << 16);
            return true;
        }
    }
}
