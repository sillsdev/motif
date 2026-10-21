# Motif tokens mapped to FieldWorks' FwAvaloniaTheme

**In plain terms:** this table sets each Motif token beside the FieldWorks token that serves the same purpose, says
whether the two values are the same, and gives the reason wherever they differ. Where the values are the same, a test
pins them. Where they differ, Motif has not changed: the differences are listed as proposed follow-ups at the end, each
with the contrast it would have. It is the mapping [ADR 0074](../adr/0074-tokens-map-onto-fwavaloniatheme-and-never-override-semi.md)
asks for, and the deviations it records belong in [docs/ux/deviations.md](../ux/deviations.md).

## Sources

- FieldWorks: pull request 1191 (branch `pr-1191`), `Src/Common/FwAvaloniaTheme/Tokens/FwColorTokens.axaml`,
  `Tokens/DialogTokens.axaml` and `Tokens/DataTree/DataTreeTokens.axaml`. FieldWorks keys are flat names with no
  naming rule; colours sit in a Light and a Dark theme dictionary, dimensions are flat.
- Motif: `src/SIL.Motif.App/Tokens/Primitives.axaml`, `Intent.axaml`, `Components/*.axaml` and
  `Scoped/ControlHeights.axaml`. Names are layered (Primitive, Intent, Component), and a view names only Intent or
  Component keys.
- Values written `Semi` are Semi.Avalonia roles that neither side hard-codes. FieldWorks' own comments give two of
  them as composited on white: `SemiColorBorder` about #EDEDED and `SemiColorText2` about #727477.
- Tests: `tests/SIL.Motif.Tests.App/App/ContrastTests.cs`, `AValueMotifSharesWithFieldWorksStaysAtFieldWorksValue`,
  `ANoFillIsFullyTransparentAsInFieldWorks` and `FieldWorksLightColoursMotifHasNotAdoptedWouldPassContrast`.

Light theme only. Same means the same value, or one alias of the same Semi step.

## Colour

| Motif key | FW key | Value (Motif / FW) | Same or different | Reason |
| --- | --- | --- | --- | --- |
| Intent.Selected.Fill | FwSelectedRowBrush | #E8F1FF (Blue50) / #D7E6FF | Different | FieldWorks pins the legacy WinForms selected-row colour (kclrBackgroundSelRow) for pixel parity; Motif has no WinForms baseline. Same pale-blue family. Adopting it passes: Intent.Accent on #D7E6FF is 5.68:1 (needs 4.5:1). Follow-up 1 |
| Intent.TextMuted, Intent.TextSecondary | FwLabelBrush | Semi (about #727477) and #454C57 / #696969 | Different | FwLabelBrush is the ink measured in FieldWorks' label column. Motif's field labels use TextMuted or TextSecondary by place. #696969 is 5.49:1 on white, 5.34:1 on Surface.Subtle and 4.73:1 on Neutral.Fill, so it passes. Follow-up 2 |
| (none) | FwWsAbbrevBrush | n/a / #404040 | FW only | Writing-system abbreviation gutter ink; Motif has no data-tree writing-system gutter. Intent.Text is the nearest role |
| Intent.Border | FwSliceRuleBrush, FwSectionRuleBrush, FwPickerBorderBrush, FwDialogGroupSeparatorBrush | Semi (about #EDEDED) / LightGray (#D3D3D3) | Different | FieldWorks found the Semi border too faint for a rule and kept the WinForms DataTree divider pen colour. Motif's border is Semi's. LightGray is 1.5:1 on white and the border about 1.2:1; both are decorative dividers, which WCAG 1.4.11 does not measure. Follow-up 3 |
| Intent.Marking.Border | FwDialogFieldBorderBrush | #AEB8C3 (Slate400) / #7A7A7A | Different | FieldWorks wants a bounded input frame to read; #7A7A7A is 4.29:1 on white, above the 3:1 a control boundary needs. Motif's marking border is 2.01:1 on white, but it edges analysis marks, not input fields, and Motif's text boxes take Semi's own border. Follow-up 4 |
| Intent.Marking.Divider | FwBrowseGridLineBrush | #D5DCE4 (Slate200) / #DCDCDC | Different | Both faint grid dividers; Motif's is tinted slate to sit with its marking palette. No contrast case. Follow-up 3 |
| Intent.TextSecondary | FwSectionHeaderBrush | #454C57 (Grey700) / #696969 | Different | FieldWorks takes SystemColors.ControlDarkDark for a section header; Motif's section heading is Intent.Text at semibold 13 (Intent.Type.Section, Intent.Weight.Section), and TextSecondary is for navigation labels. #696969 passes 4.5:1 on white. Follow-up 2 |
| Intent.TextFaint | FwDisabledOptionBrush | Semi / Gray (#808080) | Different | FieldWorks keeps legacy Gray for an unavailable option because Semi's disabled text is lighter. Motif shows an unavailable control at Intent.Opacity.Dimmed (0.35) and a disabled reason beside it. Different mechanism, not a value |
| Intent.Clear | FwTransparentBrush | #00FFFFFF / Transparent | Same | Both are alpha 0. Pinned |
| (none) | FwTreeBoxBorderBrush, FwTreeBoxGlyphBrush | n/a / #A0A0A0, #404040 | FW only | The +/- box of a collapsible data-tree section; Motif has no such box (it uses disclosure marks coloured by Intent.TextMuted) |
| Intent.Focus | (none) | #215CC7 / n/a | Motif only | FieldWorks takes Semi's focus adorner. Motif replaces the pale Semi ring (ADR 0074 keeps Semi keys unoverridden except the two it names) |

Every other Intent colour has no FieldWorks counterpart, because FieldWorks' theme holds only the data-tree and dialog
colours. They are Motif-only families: Surface.\*, Text, Shadow.Raised, Scrim, Accent, Emphasis.\*, Neutral.Fill,
Primary.\*, Link.\*, Success.\*, Warning.\*, Danger.\*, Unread.\*, Proposal.\*, Marking.\* (other than the two rows above),
Opinion.\*, Change.\*, Outcome.\*, Consequence.\*, Severity.\*, Timing.\*, Tooltip.\*. The analysis-mark hues are taken
from FieldWorks' own analysis colours, not its theme: Cyan after the approved-analysis RGB(150,255,255) and Tan after
the machine-guess RGB(234,220,186), each darkened or lightened until its text reads (see Primitives.axaml).

## Spacing, size, stroke and radius

| Motif key | FW key | Value (Motif / FW) | Same or different | Reason |
| --- | --- | --- | --- | --- |
| Intent.Space.Compact | DialogLabelFieldGap | 4 / 4 | Same | Both alias Semi ExtraTight. Pinned |
| Intent.Space.Related | DialogControlGap | 6 / 6 | Same | Pinned |
| Intent.Space.Group | DialogButtonStripGap | 8 / 8 | Same | Both alias Semi Tight. Pinned |
| Intent.Gap.BeforeGroup | DialogGroupSeparation | 0,8,0,0 / 0,8,0,0 | Same | Pinned |
| Intent.Gap.BeforeRelated | DialogControlGapAbove, DialogLabelGroupGapAbove | 0,6,0,0 / 0,6,0,0 | Same | Pinned |
| Intent.Gap.AfterRelated | DataTree.TrailingGap | 0,0,6,0 / 0,0,6,0 | Same | Pinned |
| Intent.Inset.Chip | DataTree.OptionItemPadding | 6,2 / 6,2 | Same | Pinned |
| Intent.Stroke.Box | DialogFieldBorderThickness, DataTree.HairlineBorderThickness | 1 / 1 | Same | Both are Semi's control border thickness. Pinned |
| Intent.Stroke.DividerTop | DataTree.TopHairlineBorderThickness | 0,1,0,0 / 0,1,0,0 | Same | Pinned |
| Intent.Stroke.DividerBottom | DataTree.BottomHairlineBorderThickness | 0,0,0,1 / 0,0,0,1 | Same | Pinned |
| Intent.Radius.Control | DataTree.PickerCornerRadius | 3 / Semi Small (3) | Same | Pinned |
| Intent.Inset.Page, Intent.Inset.Panel | DialogWindowPadding | 8 / 10 | Different | FieldWorks pads a dialog body 10; Motif pads pages and panels 8 as the page review settled. Follow-up 5 |
| Intent.Inset.Tooltip | DataTree.MenuItemPadding | 8,4 / 8,3 | Different | Menu items in FieldWorks keep the WinForms density. Motif's menu takes Semi's. Follow-up 5 |
| Intent.Inset.Row | DataTree.ListRowPadding | 6,4 / 4,1 | Different | FieldWorks' browse rows are about 18 high (DataTree.BrowseRowMinHeight) to match XMLViews; Motif's rows hold a 36-high DenseTable row (Component.DenseTable.RowHeight) |
| Intent.Space.Minimal | DataTree.RowSpacing, DataTree.FieldSpacing | 2 / 1 | Different | A data tree is denser than Motif's lists. Not adopting: Motif has no data tree |
| Primitive.Size.26, Primitive.Size.28 via Scoped/ControlHeights | DialogMinControlHeight | 26 and 28 / 24 (Semi Small) | Different | Motif's compact heights read better with Andika at 13. They are scoped to Motif's roots, so a host's Semi 24 is untouched (ADR 0074). Revisit with the font decision |
| Primitive.Space.\*, Primitive.Inset.\*, Primitive.Size.\* | DataTree.\* sizes, Dialog\*Padding | various | Motif only, by value | Motif's other primitives are layout values for Motif's pages (columns, rails, strips). FieldWorks' DataTree.LabelColumnWidth 150, SplitterWidth 5 and the dialog control paddings (4,2 / 6,1 / 8,2 / 8,3 / 4,1) have no Motif page that needs them |

## Type

Font family and size are not mapped here: pending owner font decision. For the record, FieldWorks' `FwSurfaceFontSize`
is 11 with the platform default family, and Motif's Intent.Type.Body is 13 with bundled Andika.

## Theme variants

Both sides keep colours in a `Light` and a `Dark` entry of `ResourceDictionary.ThemeDictionaries`, consumed with
`DynamicResource`; Motif's Primitives hold one value each and Intent chooses between them per theme. That is the
framework hook, and it is all this slice records. No dark or high-contrast value is added or changed here. FieldWorks'
Dark entries are labelled unreviewed placeholders, and neither side has a high-contrast dictionary. Fuller dark and
high-contrast work follows FieldWorks' net10 migration (deferred by the owner). When Motif is hosted it follows the
host's theme variant.

## FieldWorks resources Motif lacks

- Colour: FwWsAbbrevBrush, FwTreeBoxBorderBrush, FwTreeBoxGlyphBrush, FwDisabledOptionBrush (Motif dims instead),
  FwBrowseGridLineBrush (Motif has Marking.Divider, a different value), and the Dark entries listed above.
- Dialog layout: DialogWindowPadding, DialogControlGap's `DialogLabelFieldGapBelow` (0,0,0,4, a Primitive only),
  DialogGroupPadding, DialogMinControlHeight, DialogFieldPadding, DialogTextBoxPadding, DialogComboBoxPadding,
  DialogButtonPadding, DialogTabItemPadding, DialogListBoxItemPadding.
- Data tree: every other `DataTree.*` key (label and writing-system columns, splitter, tree box, picker and option
  list sizes, link prompt, validation message and paragraph rules). Motif has no data tree.
- Font: FwSurfaceFontSize (see Type).

## Proposed follow-ups (no Motif visual changes in this slice)

ADR 0074 decision 2 says to adopt FieldWorks' light values for these purposes wherever Motif's contrast tests pass.
This slice leaves Motif's visuals alone, so each is a proposal. Each needs the screenshots and size matrix to be
reviewed, and its Motif tests (`FieldWorksLightColoursMotifHasNotAdoptedWouldPassContrast` already shows each would
pass) to be moved to the new value.

1. Selected row: Intent.Selected.Fill #E8F1FF to #D7E6FF. Passes: Accent 5.68:1, Text 16.65:1, TextSecondary 6.87:1.
   The Dark value #1D3354 stays.
2. Label ink: add or retarget one Intent label key to #696969 for field labels and section headers. Passes on Surface
   5.49:1, Surface.Subtle 5.34:1, Neutral.Fill 4.73:1. It is lighter than TextSecondary (8.66:1), so it suits labels,
   not body text.
3. Rules: Intent.Border and the Marking dividers stay unless a view needs a visible rule; then FieldWorks' LightGray
   (#D3D3D3, 1.5:1, decorative) is the candidate. Register as a decision, not a contrast result.
4. Field border: a text-box frame in #7A7A7A (4.29:1 on Surface, 4.18:1 on Surface.Subtle, above the 3:1 non-text
   bar) once Motif's text boxes stop taking Semi's border. Needs a Motif-owned text-box theme, so it is its own slice.
5. Dialog padding and menu density (8 against 10, 8,4 against 8,3): only if Motif's views are hosted beside FieldWorks
   dialogs and the seam shows; otherwise keep Motif's.
6. Control height 26 and 28 against Semi's 24: revisit with the interface font decision.
