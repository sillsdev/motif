# Motif tokens mapped to FieldWorks' FwAvaloniaTheme

**In plain terms:** this table sets each Motif token beside the FieldWorks token that serves the same purpose, says
whether the two values are the same, and gives the reason wherever they differ. Where the values are the same, a test
pins them. Where FieldWorks' value passes Motif's contrast tests, Motif has adopted it. Where it fails, or Motif
deliberately differs, the difference is a follow-up at the end with its reason. It is the mapping [ADR 0074](../adr/0074-tokens-map-onto-fwavaloniatheme-and-never-override-semi.md)
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
  `ANoFillIsFullyTransparentAsInFieldWorks`, `AColourMotifAdoptedFromFieldWorksStaysAtItsValue`,
  `TheAdoptedFieldWorksColoursReadOnMotifsLightSurfaces` and `FieldWorksLabelInkStillFailsOnTheSelectedRow`.

Light theme only. Same means the same value, or one alias of the same Semi step.

## Colour

| Motif key | FW key | Value (Motif / FW) | Same or different | Reason |
| --- | --- | --- | --- | --- |
| Intent.Selected.Fill | FwSelectedRowBrush | #D7E6FF (Blue75) / #D7E6FF | Same (adopted) | Was #E8F1FF. FieldWorks' legacy WinForms selected-row colour. Passes: Accent 5.68:1, Text 16.65:1, TextSecondary 6.87:1 (need 4.5:1). Dark #1D3354 unchanged. Pinned |
| Intent.TextMuted, Intent.TextSecondary | FwLabelBrush | Semi (about #727477) and #454C57 / #696969 | Different (not adopted) | FwLabelBrush is the ink measured in FieldWorks' label column. It is 4.35:1 on the adopted selected row, under 4.5:1, so TextSecondary cannot take it; TextMuted is a Semi role and is never overridden (ADR 0074). Motif has no label-only key to give it. Follow-up 1 |
| (none) | FwWsAbbrevBrush | n/a / #404040 | FW only | Writing-system abbreviation gutter ink; Motif has no data-tree writing-system gutter. Intent.Text is the nearest role |
| Intent.Marking.Divider | FwSliceRuleBrush, FwSectionRuleBrush, FwPickerBorderBrush, FwDialogGroupSeparatorBrush | #D3D3D3 (Grey200) / LightGray (#D3D3D3) | Same (adopted) | Was #D5DCE4 (Slate200). Motif's divider is the key its analysis rules and strips use. Decorative, so no contrast bar. Dark unchanged. Pinned |
| Intent.Border | same four | Semi (about #EDEDED) / LightGray | Different (not adopted) | A Semi role, never overridden (ADR 0074). Follow-up 2 |
| Intent.Marking.Border | FwDialogFieldBorderBrush | #7A7A7A (Grey550) / #7A7A7A | Same (adopted) | Was #AEB8C3 (Slate400, 2.01:1). Edges Motif's compact controls such as action chips; #7A7A7A is 4.29:1 on Surface and 4.18:1 on Surface.Subtle, above the 3:1 a control boundary needs. Dark unchanged. Pinned. Motif's text boxes still take Semi's own border (follow-up 2) |
| Intent.Marking.Divider | FwBrowseGridLineBrush | #D3D3D3 / #DCDCDC | Different | Both faint grid dividers; Motif uses the rule colour above for both. Decorative |
| Intent.Text | FwSectionHeaderBrush | Semi Text0 / #696969 | Different (not adopted) | FieldWorks takes SystemColors.ControlDarkDark; Motif's section heading is Intent.Text at semibold 13 (Intent.Type.Section, Intent.Weight.Section), a Semi role. Follow-up 1 |
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
| Intent.Inset.Page, Intent.Inset.Panel | DialogWindowPadding | 8 / 10 | Different | FieldWorks pads a dialog body 10; Motif pads pages and panels 8 as the page review settled. Follow-up 3 |
| Intent.Inset.Tooltip | DataTree.MenuItemPadding | 8,4 / 8,3 | Different | Menu items in FieldWorks keep the WinForms density. Motif's menu takes Semi's. Follow-up 3 |
| Intent.Inset.Row | DataTree.ListRowPadding | 6,4 / 4,1 | Different | FieldWorks' browse rows are about 18 high (DataTree.BrowseRowMinHeight) to match XMLViews; Motif's rows hold a 36-high DenseTable row (Component.DenseTable.RowHeight) |
| Intent.Space.Minimal | DataTree.RowSpacing, DataTree.FieldSpacing | 2 / 1 | Different | A data tree is denser than Motif's lists. Not adopting: Motif has no data tree |
| Primitive.Size.26, Primitive.Size.28 via Scoped/ControlHeights | DialogMinControlHeight | 26 and 28 / 24 (Semi Small) | Different | Motif's compact heights read better with Andika at 13. They are scoped to Motif's roots, so a host's Semi 24 is untouched (ADR 0074). Revisit with the font follow-up (4) |
| Primitive.Space.\*, Primitive.Inset.\*, Primitive.Size.\* | DataTree.\* sizes, Dialog\*Padding | various | Motif only, by value | Motif's other primitives are layout values for Motif's pages (columns, rails, strips). FieldWorks' DataTree.LabelColumnWidth 150, SplitterWidth 5 and the dialog control paddings (4,2 / 6,1 / 8,2 / 8,3 / 4,1) have no Motif page that needs them |

## Type

Interface font: the owner has decided on Andika, to be bundled with FieldWorks (FieldWorks pull request 1196, still
open). Motif already bundles Andika (Primitive.Font.UI) at 13 DIP (Intent.Type.Body). FieldWorks today sets no UI
family (platform default) and its `FwSurfaceFontSize` is 11. Adopting FieldWorks' font token and size is out of scope
for this slice and is follow-up 4, once 1196 lands.

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

## Follow-ups

Adopted in this slice (each passes contrast, each pinned): selected row, field border (Marking.Border), rules
(Marking.Divider). Not adopted, with the reason:

1. Label ink and section header #696969: fails 4.5:1 on the new selected row (4.35:1), so TextSecondary cannot take it;
   TextMuted and Text are Semi roles. A label-only Intent key needs a first consumer (the token gate rejects unused
   keys) and a Dark value, so it waits for the field and form pages. On white it is 5.49:1, so it suits labels off a
   selected row.
2. Semi-role colours (Intent.Border rules, text-box frames): FieldWorks' LightGray rule (#D3D3D3, decorative) and
   #7A7A7A input frame need a Motif-owned text-box and card theme, since a Semi key is never overridden app-wide.
3. Dialog padding and menu density (8 against 10, 8,4 against 8,3): only if Motif's views are hosted beside FieldWorks
   dialogs and the seam shows; otherwise Motif keeps its own.
4. Interface font and size (Andika per FieldWorks 1196, 11 against 13): adopt FieldWorks' font token once 1196 lands.
   Control heights 26 and 28 against Semi's 24 are revisited with it.
