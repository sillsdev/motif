# Two Avalonia versions

One source tree serves both Avalonia generations (ADR 0009).

| Target | Avalonia | Who pins it | Runs on |
|---|---|---|---|
| netstandard2.0 | 11.3.17 | FieldWorks | net48, inside WinForms |
| net10.0 | 12.1.2 | Motif | net10.0 |

The versions are set per target framework in `Directory.Packages.props`.

## Per-version differences

Most of the code needs no per-version branch:

- The spike's one per-version spot (the clipboard API) is not needed, because Avalonia 11.3.17 already has the `DataTransfer` API that 12
  uses.
- `FontFeature` parsing is written without `FontFeatureCollection.Parse` (absent in 11.3) for both.

Keep any future per-version difference small, behind `#if NET10_0_OR_GREATER`, and list it here. The differences now (every one is behind
`#if NET10_0_OR_GREATER`):

- **`SpellScanner`** (`Spelling.cs`) asks `CharUnicodeInfo` for the category of a code point directly on net10.0 and through a
  one-character string on netstandard2.0 (no overload there). That only costs an allocation for a letter outside the basic plane.
- **`ScriptPlacement`** reads a font's OS/2 and hhea tables with `IGlyphTypeface.TryGetTable` on netstandard2.0. Avalonia 12 gives no
  access to tables, so on net10.0 it reads them from the font file's own table directory: once for each font; in a font collection from the
  member whose family name is the typeface's; and nothing is read whole.
- **`ClusterFuser`** adds only the Indic conjunct rule (GB9c) to `StringInfo` on net10.0. On netstandard2.0 it also fuses Extend,
  SpacingMark, emoji ZWJ sequences and regional-indicator flags itself (see [Graphemes](rendering.md#graphemes)).
- **`RichTextField`'s dispatcher.** Avalonia 12 lets a control live on a dispatcher other than the main UI thread's, so on net10.0 the
  field uses its own `Dispatcher` (and the typing-pause timer is created on it); Avalonia 11 has only `Dispatcher.UIThread`.
- **`RichTextField.ContextMenu.cs`** has one extra `using Avalonia.Input` on net10.0.

## Packages used by the samples only

Avalonia.Desktop, Avalonia.Themes.Fluent and Avalonia.Win32.Interoperability are used by the samples only; the libraries do not reference
them. The playground tests load the Fluent theme so the sample's buttons and pickers have templates.

## Avalonia faults, pinned by tests

Tests pin these faults. When such a test starts failing, remove the workaround, not the test.

- **Right-to-left hit testing on 11.3.17.** Hit testing in a right-to-left paragraph is wrong on 11.3.17 (fixed in 12.1.2).
- **Lines of several runs, both versions.** On both 11.3.17 and 12.1.2 the caret, hit testing and selection of a left-to-right run are
  wrong on a right-to-left paragraph's line of two or more runs, and likewise on a left-to-right paragraph's line of two or more runs with
  a right-to-left run in it (on 12 some of its ranges throw).
  - The layout works around these first two by reading such a line from its runs as they are drawn (`LineInfo.UsesRuns`: caret x, hit
    testing, visual stops and selection boxes from each run's glyphs).
  - It is not behind `#if`, because 12 has the second fault too (ADR 0027, which supersedes the 11.3-only scope of ADR 0015).
- **Inline objects in right-to-left text** are mis-ordered on both. `RichTextField.ObjectAsGlyph` is the workaround.
- **Wrapped Arabic before trailing spaces, 12 only.** The spaces a wrapped line ends with after a run of Arabic in a left-to-right paragraph
  come with glyph clusters numbered against the run's direction, and answer distances and hits that do not add up (a click at the end of
  the line lands one space short). `RunSlot` places such a run by counting its spaces (`ClusterOrderTests` names the fault).
- **Zero width joiner, 11.3 only.** The formatter throws "Cannot split: requested length N consumes entire run" for some sequences with a
  zero width joiner, such as one after a space before Arabic text in a left-to-right paragraph (`ZwjAfterSpaceTests` names it).
  - The layout catches the refusal and lays the paragraph out again with the joiners swapped for word joiners, then the other format
    characters, then (for a space followed by a mark, the other way the formatter refuses) the space swapped for a dotted circle so the mark
    is still drawn, and as a last resort every format character and mark swapped for a word joiner. The model, the offsets and the grapheme
    boundaries are untouched.
  - Two costs of the retry, on 11.3 only: the no-break space it puts before a mark does not hang at a line end the way a space does, and the
    dotted circle is drawn where the model has a space (the caret and the offsets still treat it as the space).
- **Empty ranges, 12 only.** Asking for the boxes of a range that covers no width (a right-to-left mark alone before the spaces a line ends
  with) throws "Covered length must be greater than zero". The layout catches it, in the boxes of a range and in the spaces a justified line
  stretches, and answers that such a range has no box (`RlmWrapTests` names it).
