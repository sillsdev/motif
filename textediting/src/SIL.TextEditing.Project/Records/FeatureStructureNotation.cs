using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using SIL.LCModel;
using SIL.LCModel.Core.KernelInterfaces;

namespace SIL.TextEditing.Project
{
    /// <summary>A failure to render or read a feature-structure text, with a stable code (<c>fs-notation.*</c>) a host can show or map.</summary>
    public sealed class FeatureStructureNotationException : InvalidOperationException
    {
        internal FeatureStructureNotationException(string code, string message) : base(message) => Code = code;

        /// <summary>One of the <see cref="FeatureStructureNotation"/> code constants.</summary>
        public string Code { get; }
    }

    /// <summary>One closed feature and the value it takes in a structure.</summary>
    public sealed class FeatureStructureSelection
    {
        internal FeatureStructureSelection(IFsClosedFeature feature, IFsSymFeatVal value)
        {
            Feature = feature;
            Value = value;
        }

        public IFsClosedFeature Feature { get; }
        public IFsSymFeatVal Value { get; }
    }

    /// <summary>The text form of a feature structure (<c>FsFeatStruc</c> with closed values): <c>[cas:nominative num:singular]</c>.
    /// <para>A structure is square brackets around zero or more <c>feature:value</c> pairs separated by white space; <c>[]</c> is the empty structure.
    /// A feature is written by its abbreviation and a value by its name, in the project's default analysis writing system, the way FieldWorks
    /// shows a structure (<c>[cas:nominative num:plural]</c>); an empty abbreviation (or name) falls back to the name (or abbreviation). In a label, a backslash, a square bracket, a colon and each white-space character are written after a backslash.
    /// Pairs are in ordinal order of the feature label, so a render is canonical and a diff of two renders shows only the pairs that differ.
    /// Reading ignores the white space around brackets and pairs and accepts any pair order; <see cref="Render"/> never emits them.</para>
    /// <para>Only closed values are written. A structure that holds a complex, negated, shared, disjunctive or open value, a closed value with no feature or no value,
    /// a feature twice, a label that is empty or has a control character, or a label that more than one feature (or more than one value of a feature) has,
    /// cannot be written without losing or guessing: <see cref="Render"/> refuses it. A structure's declared type is not part of the text.</para></summary>
    public static class FeatureStructureNotation
    {
        /// <summary>The notation name a record schema's <c>notation=</c> attribute gives.</summary>
        public const string Name = "feature-structure";

        /// <summary>The text is not a bracketed list of <c>feature:value</c> pairs.</summary>
        public const string Syntax = "fs-notation.syntax";
        /// <summary>No closed feature of the feature system has the label.</summary>
        public const string UnknownFeature = "fs-notation.unknown-feature";
        /// <summary>The feature has no value with the label.</summary>
        public const string UnknownValue = "fs-notation.unknown-value";
        /// <summary>More than one feature (or more than one value of the feature) has the label, so it does not name one.</summary>
        public const string AmbiguousLabel = "fs-notation.ambiguous-label";
        /// <summary>The text names one feature twice.</summary>
        public const string RepeatedFeature = "fs-notation.repeated-feature";
        /// <summary>The label names a feature the notation cannot write (a complex feature).</summary>
        public const string UnsupportedFeature = "fs-notation.unsupported-feature";
        /// <summary>The structure holds something the text cannot show.</summary>
        public const string Unrenderable = "fs-notation.unrenderable";

        /// <summary>The canonical text of the structure.</summary>
        /// <exception cref="FeatureStructureNotationException">The structure holds something the text cannot show (<see cref="Unrenderable"/>) or two
        /// features or values share a label (<see cref="AmbiguousLabel"/>).</exception>
        public static string Render(IFsFeatStruc structure, LcmCache cache)
        {
            if (structure == null) throw new ArgumentNullException(nameof(structure));
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            int ws = cache.DefaultAnalWs;
            var pairs = new List<(string Feature, string Value)>();
            var seen = new HashSet<Guid>();
            foreach (var spec in structure.FeatureSpecsOC)
            {
                if (!(spec is IFsClosedValue closed))
                    throw new FeatureStructureNotationException(Unrenderable, "The structure holds a " + spec.ClassName + ", which the text form does not show.");
                if (!(closed.FeatureRA is IFsClosedFeature feature) || closed.ValueRA == null)
                    throw new FeatureStructureNotationException(Unrenderable, "The structure holds a value with no feature or no choice.");
                if (!seen.Add(feature.Guid))
                    throw new FeatureStructureNotationException(Unrenderable, "The structure gives feature " + feature.Guid.ToString("D") + " more than once.");
                string featureLabel = Label(feature.Abbreviation, feature.Name, ws)
                    ?? throw new FeatureStructureNotationException(Unrenderable, "Feature " + feature.Guid.ToString("D") + " has no abbreviation or name in the default analysis writing system.");
                string valueLabel = Label(closed.ValueRA.Name, closed.ValueRA.Abbreviation, ws)
                    ?? throw new FeatureStructureNotationException(Unrenderable, "Value " + closed.ValueRA.Guid.ToString("D") + " has no abbreviation or name in the default analysis writing system.");
                if (feature.Owner is IFsFeatureSystem system && FeaturesLabelled(system, featureLabel, ws).Count > 1)
                    throw new FeatureStructureNotationException(AmbiguousLabel, "More than one feature is labelled " + featureLabel + ".");
                if (feature.ValuesOC.Count(v => Label(v.Name, v.Abbreviation, ws) == valueLabel) > 1)
                    throw new FeatureStructureNotationException(AmbiguousLabel, "More than one value of " + featureLabel + " is labelled " + valueLabel + ".");
                pairs.Add((featureLabel, valueLabel));
            }
            pairs.Sort((a, b) => string.CompareOrdinal(a.Feature, b.Feature));
            var text = new StringBuilder("[");
            for (int i = 0; i < pairs.Count; i++)
            {
                if (i > 0) text.Append(' ');
                text.Append(Escape(pairs[i].Feature)).Append(':').Append(Escape(pairs[i].Value));
            }
            return text.Append(']').ToString();
        }

        /// <summary>Reads the text against the closed features of <paramref name="system"/>.</summary>
        /// <exception cref="FeatureStructureNotationException">The text is not well formed, names an unknown or ambiguous feature or value, or repeats a feature.</exception>
        public static IReadOnlyList<FeatureStructureSelection> Parse(string text, IFsFeatureSystem system, LcmCache cache)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (system == null) throw new ArgumentNullException(nameof(system));
            if (cache == null) throw new ArgumentNullException(nameof(cache));
            int ws = cache.DefaultAnalWs;
            var result = new List<FeatureStructureSelection>();
            var seen = new HashSet<Guid>();
            foreach (var (featureLabel, valueLabel) in Pairs(text))
            {
                var matches = FeaturesLabelled(system, featureLabel, ws);
                if (matches.Count == 0)
                {
                    bool complex = system.FeaturesOC.Any(f => !(f is IFsClosedFeature) && Label(f.Abbreviation, f.Name, ws) == featureLabel);
                    throw new FeatureStructureNotationException(complex ? UnsupportedFeature : UnknownFeature,
                        complex ? "Feature " + featureLabel + " is complex, which the text form does not write."
                            : "The feature system has no closed feature labelled " + featureLabel + ".");
                }
                if (matches.Count > 1)
                    throw new FeatureStructureNotationException(AmbiguousLabel, "More than one feature is labelled " + featureLabel + ".");
                var feature = matches[0];
                if (!seen.Add(feature.Guid))
                    throw new FeatureStructureNotationException(RepeatedFeature, "Feature " + featureLabel + " is named more than once.");
                var values = feature.ValuesOC.Where(v => Label(v.Name, v.Abbreviation, ws) == valueLabel).ToList();
                if (values.Count == 0)
                    throw new FeatureStructureNotationException(UnknownValue, "Feature " + featureLabel + " has no value labelled " + valueLabel + ".");
                if (values.Count > 1)
                    throw new FeatureStructureNotationException(AmbiguousLabel, "More than one value of " + featureLabel + " is labelled " + valueLabel + ".");
                result.Add(new FeatureStructureSelection(feature, values[0]));
            }
            return result;
        }

        static List<IFsClosedFeature> FeaturesLabelled(IFsFeatureSystem system, string label, int ws)
            => system.FeaturesOC.OfType<IFsClosedFeature>().Where(f => Label(f.Abbreviation, f.Name, ws) == label).ToList();

        static string? Label(IMultiUnicode first, IMultiUnicode second, int ws)
        {
            string text = first.get_String(ws)?.Text ?? string.Empty;
            if (text.Length == 0) text = second.get_String(ws)?.Text ?? string.Empty;
            text = text.Normalize(NormalizationForm.FormC);
            return text.Length == 0 || text.Any(char.IsControl) ? null : text;
        }

        static string Escape(string label)
        {
            var text = new StringBuilder();
            foreach (char c in label)
            {
                if (c == '\\' || c == '[' || c == ']' || c == ':' || char.IsWhiteSpace(c)) text.Append('\\');
                text.Append(c);
            }
            return text.ToString();
        }

        static IEnumerable<(string Feature, string Value)> Pairs(string text)
        {
            int at = 0;
            void Skip() { while (at < text.Length && char.IsWhiteSpace(text[at])) at++; }
            FeatureStructureNotationException Bad(string why) => new FeatureStructureNotationException(Syntax,
                "The feature structure is not written as [feature:value feature:value]: " + why + " (at " + at.ToString(CultureInfo.InvariantCulture) + ").");
            string Label(bool feature)
            {
                var label = new StringBuilder();
                while (at < text.Length)
                {
                    char c = text[at];
                    if (c == '\\')
                    {
                        if (at + 1 >= text.Length) throw Bad("a backslash ends the text");
                        label.Append(text[at + 1]);
                        at += 2;
                    }
                    else if (c == ':' && feature) break;
                    else if (c == ']' || c == '[' || c == ':' || char.IsWhiteSpace(c))
                    {
                        if (feature) throw Bad("a feature is followed by a colon");
                        break;
                    }
                    else { label.Append(c); at++; }
                }
                string result = label.ToString().Normalize(NormalizationForm.FormC);
                if (result.Length == 0) throw Bad(feature ? "a feature has no label" : "a value has no label");
                return result;
            }
            var pairs = new List<(string, string)>();
            Skip();
            if (at >= text.Length || text[at] != '[') throw Bad("it does not start with [");
            at++;
            while (true)
            {
                Skip();
                if (at >= text.Length) throw Bad("it does not end with ]");
                if (text[at] == ']') { at++; break; }
                string feature = Label(true);
                if (at >= text.Length || text[at] != ':') throw Bad("a feature has no colon");
                at++;
                if (at >= text.Length || text[at] == ']' || char.IsWhiteSpace(text[at])) throw Bad("a feature has no value");
                pairs.Add((feature, Label(false)));
            }
            Skip();
            if (at < text.Length) throw Bad("text follows ]");
            return pairs;
        }
    }
}
