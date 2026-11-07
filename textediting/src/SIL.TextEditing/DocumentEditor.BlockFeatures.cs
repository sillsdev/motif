using System;

namespace SIL.TextEditing
{
    // Block features (ADR 0033, owner decision Q79): which of tables, lists and headings this field may hold, beside SingleParagraph.
    public sealed partial class DocumentEditor
    {
        IBlockRecognizer _blockRecognizer = StyleBlockRecognizer.Default;

        /// <summary>The block kinds this field may hold: <see cref="SIL.TextEditing.BlockFeatures.Tables"/>, <see cref="SIL.TextEditing.BlockFeatures.Lists"/> and
        /// <see cref="SIL.TextEditing.BlockFeatures.Headings"/>. The default is <see cref="SIL.TextEditing.BlockFeatures.All"/>, which changes nothing. When a feature is off
        /// (a) the host's commands that would make it are refused: ask <see cref="IsParagraphStyleAllowed"/> (and <see cref="IsAllowed"/>) before applying a
        /// paragraph style, (b) paste, and so Markdown import, turns a block of that kind into plain paragraphs (a table into one paragraph per row, the cells
        /// joined by a space, a bar and a space; see <see cref="BlockFlattener"/>) and raises <see cref="BlocksFlattened"/>, and (c) Enter in an existing block starts a plain paragraph.
        /// As with <see cref="SingleParagraph"/>, blocks the document already holds are kept; only new ones are refused (Find and Replace inside a kept block change its text
        /// and nothing else, and raise nothing). Undo and redo restore exactly what was there. The default <see cref="BlockRecognizer"/> knows no table style: see
        /// <see cref="SIL.TextEditing.BlockFeatures.Tables"/>. A handler of <see cref="BlocksFlattened"/> that throws surfaces from the paste, after the edit has been made.</summary>
        public BlockFeatures BlockFeatures { get; set; } = BlockFeatures.All;

        /// <summary>How a paragraph is recognised as a table row, a list item or a heading (by the host's style names; see <see cref="StyleBlockRecognizer"/>).
        /// Default: <see cref="StyleBlockRecognizer.Default"/>. A host with its own style names, or a later table form, plugs its recogniser in here.</summary>
        /// <exception cref="ArgumentNullException">The value is null.</exception>
        public IBlockRecognizer BlockRecognizer
        {
            get => _blockRecognizer;
            set => _blockRecognizer = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>True when <see cref="BlockFeatures"/> allows the kind (a plain paragraph, <see cref="SIL.TextEditing.BlockFeatures.None"/>, is always allowed).</summary>
        public bool IsAllowed(BlockFeatures kind) => kind == BlockFeatures.None || (BlockFeatures & kind) == kind;

        /// <summary>True when the host may give paragraphs this style: its kind (<see cref="IBlockRecognizer.RecognizeStyle"/>) is allowed. A host asks before
        /// it applies a paragraph style and refuses when this is false; it also uses it to hide the styles a field cannot take.</summary>
        public bool IsParagraphStyleAllowed(string styleName) => IsAllowed(BlockRecognizer.RecognizeStyle(styleName));

        /// <summary>Raised after a paste that turned blocks into plain paragraphs because <see cref="BlockFeatures"/> does not allow them, with what changed.
        /// Not raised when nothing was flattened.</summary>
        public event Action<BlockFlattening>? BlocksFlattened;

        /// <summary>Flattens a fragment's disallowed blocks; null when <see cref="BlockFeatures"/> is <see cref="SIL.TextEditing.BlockFeatures.All"/>
        /// or nothing needed it.</summary>
        BlockFlattening? FlattenFragment(System.Collections.Generic.List<Paragraph> fragment)
        {
            if (BlockFeatures == BlockFeatures.All) return null;
            var result = BlockFlattener.Flatten(fragment, BlockFeatures, BlockRecognizer);
            return result.Total > 0 ? result : null;
        }

        void RaiseBlocksFlattened(BlockFlattening? flattening)
        {
            if (flattening != null) BlocksFlattened?.Invoke(flattening);
        }

        /// <summary>The paragraph Enter just made is new: when its block kind is not allowed it starts plain.</summary>
        void FlattenNewParagraph(Paragraph paragraph)
        {
            if (BlockFeatures == BlockFeatures.All) return;
            var kind = BlockRecognizer.Recognize(paragraph);
            if (BlockFlattener.IsBlockKind(kind) && !IsAllowed(kind)) BlockRecognizer.Flatten(paragraph, kind);
        }
    }
}
