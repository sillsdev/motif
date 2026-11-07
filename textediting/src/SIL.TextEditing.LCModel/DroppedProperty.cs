namespace SIL.TextEditing.LCModel
{
    /// <summary>Why the adapter left a property out.</summary>
    public enum DroppedReason
    {
        /// <summary>liblcm's XML reader refuses the property by number, whatever its value (ADR 0016, amended for issue 136).</summary>
        PropertyNotLoadable,

        /// <summary>liblcm accepts the property but refuses this value: its reader throws on it, its writer throws on it, or a save
        /// after a load throws (ADR 0016, amended for issue 139).</summary>
        ValueNotLoadable,

        /// <summary>liblcm refuses the value, and the model holds the same fact in a typed field (<see cref="Paragraph.Rtl"/>), so the adapter
        /// stored the typed field's value instead of leaving the property out. <see cref="DroppedProperty.Detail"/> says what.</summary>
        ValueReplaced,
    }

    /// <summary>A property the adapter left out of the string or StyleRules it built, because liblcm cannot load it back (its XML reader
    /// refuses the property or this value, or its writer cannot write the value) and a project holding it would be reported as a
    /// corrupted record (ADR 0016, amended). The run model's bag still holds it.</summary>
    public sealed class DroppedProperty
    {
        /// <summary>The stable id of a finding where the property was left out (<see cref="DroppedReason.PropertyNotLoadable"/> and
        /// <see cref="DroppedReason.ValueNotLoadable"/>), for a host that counts or filters findings.</summary>
        public const string DiagnosticId = "textediting.lcm.unstorable-property";

        /// <summary>The stable id of a finding where the adapter stored another value in its place
        /// (<see cref="DroppedReason.ValueReplaced"/>): the host's data is kept, in a different form.</summary>
        public const string ReplacedDiagnosticId = "textediting.lcm.replaced-property";

        public DroppedProperty(int number, bool isString, int? paragraphIndex, int? runIndex,
            DroppedReason reason = DroppedReason.PropertyNotLoadable, string? detail = null)
        {
            Number = number;
            IsString = isString;
            ParagraphIndex = paragraphIndex;
            RunIndex = runIndex;
            Reason = reason;
            Detail = detail;
        }

        /// <summary><see cref="DiagnosticId"/>, or <see cref="ReplacedDiagnosticId"/> when <see cref="Reason"/> is <see cref="DroppedReason.ValueReplaced"/>.</summary>
        public string Id => Reason == DroppedReason.ValueReplaced ? ReplacedDiagnosticId : DiagnosticId;

        /// <summary>The FieldWorks property number.</summary>
        public int Number { get; }

        public bool IsString { get; }

        /// <summary>The paragraph, when the conversion was of a document (<see cref="TsStringConversion.ToTextParagraphs"/>); null for
        /// <see cref="TsStringConversion.ToTsString(System.Collections.Generic.IReadOnlyList{Run}, IWritingSystemHandles, string, System.Action{DroppedProperty}?)"/>,
        /// <see cref="TsStringConversion.ToStyleRules"/> and <see cref="TsStringClipboard.FragmentToXml"/>.</summary>
        public int? ParagraphIndex { get; }

        /// <summary>The run, or null for a paragraph's StyleRules. For <see cref="TsStringClipboard.FragmentToXml"/>, which joins
        /// the paragraphs of a fragment into one string, it is the index into the joined runs (paragraph separators included), and
        /// <see cref="ParagraphIndex"/> is null.</summary>
        public int? RunIndex { get; }

        /// <summary>Whether liblcm refuses the property itself or only this value of it.</summary>
        public DroppedReason Reason { get; }

        /// <summary>For a refused value, what is wrong with it, in words; null for a refused property.</summary>
        public string? Detail { get; }

        public string Message => (IsString ? "String" : "Integer") + " property " + Number
            + (Reason == DroppedReason.ValueReplaced
                ? " was replaced: liblcm cannot load the value in the bag back" + (Detail == null ? "" : " (" + Detail + ")") + "."
                : Reason == DroppedReason.PropertyNotLoadable
                ? " is not stored: liblcm refuses it when it loads the project, which would report a corrupted record."
                : " is not stored: liblcm cannot load this value back" + (Detail == null ? "" : " (" + Detail + ")")
                    + ", which would report a corrupted record.");

        public override string ToString() => Id + " " + Message;
    }
}
