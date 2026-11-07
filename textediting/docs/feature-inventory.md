# R6: FieldWorks text editing feature inventory (for SIL.TextEditing)

Read-only survey, 2026-11-03. Nothing in any repository was changed.

## Legend and path abbreviations

Columns: Feature | Legacy source (file:line) | FwAvalonia has it? | SIL.TextEditing (TE) has it? | Size guess (S/M/L).
TE values: YES, PARTIAL (what), NO. FA values: YES, PARTIAL, NO, N/A. UNVERIFIED = line or behaviour not confirmed by reading the code.

- `V/` = /mnt/h/repos/FieldWorks/Src/views/
- `SRS/` = /mnt/h/repos/FieldWorks/Src/Common/SimpleRootSite/
- `RS/` = /mnt/h/repos/FieldWorks/Src/Common/RootSite/
- `FW/` = /mnt/h/repos/FieldWorks/Src/Common/Framework/
- `FA/` = /mnt/h/repos/FieldWorks/Src/Common/FwAvalonia/Detail/ (the whole FwAvalonia project is a Detail-view port; its text editing is an Avalonia `TextBox` per writing system row with run replay, not a text engine)
- `TE/` = /mnt/h/repos/SIL.TextEditing/src/ (`TE/A/` = SIL.TextEditing.Avalonia; `TE/M/` = SIL.TextEditing model)
- `KRN/` = /mnt/h/repos/FieldWorks/Src/Kernel/TextServ.idh (property and enum definitions)
- `CFG/` = /mnt/h/repos/FieldWorks/DistFiles/Language Explorer/Configuration/Main.xml
- r5 = ~/work/gdr-design-20261102/reports/r5-tsstring-inventory.md (ktpt names, liblcm lines)

## Headline findings (read these first)

1. The legacy property store (`V/VwPropertyStore.cpp`) throws E_NOTIMPL for an int property it has no case for (default at ~1553). Not handled, so effectively "never in a view ttp": TabDef(23), ktptMarkItem(9999). Quietly ignored string props (default at ~1642): CharStyle(2), ParaStyle(3), TabList(4), ObjData(6, handled by the text source instead), CustomBullet(7), FieldName(9998). The modern style carrier is NamedStyle(133), used for both paragraph and character styles (V/VwPropertyStore.cpp:1609). UNVERIFIED that liblcm never writes ktptCharStyle/ParaStyle into live strings.
2. Legacy arrow keys are LOGICAL by default, not visual: `SimpleRootSite.ComplexKeyBehavior` returns Logical (SRS/SimpleRootSite.cs:2433-2438); `VwTextSelection::LeftArrow` maps Left to Forward in an RTL paragraph (V/VwSelection.cpp:7923-7936). TE moves visually (TE/A/RichTextField.cs `MoveVisualLeftRight`). This is a deliberate behaviour decision, not a gap. (Note `nFlags && 0x1` at V/VwRootBox.cpp:1415 is a logical-and, so any non-zero flag means logical.)
3. Legacy Backspace deletes ONE typed character (surrogate pair as one), not a whole cluster; Delete removes a character plus following diacritics (V/VwSelection.cpp:3925-4010). TE deletes a grapheme cluster both ways (TE/M/DocumentEditor.cs `Backspace` and `DeleteForward`, `GraphemeClusters.BackspaceTarget`). Decision needed.
4. Legacy normalises typed input to NFD (V/VwSelection.cpp:3660-3679; IME/NFD special case V/VwTextStore.cpp:2744 `IsNfdIMEActive`) and puts NFC text on the clipboard (SRS/EditingHelper.cs:3166). TE has no Unicode normalisation (`Paragraph.Normalize` only merges runs). Decision needed: where does normalisation live (editor or LCModel adapter)?
5. Legacy has NO Ctrl+B / Ctrl+I / Ctrl+U and no Bold/Italic button; all formatting is by named style plus a few hard-format paths (grep of Keys.B/I/U in SRS, RS, FW, Widgets: no hits). FwAvalonia ADDED Ctrl+B/I/U (FA/FwFieldControls.cs:291-336). Treat as new, not legacy parity.
6. Legacy has NO in-text drag and drop of selected text (no IDropTarget/DoDragDrop in V/*.cpp; no OnDragDrop in SRS/RS/FW; only tree/record-bar DnD in DetailControls/xWorks). Mouse drag only extends the selection (V/VwRootBox.cpp:2253). The owner list in the brief ("drag and drop") is therefore a non-legacy feature.
7. Legacy has NO hyphenation (property exists, "not yet supported": KRN ktptHyphenate comment; V/VwTextBoxes.cpp:1368), NO tab stops (TabDef/TabList unhandled), NO text justification of the last line (by design), NO hyperlink tooltips (commented-out code SRS/SimpleRootSite.cs:~4140-4180, TE-8540/LT-10281), and UIA is disabled (SRS/SimpleRootSite.cs WM_GETOBJECT block, FWR-2874/3045); only MSAA `IAccessible` (V/VwAccessRoot.cpp).
8. Pagination props (KeepWithNext, KeepTogether, WidowOrphanControl) act only in page layout and printing (V/VwLayoutStream.cpp:1252, V/VwSimpleBoxes.cpp:3016-3050, V/VwTextBoxes.cpp:6272). An on-screen editor can carry them.
9. TE draws only: Ws font+size, Bold(on), Italic(on), Underline(any non-zero as one solid line), FontSize (TE/A/ParagraphLayout.cs `MakeProperties`), paragraph direction (`ParagraphLayout.Build`), inline picture ORC (`ParagraphSource.GetTextRun`). Everything else is carried.
10. Biggest functional gaps in TE: spelling, find/replace/highlight, read-only/editable regions, scrolling, link activation, hot-link ORC text expansion, context menus, accessibility TextPattern, paragraph formatting, colour/offset/superscript drawing, Tab/field exit, PageUp/Down, F7/F8, Ctrl+Up/Down, Shift+Enter, Ctrl+Space.

---

## 1. Drawing of each text property (every ktpt; draw or carry; what the legacy engine does)

Engine entry: `VwPropertyStore::put_IntProperty` (V/VwPropertyStore.cpp:1177, case lines below), `put_StringProperty` (:1577), `ApplyTtp` (:2120-2230, applies NamedStyle first, then ws-dependent style, then explicit props; skips DirectionDepth and RelLineHeight, line height last). Values at KRN:140-330. Int props >= 10000 are ignored (:1182). Drawing is in `UniscribeSegment` (`V/lib/UniscribeSegment.cpp`) and `GraphiteSegment`, boxes in `V/VwTextBoxes.cpp` and `V/VwSimpleBoxes.cpp`.

### 1a. Character-level int props

| Feature | Legacy source and what the engine does | FA | TE | Size |
|---|---|---|---|---|
| ktptWs (1): picks font, size defaults, features, RTL, line breaker locale, spell dictionary, keyboard | V/VwPropertyStore.cpp:1380 (sets fWsRtl, nDirDepth from ws); DRAW | YES: per-row ws, run retag FA/FwFieldControls.cs:442-530 | YES: Run.Ws, font and size from registry, TE/A/ParagraphLayout.cs `MakeProperties` | S |
| ktptItalic (2) on/off/invert | :1190-1215 DRAW; invert resolved against parent style (:1196-1206) | PARTIAL: bool Italic FA/DetailModel.cs:137, edited by Ctrl+I | PARTIAL: on only, Invert not resolved (`ParagraphLayout.MakeProperties`) | S |
| ktptBold (3) on/off/invert, numeric weights 100-900, Bolder/Lighter | :1217-1262 DRAW; weight <550 normal; >900 clamped | PARTIAL: bool Bold FA/DetailModel.cs:136 | PARTIAL: on only (`ParagraphLayout.MakeProperties`); no weights, no invert | S |
| ktptSuperscript (4) off/super/sub | :1335; DRAW using the font's OS/2 super/subscript size and offset V/lib/VwGraphics.cpp:1751-1805 | NO (carry-only round-trip check lossy: read-only) | YES: sized and raised or lowered from the font's OS/2 table, FieldWorks' fallback (two thirds, raised the rest of the size, lowered a fifth of its own) where the font has none (`ScriptPlacement`); the ratios are applied to the run's size, a default | M |
| ktptUnderline (5) none/dotted/dashed/single/double/strikethrough/squiggle (KRN:636-647, FwUnderlineType) | :1344; DRAW `VwParagraphBox::DrawUnderline` V/VwTextBoxes.cpp:8761-8820 (continuous, dotted 2px, dashed 6/3, double two lines, strikethrough at ascent/3, squiggle) | PARTIAL: bool Underline FA/DetailModel.cs:138 | YES: dotted, dashed, single, double, strikethrough as plain lines the layout draws itself, unbroken by descenders (`ParagraphLayout.DrawDecorations`; Avalonia's own leave a gap at each), except under a bullet or number or an ellipsis; the squiggle is a zigzag the layout draws after the text (ParagraphLayout.DrawSquiggles) | M |
| ktptUnderColor (10) | :1351; DRAW (clrUnder passed to DrawUnderline) | NO | NO | S |
| ktptFontSize (6) mpt, relative (ktpvRelative), enum sizes | :1264-1313 DRAW | YES: FontSizeMilliPoints FA/DetailModel.cs:135 | YES: mpt only (`ParagraphLayout.MakeProperties`); relative and enum NO | S |
| ktptOffset (7) baseline offset mpt | :1314; DRAW baseline shift V/lib/UniscribeSegment.cpp:993,1023,1877,2303,2721 (underline lowered for subscripts only :2727) | NO | YES: the run is drawn shifted by the layout (LineInfo.Draw), the line slot makes room, caret, selection, backgrounds and squiggle follow | S |
| ktptForeColor (8) | :1362; DRAW; inverted when the range is selected V/lib/UniscribeSegment.cpp:3516-3530 | NO (lossy: read-only) | NO: foreground brush is a field-level brush, run colour ignored (`RunTextProperties`, `ParagraphLayout.MakeProperties`) | S |
| ktptBackColor (9) | :1368; DRAW run background (UniscribeSegment DrawText, :1012-1014 comments) | NO | YES (`RunTextProperties.Back`, painted by `ParagraphLayout.DrawBackgrounds` below the selection; not under the spaces a wrapped line ends with, as FieldWorks' line-end segments leave them out of their width) | S |
| ktptBaseWs (16) | :1403; CARRY mostly: `m_wsBase` seeds the font size for relative line height (:1470) | NO | NO (carried) | S |
| ktptFontFamily string (1) incl. markup `<default serif>`, `<default sans serif>`, `<default monospace>`, `<default body>` | :1577; DRAW; markup resolved by V/../AppCore/FwStyledText.cpp:48-51,1467 | PARTIAL: FA/DetailModel run FontFamily; per-run font display FA/DetailRichTextChrome.cs:30-60 | PARTIAL: carried (ParagraphLayout uses the ws font only, :181) | M |
| ktptFontVariations string (130) OpenType/Graphite feature list, concatenated | :1595; DRAW features parsed V/lib/UniscribeSegment.cpp:241 (`TryParseFontFeatureRecords`), Graphite V/lib/GraphiteSegment.cpp | NO | PARTIAL: OpenType features from the writing system and per run (property 130 in a run's or style's bag is read and appended to them, `StyleResolver`), drawn through Avalonia; no Graphite (ADR 0022) (docs/reference/rendering.md) | M |
| ktptWsStyle string (156): ws-dependent character-style overrides, merged by `ComputeWsStyleInheritance` | :1580-1594, :2120-2160 (explains order); applied when the ws is known; CARRY in string, RESOLVE at draw | NO (lossy: read-only) | NO | L |
| ktptNamedStyle string (133): paragraph or character style name; applied first, may bring ws styles, drop caps hack | :1609-1630; DRAW via the style sheet | PARTIAL: NamedStyle on run, apply FA/FwFieldControls.cs:342-429 | PARTIAL: carried as CharStyle/NamedStyle numbers, no stylesheet resolution (TE/M/PropertyBag.cs `TextPropertyNumbers`) | L |
| ktptCharStyle (2), ktptParaStyle (3): old style carriers | ignored (:1642 default); CARRY | NO | PARTIAL (constants only) | S |
| ktptSpellCheck (26) normal/doNotCheck/forceCheck | :1547; BEHAVIOUR: `SpellCheckMethod::IsRunSpellChecked` V/VwTextBoxes.cpp:8552-8557 | NO | YES: normal, do-not-check, force-check, from the run, its character style and the paragraph style (`SpellScanner.ModeOf`, ADR 0020) | S |
| ktptEditable (155) NotEditable/IsEditable/SemiEditable (type-ahead) | :1541; BEHAVIOUR: restricts editing, selection, spell check; VwEnv.cpp:2207, VwSelection.cpp:994 | PARTIAL: row-level read-only only | PARTIAL: per-run and field-level; semi-editable takes typing but not formatting; no spell-check clause (`DocumentEditor.AllowsEditing`, `CanEditRange` and `CanFormatSelection` in src/SIL.TextEditing/DocumentEditor.Editability.cs; `Run.Editable` in src/SIL.TextEditing/Run.cs) | L |
| ktptTags (5) overlay tag GUID list (8 chars per GUID) | :1606; used by overlays V/VwOverlay.cpp, V/VwTxtSrc.cpp:414,507, V/VwTextBoxes.cpp:8853 (`FindGuidDiffs`), search V/VwPattern.cpp:1029,1068; DRAW tag marks UNVERIFIED who in FLEx uses overlays (SRS/SimpleRootSite.cs and PrintRootSite.cs only plumb IVwOverlay) | NO | NO (carried) | M |
| ktptObjData (6) string: see group 7 | handled in the text source, not the property store (:1642 comment) | PARTIAL | PARTIAL | see 7 |
| ktptCustomBullet (7) | reader only (r5); not in the store; CARRY | NO | NO | S |
| ktptFieldName (9998), ktptMarkItem (9999) fake export props | CARRY; MarkItem is < 10000 so it would hit the int default E_NOTIMPL, UNVERIFIED | NO | NO | S |
| ktptDirectionDepth (129) read-only, computed by the engine | ignored by ApplyTtp (:2195); computed V/VwTextBoxes.cpp:1887 | NO | N/A (derived from layout) | S |
| ktptRelLineHeight (160), ktptSetRowDefaults (159), ktptTableRule (161), ktptCellBorderWidth/Spacing/Padding (152-154) | store cases :1496-1509; table/VC use only (`V/VwTableBox.cpp`), fake props ignored by ApplyTtp; CARRY for a text editor | NO | NO (carried) | S |

### 1b. Paragraph-level props (all are drawn or acted on by paragraph and pile boxes)

| Feature | Legacy source and behaviour | FA | TE | Size |
|---|---|---|---|---|
| ktptAlign (17): Leading, Left, Center, Right, Trailing, Justify (not on last line) | :1185; V/VwTextBoxes.cpp:3252-3275; enum KRN:653-663 | NO | YES: leading, left, centre, right, trailing; justify stretches every line but the last (ParagraphLayout Justification); a line that wraps is aligned by its text, the spaces it ends with hang outside it even when they do not fit the text area (`ParagraphLayout.HangingShift`) | M |
| ktptFirstIndent (18) negative = hanging | :1460; V/VwTextBoxes.cpp:1252,7136; negative handled as pad V/VwTextBoxes.cpp:9394 | NO | NO (Indent=>0, `ParagraphProperties`) | M |
| ktptLeadingIndent / ktptMarginLeading (19) | :1417; V/VwSimpleBoxes.cpp:679,1087,1165 | NO | NO | M |
| ktptTrailingIndent / ktptMarginTrailing (20) | :1421 | NO | NO | M |
| ktptSpaceBefore = ktptMswMarginTop (21): "only MswMarginTop used in paragraph dialogs" KRN comment | :1406 (MSW margin-collapse semantics UNVERIFIED) | NO | NO | M |
| ktptSpaceAfter = ktptMarginBottom (22) | :1413 | NO | NO | M |
| ktptMarginTop (50) | :1409 | NO | NO | S |
| ktptLineHeight (24) exact/at-least/relative; ktpvRelative uses 1.4 x ws font size | :1463-1495; V/VwTextBoxes.cpp:1374,5447,6195 | NO | YES: slot height per rule; under an exact height the baseline keeps one share of the slot on every line, the ascent share (a whole per cent) of the font most of the first line's characters are in, as in FieldWorks, so taller text overflows both ways (ParagraphLayout.LineInfo.TextY, ExactFraction) | M |
| ktptParaColor (25): paragraph background, treated as BackColor | :1368 ("kludge"); fill V/VwTextBoxes.cpp (box background) | NO | NO | S |
| ktptRightToLeft (128) paragraph direction | :1357; V/VwTextBoxes (para RightToLeft()) | PARTIAL: box FlowDirection from ws (FA/FwFieldControls.cs:~790) | YES: Paragraph.Rtl (`ParagraphLayout.Build`) | S |
| ktptPadLeading/Trailing/Top/Bottom (134-137) | :1425-1434; V/VwTextBoxes.cpp:9394 | NO | NO | S |
| ktptBorderTop/Bottom/Leading/Trailing (138-141) + ktptBorderColor (142) | :1437-1446,1375; drawn V/VwSimpleBoxes.cpp:743,1087, V/VwTextBoxes.cpp:9766 | NO | NO | M |
| ktptBulNumScheme (143): none, Arabic(10), RomanUpper, RomanLower, LetterUpper, LetterLower, Arabic01, bullets 100+ (25 glyphs, font "Quivira") | :1449; `GetBulNumString` V/VwTextBoxes.cpp:6604-6770; glyph table :6373; sequence `ParaNumber` :6380-6470; drawn :975,7105-7122; KRN:1273-1286 | NO | YES: label drawn at the first indent; a label wider than the line takes a line of its own, as tall as the label (the line height rule does not apply to it), as in FieldWorks, and otherwise stays beside the text, which wraps there (ParagraphLayout.PlaceFirstLine) | L |
| ktptBulNumStartAt (144) | :1455 (VwCacheDa.cpp:1376 resets) | NO | NO | S |
| ktptBulNumTxtBef/Aft (145,146) | :1630-1635; text around the number (:6760) | NO | NO | S |
| ktptBulNumFontInfo (147) packed font props for the number | :1636; parsed :6700-6745 | NO | NO | M |
| ktptKeepWithNext (148), ktptKeepTogether (149), ktptWidowOrphanControl (162) | :1514-1528; CARRY for on-screen; page layout and print only (V/VwLayoutStream.cpp:1252, VwSimpleBoxes.cpp:3016) | NO | NO (carry OK) | S |
| ktptHyphenate (150) | :1529; stored, NOT implemented (V/VwTextBoxes.cpp:1368) | NO | NO (carry OK) | S |
| ktptMaxLines (151) | :1534; DRAW truncates lines V/VwSimpleBoxes.cpp:2419,2495, V/VwInvertedViews.cpp:312; who sets it UNVERIFIED | NO | YES: lines past the limit are left out and the last visible line ends with "..." (ParagraphLayout.EllipsisRect) | S |
| ktptTabDef (23), ktptTabList (4) | NOT handled: int default throws E_NOTIMPL; no tab-stop drawing found; CARRY | NO | NO | S |

Unit and variation note: lengths are mpt (ktpvMilliPoint); colours are ktpvDefault ints (BGR, kclrTransparent for none); toggles are Enum with kttvOff=0, kttvForceOn=1, kttvInvert=2 (KRN:588-590).

---

## 2. Paragraph formatting (editing, not just drawing)

| Feature | Legacy source | FA | TE | Size |
|---|---|---|---|---|
| Apply paragraph style to selected paragraphs; clears all explicit paragraph formatting | SRS/EditingHelper.cs:2212-2268 `ApplyParagraphStyle`; RS/RootSiteEditingHelper.cs:664 `ChangeParagraphStyle` | PARTIAL: per-paragraph style picker FA/FwStructuredTextField.cs:305-360 | PARTIAL: Paragraph.Style carried, no apply command | M |
| Read paragraph props for selection (style name, hard props, mixed) | SRS/EditingHelper.cs:1608 `GetStyleNameFromSelection`, 1644, 1732-1990 (`GetParagraphProps`, `IsParagraphProps`), V/VwSelection.cpp:817 `GetHardAndSoftParaProps` | PARTIAL | NO | M |
| Paragraph-level tag test (which flids hold paragraph props) | SRS/EditingHelper.cs:2018 `IsParagraphLevelTag`, RS/RootSiteEditingHelper.cs:695 | NO | NO | S |
| Special paragraph types (e.g. picture caption, footnote paragraphs) | SRS/EditingHelper.cs:1786, RS/RootSiteEditingHelper.cs:752 `HandleSpecialParagraphType` | NO | NO | S |
| Hard line break within a paragraph (Shift+Enter inserts U+2028) | RS/RootSiteEditingHelper.cs:782-800, 821-840; V/VwRootBox.cpp:~1712 (`kecEnterKey`, `kfssShift`) | NO | YES: Shift+Enter inserts U+2028, both modes (`DocumentEditor.InsertLineBreak`; the Shift+Enter case in `RichTextField.HandleKey`) See ADR 0024. | S |
| Enter splits paragraph (style and direction carried to both halves) | V/VwRootBox.cpp (kecEnterKey kfssNone); StText paragraph split | PARTIAL: inserts new paragraph AFTER, not a split (FA/FwStructuredTextField.cs:137-165, with Review note) | YES: TE/M/DocumentEditor.cs `Enter` | S |
| Backspace/Delete joins paragraphs; paragraph identity rules | V/VwSelection.cpp OnTyping 4951; liblcm StTxtPara | PARTIAL: Backspace at empty para deletes it (FA/FwStructuredTextField.cs:155-165) | YES: DocumentEditor.cs `Backspace`, `DeleteForward` + `ReplaceRange` (identity rules in tests) | S |
| Add/delete paragraph (structural) | StText slice | YES: FA/FwStructuredTextField.cs:174-187 | PARTIAL: Enter/join only | S |
| Bullets/numbering editing (choose scheme, start-at, text before/after) | Styles dialog (FwCoreDlgs, UNVERIFIED lines); engine V/VwTextBoxes.cpp:6604 | NO | NO | L |
| Remove hard paragraph formatting redundant with the style | SRS/EditingHelper.cs:2539 `RemoveRedundantHardFormatting` | NO | NO | M |
| Clipboard paste keeps paragraph breaks and paragraph styles | SRS/EditingHelper.cs:3460-3600, 3613 `ChangeStyleForPaste` | PARTIAL | PARTIAL: multi-paragraph fragment (`DocumentEditor.Paste`, `ReplaceRange`); styles carried by a rich form that keeps paragraphs (FieldWorks' TsString form pastes one paragraph, as FieldWorks does) | M |

---

## 3. Caret, selection and navigation (keyboard and mouse)

Dispatch: key down -> `EditingHelper.OnKeyDown` (SRS/EditingHelper.cs:648) -> `CallOnExtendedKey` (:1117) -> `VwRootBox::OnExtendedKey` (V/VwRootBox.cpp:1409-1740) -> `VwTextSelection::*Arrow/*Key` (V/VwSelection.cpp:7923-10250). Return value S_FALSE means "move to next/previous field or record".

| Feature | Legacy source | FA | TE | Size |
|---|---|---|---|---|
| Left/Right by character, logical by default, RTL-aware | V/VwSelection.cpp:7923 `LeftArrow`, :7945 `RightArrow`, :8013 `PhysicalArrow`, :8244 `BackwardArrow` | PARTIAL: own bidi nav FA/DetailBidirectionalTextNavigation (used FwFieldControls.cs:156-225) | PARTIAL: visual movement instead (`RichTextField.MoveVisualLeftRight`) | M |
| "Clumping": arrow skips a whole cluster/ligature unless registry `ArrowByCharacter`=True or env `FW_ARROWBYCHARACTER` | V/VwRootBox.cpp:1384-1403, used :1517-1560 | NO (TextBox default) | PARTIAL: shaper caret stops (`ParagraphLayout.VisualStops`; on a right-to-left paragraph's line of several runs, grapheme boundaries measured on each run's glyphs, docs/reference/rendering.md), no override switch | S |
| F7 / F8 = strict left/right arrow by character (physical) | V/VwRootBox.cpp (kecF7/kecF8 cases); SRS/EditingHelper.cs:658-659 | NO | NO | S |
| Shift+arrows extend selection | V/VwSelection.cpp:9241 `ShiftUpArrow`, ShiftLeft/Right | YES (TextBox + FA nav 191-208) | YES (`RichTextField.HandleKey`) | S |
| Ctrl+Left/Right by word (logical) | V/VwSelection.cpp:9492 `ControlLeftArrow`; word boundary :11199 `FindWordBoundary` (ICU per ws) | NO (Ctrl keys skipped, FwFieldControls.cs:160-162) | PARTIAL: `WordBreaks`, a simple rule that is the host's per writing system (`IWordRules`, `RichTextField.WordRules`; the spell checker's rule), no dictionary (docs/reference/editing.md, ADR 0026) | M |
| Ctrl+Up / Ctrl+Down = previous / next paragraph start | V/VwSelection.cpp:9706 `ControlUpArrow`, :9817 `ControlDownArrow` | NO | NO | S |
| Up/Down keep a preferred X (`m_xdPos`) across lines | V/VwSelection.cpp:8487 `UpArrow`, VwRootBox.cpp:1431; `UpOneLine`/`DownOneLine` | PARTIAL (TextBox) | YES (RichTextField MoveVertical) | S |
| Home/End: visual line; Ctrl+Home/End: start/end of view; logical in RTL | V/VwSelection.cpp:8777 `HomeKey`, :9908 `ControlEndKey`, :9969 `ControlHomeKey`; EditingHelper.cs:1142-1190 scrolls | PARTIAL | YES (`RichTextField.HandleKey`, Home and End; End on a wrapped line puts the caret after the line's trailing space, as in FieldWorks, and draws it one space width per hanging space past the text, held inside the control; where the control gives no room the carets across two or more spaces are held a default 2 px apart (`ParagraphLayout.MinCaretStep`) so each arrow step shows, except where the text itself ends at the edge, which a host margin cures; a click among those spaces goes to the nearest caret as drawn) but RTL Home/End logical | S |
| PageUp / PageDown (plain, Shift, Ctrl, Ctrl+Shift) with selection kept at same X | V/VwSelection.cpp:347-... `PageUpKey`, `DoPageUpDown`, `MoveIpVerticalByAmount`, `DoCtrlPageUpDown`; V/VwRootBox.cpp:~1435-1470 | NO | YES: PageUp and PageDown, plain or Shift, by the field's visible height (Ctrl and Ctrl+Shift go to the corners of what shows, as FieldWorks does), keeping the caret's place along the line; the host is asked to scroll by the distance moved (`RichTextField.MovePage`, ADR 0025) | M |
| Ctrl+A select all | SRS/SimpleRootSite.cs:3677 `OnEditSelectAll`; EditingHelper.cs:3782; CFG:135 CmdSelectAll | YES (TextBox) | YES (`RichTextField.HandleKey`, Ctrl+A) | S |
| Tab / Shift+Tab: to next/previous editable place in the root box; S_FALSE = leave the view (next slice/record); Ctrl+Tab = last editable; Ctrl+Shift+Tab = first | V/VwRootBox.cpp:~1672-1705; V/VwSelection.cpp:10213 `TabKey`, `ShiftTabKey`; `FindEditablePlaceOnLine`; SRS `IsInputKey` 4907 (`AcceptsTab`) | PARTIAL: one tab stop per field, group logic FA/DetailValueStopGroup.cs:124-149 | YES: next/previous editable place, Ctrl+Tab last, Ctrl+Shift+Tab first, leave only when none (`DocumentEditor.TryGetEditablePlace` in src/SIL.TextEditing/DocumentEditor.Editability.cs; `RichTextField.HandleKey` in src/SIL.TextEditing.Avalonia/RichTextField.cs) | M |
| Arrow past the last position of a field leaves the field (S_FALSE: previous/next field) | V/VwRootBox.cpp:~1517-1700 ("Need to move to the previous/next field") | YES: Left/Right at text edges, Up/Down move between rows (FA/DetailValueStopGroup.cs:136-149) | YES: LeaveRequested at the edges (`RichTextField.HandleKey` in src/SIL.TextEditing.Avalonia/RichTextField.cs) | S |
| Caret placement across non-editable text: IP moves to nearest editable place | V/VwSelection.cpp:12669 `FindClosestEditableIP`; V/VwRootBox.cpp MouseUp ~2370-2400 | NO | YES: caret moves to the nearest editable place within the paragraph (`DocumentEditor.NearestEditable` in src/SIL.TextEditing/DocumentEditor.Editability.cs) | M |
| Mouse click places IP (associates with previous/next char, bidi/line-end affinity) | V/VwRootBox.cpp:2108 `MouseDown`, :1771 `MakeSelAt`; SRS/SimpleRootSite.cs:4449 `OnMouseDown`, 5407 `CallMouseDown` | YES (TextBox) | YES (affinity: ParagraphLayout HitTestDetailed 350-373; a right-to-left paragraph's line of several runs is read from its runs, because Avalonia's hit testing is wrong there on 11.3 and 12, docs/reference/avalonia-versions.md; a click beyond a line that ends in a hard break goes to the line's visual end in the paragraph's direction) | M |
| Shift+click extends | V/VwRootBox.cpp:2274 `MouseDownExtended`; SimpleRootSite.cs:4545 | YES | YES (`RichTextField.OnPointerPressed`) | S |
| Drag selects; auto-scroll when dragging above/below the view | V/VwRootBox.cpp:2253 `MouseMoveDrag`; SRS/SimpleRootSite.cs:5482-5540 (10 px per event, 30 px zone) | PARTIAL (TextBox) | YES: drag selects; past the visible edge the selection goes only just beyond it and the field raises `ScrollRequested` (10 px a move near the edge, 30 beyond, no timer); the host scrolls (`RichTextField.DragPoint`, ADR 0025) | M |
| Double-click selects word; word growth `GrowToWord` and `ExpandToWord` | V/VwRootBox.cpp:2193 `MouseDblClk`; V/VwSelection.cpp:7066, 12366 | YES (TextBox) | YES (`RichTextField.OnPointerPressed`) | S |
| Triple-click selects paragraph | NOT in legacy (no triple-click handler in V/VwRootBox.cpp, `kmeTriple` grep empty) | YES (TextBox: line) | YES (`RichTextField.OnPointerPressed`) -- extra | S |
| Caret blink (`FlashInsertionPoint`, timer) | V/VwRootBox.cpp:1753; SRS/SimpleRootSite.cs:3702 `OnTimer` | YES (TextBox) | NO (static caret, `RichTextField.Render`) | S |
| Insertion point shape: bidi split caret (primary + secondary), I-beam cursor rotated for vertical text | V/lib/UniscribeSegment.cpp:1946 `DrawInsertionPoint`, 2001 `PositionsOfIP`; EditingHelper.cs:1460 `SetCursor`, SRS `IBeamCursor` | PARTIAL | PARTIAL: one caret, bidi caret stops (docs/reference/editing.md) | M |
| Selection shading (range drawn, not in-segment inversion) incl. disjoint bidi ranges | V/lib/UniscribeSegment.cpp:2159 `DrawRange`, 2192 `PositionOfRange` | YES | YES (`ParagraphLayout.SelectionRects`, drawn by `RichTextField.Render`) | M |
| Selection state when the view loses focus (disabled/out-of-focus look) | V/VwRootBox.cpp:2474 `get_SelectionState`, 2419 `Activate`; SRS/SimpleRootSite.cs:5693 `GetNonFocusedSelectionState`, 6340 `Activate` | YES (TextBox) | NO | S |
| Scroll selection into view (options, one line of margin, warm-up of lazy boxes) | SRS/SimpleRootSite.cs:1612 `ScrollSelectionIntoView`, 3173-3510 `MakeSelectionVisible`; lazy boxes V/VwLazyBox.cpp | YES (ScrollViewer) | PARTIAL: the field raises `ScrollRequested` after caret moves, with a line of margin, and a scroll viewer round the field answers by default; no warm-up of lazy boxes and no scrolling inside the field (the host scrolls the view, ADR 0021) | L |
| Mouse wheel, scrollbars, Zoom property | SRS/SimpleRootSite.cs:950 `Zoom`, 5118-5300 scroll | YES | NO | M |
| Vertical (rotated) orientation | SRS/OrientationManager.cs (340 lines); used by which views UNVERIFIED | NO | NO | L |
| Selection types beyond text: picture selection (arrows, Home/End), integer/checkbox boxes | V/VwSelection.cpp:13613 `VwPictureSelection::Draw`, 13872+ arrow overrides; V/VwSimpleBoxes.h:1341 `VwPictureBox` | NO | PARTIAL: object run selectable as a character | M |
| Hit-testing of lazy / not-yet-laid-out content | V/VwLazyBox.cpp (1500 lines) | N/A | N/A (whole doc laid out; perf risk for 200+ paragraphs UNVERIFIED) | L |
| Selection query API for hosts: IsRange, TextSelInfo, AllTextSelInfo, EndPoint, CLevels, Location | V/VwSelection.cpp:161-335, 2341; SRS/TextSelInfo.cs, SelectionHelper.cs (2109 lines), SelPositionInfo.cs, SelectionRestorer.cs | N/A | PARTIAL: Editor.Caret/Anchor/SelectionWsSet (`DocumentEditor.Caret`, `Anchor`, `SelectionWsSet`) | M |
| Restore selection after a data change / undo / redo | SRS/SelectionRestorer.cs, SimpleRootSite.cs:767 `RequestSelectionAtEndOfUow`, 2592; RS/RequestSelectionHelper.cs | N/A | PARTIAL: host Apply/ExternalReplaceParagraph (`DocumentEditor.Apply`, `ExternalReplaceParagraph`) | M |

---

## 4. Editing commands: typing, delete, cut/copy/paste formats, drag and drop, undo grouping

| Feature | Legacy source | FA | TE | Size |
|---|---|---|---|---|
| Typing inserts at IP with the properties of the neighbouring run (or typing props set by ws/style change) | V/VwSelection.cpp:4951 `OnTyping`, :1874 `SetTypingProps`, :7891 `CleanPropertiesForTyping`, :3535 `GetInsertionProps` | YES (TextBox + run replay FA/DetailModel `ApplyPlainTextEdit`) | YES (`DocumentEditor.TypeText`, `InheritProps`) | S |
| Typing over a range selection: delete then insert (split into two UOWs then merged) | SRS/EditingHelper.cs:1001-1050 `OnCharAux`, :1042 `MergeLastTwoUnitsOfWork`, RS/RootSiteEditingHelper.cs:369 | YES | YES (Delete then InsertOne) | S |
| Type-ahead collection (batching keystrokes), disabled for Keyman (FWR-2205) | SRS/EditingHelper.cs:828 `KeepCollectingInput`, 843-970 `CollectTypedInput` (surrogate pair validation) | N/A | N/A (OnTextInput) | S |
| Ignored keys / control characters | SRS/EditingHelper.cs:747 `IsIgnoredKey`; SimpleRootSite.cs:4776 | N/A | PARTIAL (OnTextInput ignores empty) | S |
| NFD normalisation of typed input | V/VwSelection.cpp:3660-3679 | NO | NO | M |
| Backspace: one char, surrogate pair aware; deletes picture ORC props correctly | V/VwSelection.cpp:3925-4010 | YES (TextBox) | PARTIAL: whole cluster (`DocumentEditor.Backspace`) | S |
| Delete (forward): char plus diacritics | V/VwSelection.cpp:4773-4810 | YES (TextBox) | YES (cluster, `DocumentEditor.DeleteForward`) | S |
| Complex delete across properties/paragraphs (`DeleteRangeIfComplex`, `RuinedByDeleting`) | V/VwRootBox.cpp:1323; V/VwSelection.cpp:7585, 7669 `DeleteRangeAndPrepareToInsert`; SRS/EditingHelper.cs:974 | PARTIAL | YES (ReplaceRange 211) | M |
| Deleting an embedded object that owns data: confirm | SRS/SimpleRootSite.cs:1529 `OnProblemDeletion` (VwDelProbResponse) | NO | NO | M |
| Selection must lie in ONE editable prop (cut/paste/delete disabled otherwise) | SRS/EditingHelper.cs:3302,3336 `IsSelectionInOneEditableProp`, 3351 `IsSelectionInOneFormattableProp`; CanDelete 3624, CanCut 3646, CanCopy 3665, CanEdit 3690/3707, CanPaste 3763 | PARTIAL | NO | M |
| Cut / Copy to clipboard as TsString (private format), Serializable, and NFC Unicode text | SRS/EditingHelper.cs:3126-3185 (`CopySelection`, `SetTsStringOnClipboard`; formats TsStringWrapper.TsStringFormat, DataFormats.Serializable, UnicodeText), 3253 `CutSelection`; retry on clipboard failure :3158-3172 (LT-14822) | PARTIAL: IFwClipboard (text, RichXml, RichText) FA/Seams/IFwClipboard.cs; Ctrl+C/V handler FwFieldControls.cs:673-711 | PARTIAL: own format `SIL.TextEditing.Runs.v1` + plain text (TE/M/Interchange.cs, ClipboardCompat.cs); no HTML/RTF; no LCModel TsString format | L |
| Copy of an ORC / footnote selection uses a text representation (`TextRepOfObj`), paste re-creates object (`MakeObjFromText`, kodtEmbeddedObjectData) | RS/RootSiteEditingHelper.cs:189,213; SRS/SimpleRootSite.cs:1596 `get_MakeObjFromText`, :5356 `GetTsStringForClipboard`; KRN kodtEmbeddedObjectData | NO | PARTIAL: object runs round-trip in own format | M |
| Paste: TsString first; fall back to plain text; format stripped if the target can't format | SRS/EditingHelper.cs:3412-3600 (`PasteClipboard`, `PasteCore`, `GetTextFromClipboard` 3570 `fCanFormat`) | PARTIAL: whole-field rich override only when whole text selected (FwFieldControls.cs:701-704) | YES: registered forms in a host-set order, TsString, FieldWorks Markdown, plain text by default (`RichTextField.PasteAsync`, `ClipboardForms.PasteOrder`, ADR 0029); the field has no "cannot format" case yet | M |
| Paste with writing system policy: preserve, use destination ws, cancel; replace "external" ws | SRS/EditingHelper.cs:3187-3216, 3508-3570, 3822 `DeterminePasteWs` (PasteStatus 261) | NO | PARTIAL: a writing system the target lacks takes the selection's and `WritingSystemsMapped` tells the host (ADR 0029); no preserve or cancel choice | M |
| Pre-paste hook and validity check | SRS/SimpleRootSite.cs:2489 `PrePasteProcessing`; EditingHelper.cs:3601 `ValidToReplaceSelWithTss`; FW/FwEditingHelper.cs:476 | NO | NO | S |
| Paste hyperlink (URL or file name on clipboard becomes a link) | RS/RootSiteEditingHelper.cs:986 `CanPasteUrl`, 1006 `PasteUrl`; CFG:132 CmdPasteHyperlink | NO | NO | M |
| Drag and drop of selected text within/between fields | NOT in legacy (finding 6) | NO | NO | M (new feature) |
| Undo / redo (host-owned; one UOW per typed key, per delete, per paste) | RS/UndoTaskHelper.cs; RS/RootSiteEditingHelper.cs:821-876 (labels "Undo typing X", "CR", "Hard line break", "Delete"); RS/UndoActions.cs; CFG:121-122 (Ctrl+Z, Ctrl+Y) | PARTIAL: edits staged to host edit session (IDetailEditContext) | PARTIAL: host-owned; stand-in `SimpleUndoStack` (in DocumentEditor.cs); `UndoRequested`/`RedoRequested` events on `RichTextField` | M |
| Undo grouping rule: each OnKeyPress is its own task (no time-based coalescing found); typed-ahead run is one task; UNVERIFIED whether LCModel merges consecutive typing | SRS/EditingHelper.cs:1042; RS/RootSiteEditingHelper.cs:821-876 | N/A | N/A (TE reports labelled EditDeltas; host groups) Typing grouping: ADR 0023; delete and paste steps: ADR 0024. | S |
| Selection after undo/redo restored | RS (see group 3) | N/A | PARTIAL | M |
| Remove character formatting (Ctrl+Space) incl. optional style removal | SRS/EditingHelper.cs:698-703, 2268-2433 `RemoveCharFormatting`, RS/RootSiteEditingHelper.cs:549 | NO | NO | M |
| Insert special character (character map dialog) | CFG:130 CmdShowCharMap ("Special _character...") | NO | NO | S (host dialog; editor needs InsertText API only) |

---

## 5. Writing systems and keyboards: switching, Keyman, IME

| Feature | Legacy source | FA | TE | Size |
|---|---|---|---|---|
| Activate the keyboard of the ws at the selection when the selection changes | SRS/EditingHelper.cs:2877 `SetKeyboardForWs(ws)`, 2915, 2932-2965 `SetKeyboardForSelection`; ws from `SelectionHelper.GetFirstWsOfSelection` | YES: `WireWritingSystemKeyboard` FA/FwFieldControls.cs:715,880 (on focus) | PARTIAL: ActiveWs + `ActiveWsChanged` event (`DocumentEditor.ActiveWs`); host must switch keyboard | M |
| On keyboard (input language) change, switch the ws of the IP or of the range (may re-tag the range) | SRS/SimpleRootSite.cs:115 `OnInputLanguageChanged`, 5588 `HandleKeyboardChange`, 5642 `GetPossibleWritingSystemsToSelectByInputLanguage`, 6767 `GetWSForInputMethod`; `m_wsPending` cleared by nav keys (EditingHelper.cs:1126) | NO | PARTIAL: `SetActiveWs` override (`DocumentEditor.SetActiveWs`) | M |
| On focus lost switch back to the default (UI) keyboard unless focus moves to another view; 0.5 s guard for keyboard-switch focus flicker (LT-19289) | SRS/EditingHelper.cs:2966, 3007-3062 `LostFocus`, `ShouldRestoreKeyboardSwitchingTo`; 3063 `GotFocus` | NO | NO | S |
| Keyman: type-ahead disabled; keyboard activation per ws | SRS/EditingHelper.cs:843-870 (FWR-2205); libpalaso `Keyboard.Controller` | PARTIAL: "ordinary IME/Keyman composition" claim FA/DetailModel.cs:442 (UNVERIFIED) | UNVERIFIED (manual test 4 in docs/manual-testing.md; docs/reference/limitations.md: "real IME and Keyman interaction are untested") | M |
| IBus on Linux | SRS/IbusRootSiteEventHandler.cs (653 lines) | NO | NO | M (Linux only; may be out of scope) |
| TSF composition (Windows): ITextStoreACP implementation: text, formatted text, selection, attrs, text extent, ACP from point, insert at selection, compositions start/update/end, `TerminateAllCompositions` | V/VwTextStore.cpp:207-2810 (e.g. GetText 698, SetText 938, GetFormattedText 994, GetTextExt 1228, InsertTextAtSelection 1373, OnSelectionChange 1669, OnStartComposition 2467, TerminateAllCompositions 2008) | N/A (Avalonia TextInputMethodClient via TextBox) | PARTIAL: `InputMethodClient` (nested in RichTextField.cs): preedit shown inline, not in model, surrounding text, selection | L |
| Composition display attributes (underline kinds, colours per clause) | V/VwTextStore.cpp:2260-2420 `DoDisplayAttrs` (maps TF_LS_* to kunt*, falls back to swapped colours) | N/A | PARTIAL: preedit always underlined (`ParagraphLayout.MakeProperties`), in the character formatting the committed text will get (`DocumentEditor.TypingProperties`) | M |
| WM_IME_CHAR handling and NFD-IME awareness | SRS/SimpleRootSite.cs:~6856; V/VwTextStore.cpp:2744 | N/A | NO | S |
| IME candidate window placement (text extent) | V/VwTextStore.cpp:1228 `GetTextExt`, :1304 `GetScreenExt` | N/A | PARTIAL: `InputMethodClient.NotifyCaret` (in RichTextField.cs) | S |
| Reconversion / surrounding text | V/VwTextStore.cpp GetText 698, `GetEmbedded` 1009 | N/A | PARTIAL: SurroundingText = current paragraph only | M |
| Writing-system selector (toolbar combo) reflects selection; applying a ws to a range or IP | SRS/EditingHelper.cs:413 `GetWsList`, 439 `GetWsListCurrentFirst`, 467 `ApplyWritingSystem`, 516/RS:578 `ChangeWritingSystem`, 2812,2838; FW/MainWindowDelegate.cs:529; CFG:123 menu "Writing System" | PARTIAL: ws retag of selection FA/FwFieldControls.cs:442-530 | YES: SetActiveWs, SelectionWsSet, `RichTextField.SelectionWsState` event; retag is a sample feature (`PlaygroundView.SetWritingSystem`), not the library | M |
| Ws-specific attributes: RTL script, font, size, features, spell id, ICU locale, legacy mapping | V/VwPropertyStore.cpp:1380; V/VwTextBoxes.cpp:8621 `get_SpellCheckingId`; V/lib/LgLineBreaker.cpp:77-115 (ICU line breaks per locale) | PARTIAL | PARTIAL: font, size, rtl, features (WritingSystemRegistry.cs:9); no spell/locale | M |
| Mixed-ws word handling (a "word" spanning writing systems) | V/VwTextBoxes.cpp:8586-8600 (type-2 problem, blue squiggle) | NO | NO | S |

---

## 6. Styles: apply, the styles combo, per-writing-system overrides

| Feature | Legacy source | FA | TE | Size |
|---|---|---|---|---|
| Apply a style by name: character style to the range, paragraph style to paragraphs | SRS/EditingHelper.cs:2121 `ApplyStyle(name)`, 2150 `ApplyStyle(name, sel, ...)` | PARTIAL: char style FA/FwFieldControls.cs:342-429; para style FwStructuredTextField.cs:305-360 | NO (library has no style API; sample only) | M |
| Toolbar style combos: paragraph combo + character combo, filtered by context, update with selection | FW/MainWindowDelegate.cs:456 `InitStyleComboBox`, 481 `UpdateStyleComboBoxValue`, 553 `RefreshParaStyleComboBoxList`, 586 `RefreshCharStyleComboBoxList`; FwCoreDlgs/.../StyleComboListHelper.cs | NO (menu picker only) | NO | L |
| Style name of selection (mixed handling) | SRS/EditingHelper.cs:1608-1730 | NO | NO | M |
| Format > Styles... dialog; Format > Apply Style... dialog | CFG:122-123; FW/MainWindowDelegate.cs:622 `ShowStylesDialog`, 682 `ShowApplyStyleDialog`; xWorks/FwXWindow.cs:1817 `OnFormatApplyStyle` | NO | N/A (host dialog) | host |
| Style sheet drives all draw props: named style + parent chain + ws overrides (`<default props>`), hard formatting on top | V/VwPropertyStore.cpp:2120-2230; AppCore/FwStyledText.cpp:286-420; liblcm StStyle (r5 section 4) | NO | NO (no stylesheet interface) | L |
| Per-writing-system style overrides (ktptWsStyle) | V/VwPropertyStore.cpp:1580; AppCore/FwStyledText.cpp `ZapWsStyle`; Framework/StylesXmlAccessor.cs:262,277,1128; EditingHelper.cs:2724 | NO | NO | L |
| Resolve a style's font for a ws (e.g. `GetFontForNormalStyle`) | SRS/EditingHelper.cs:1526 | PARTIAL: font map FA/DetailRichTextChrome.cs | PARTIAL: ws font only | M |
| Hyperlink style auto-applied to links | RS/RootSiteEditingHelper.cs:892 `ConvertSelToLink`; FW/FwEditingHelper.cs:251 `AddHyperlink` | PARTIAL: ApplyHyperlink FA/DetailModel.cs:771 | NO | M |
| Special semantics character styles ("Hyperlink" etc.) | SRS/EditingHelper.cs:2304 `SpecialSemanticsCharacterStyle` | NO | NO | S |
| Style change refreshes display (stylesheet change -> relayout) | V/VwRootBox.cpp:2634 `OnStylesheetChange` | NO | NO (`RichTextField.InvalidateAllLayouts` exists) | M |
| Invert toggles in styles (bold="invert") | V/VwPropertyStore.cpp:1196-1240 | NO | NO | M |

---

## 7. Embedded objects: pictures, links, footnote-like ORCs, other kodt types

U+FFFC (ORC) carries `ktptObjData`: first char = kodt type, rest = payload (KRN:325-387). Legacy handling: text source `V/VwTxtSrc.cpp:834-870`, paragraph builder `V/VwTextBoxes.cpp:2360-2450`.

| Feature | Legacy source | FA | TE | Size |
|---|---|---|---|---|
| kodtPictEvenHot (1)/kodtPictOddHot (2): inline picture bytes (OleLoadPicture), clickable; GUID prefix of 8 chars | V/VwTextBoxes.cpp:2377-2450 `HandlePicture`; V/VwSimpleBoxes.cpp:3466 `VwPictureBox::DoHotLink` (-> `DoHotLinkAction` :3514) | PARTIAL: OrcKind.Picture render-only FA/DetailModel.cs:166 | PARTIAL: image-by-key ObjectRun (`ParagraphSource.GetTextRun`); bidi mis-order workaround `ObjectAsGlyph` | M |
| kodtNameGuidHot (3): text = name of the referenced object (via `GetStrForGuid` from the view constructor), hot | V/VwTxtSrc.cpp:834-870 (ORC expanded to text); V/VwTextBoxes.cpp:275-276; hot link :528-612 | NO (read-only) | NO (ORC is a single glyph; no text expansion hook) | L |
| kodtOwnNameGuidHot (5): as 3 but text owns the object (deleted with the ORC, copied with it); used for footnote markers | V/VwTxtSrc.cpp:1075; V/VwTextBoxes.cpp:2385; RS/RootSiteEditingHelper.cs:416-523 (footnote anchor icon select, `HandleFootnoteAnchorIconSelected`, custom cursor :397 `SetCustomCursor`) | PARTIAL: OrcKind.Footnote, deletable FA/DetailModel.cs:168; edit deferred | NO | L |
| kodtExternalPathName (4): external file link (relative to LinkedFilesRootDir); click = ShellExecute | V/VwTextBoxes.cpp:590 (hot); RS/RootSiteEditingHelper.cs:892-1006, FW/FwEditingHelper.cs:251-400 (`AddHyperlink`); RS/FwBaseVc.cs:182 | PARTIAL: insert/edit URL/delete FA/FwFieldControls.cs:543-669 (ExternalLink) | PARTIAL: ORC carried; no activation | M |
| kodtEmbeddedObjectData (6): clipboard-only text form of an owned object | KRN:355-361; RS/RootSiteEditingHelper.cs:189,213 | NO | NO | M |
| kodtContextString (7): generated text (page number, date, title; headers/footers) | KRN:362-377; V/VwRootBox.cpp:2962-3290 (`ProcessHeaderSpecials`, `PrintHeader`); V/VwTxtSrc.cpp:845 | NO | NO (print only; likely out of scope) | S |
| kodtGuidMoveableObjDisp (8): object displayed (by the view constructor, e.g. CmPicture with caption) right after the line holding the ORC; anchor | V/VwTextBoxes.cpp:2380-2384 `GetMoveablePile`; VC `DisplayEmbeddedObject`; V/VwSimpleBoxes.h:1150 `VwMoveablePileBox`; V/VwTxtSrc.cpp:861 | PARTIAL: read as OrcKind.Picture (DetailModel.cs:166, "tag 8") | NO (a picture ORC is inline, not a block below) | L |
| Unknown kodt or empty ObjData: skip the character with a warning | V/VwTextBoxes.cpp:2398-2403 | PARTIAL: OrcKind.Other, whole value goes read-only | PARTIAL: carried | S |
| Insert picture (ICmPicture + caption ORC) | FW/FwEditingHelper.cs:135,402 `InsertPicture`, 453 `InsertPictureOrc`; CFG: CmdInsertPicture (Lexicon area config, UNVERIFIED line) | NO (deferred) | NO | L |
| Insert link to file (dialog), paste hyperlink | RS/RootSiteEditingHelper.cs:968; CFG:134 CmdInsertLinkToFile | PARTIAL (URL prompt) | NO | M |
| Typing next to a link must not extend it; deleting a link boundary | FW/FwEditingHelper.cs:154-250 `OnCharAux` (URL before/after compared) | NO | NO | M |
| Deleting an ORC deletes owned object; copy makes a copy | KRN comments kodtOwnNameGuidHot; SRS/SimpleRootSite.cs:1529 | PARTIAL: ORC delete FA/FwFieldControls.cs:641-669 | NO | M |
| ORC and spell checking/Find (ORC skipped) | V/VwTextBoxes.cpp:8586-8592; V/VwPattern.cpp `RemoveIgnorableRuns` 2063 | N/A | N/A | S |
| Hidden formatting objects: "Invisible Spaces" display (ZWSP shown) | CFG:481; LexText/Interlinear/RawTextPane.cs:192-352 (click inserts invisible space) | NO | NO | S |

---

## 8. Links and hot links: navigation

| Feature | Legacy source | FA | TE | Size |
|---|---|---|---|---|
| Click on hot ORC/text (no range selection) calls view constructor `DoHotLinkAction(objdata)`; only for kodt 3, 4, 5 (strings) and pictures 1, 2 | V/VwRootBox.cpp:2321-2362 `MouseUp` (skips when a range exists); V/VwTextBoxes.cpp:528-612 `VwStringBox::DoHotLink` (ignores clicks outside the box bounds, so click after a link at paragraph end does nothing) | NO | NO | M |
| The link target characters need not be an ORC: any char with ObjData hot type is hot (note in KRN:339-341) | V/VwTextBoxes.cpp:590 | NO | NO | S |
| Hand cursor over hot kodt 1-5; arrow over pictures | SRS/EditingHelper.cs:1578-1600 `GetCursor`, 1460 `SetCursor`; SRS/SimpleRootSite.cs:4218 `OnMouseMoveSetCursor` | NO | NO | S |
| Hyperlink tooltip | NOT implemented (commented code SRS/SimpleRootSite.cs:~4140-4180) | NO | NO | S (new) |
| External file link opens with the default program; relative path vs LinkedFilesRootDir | KRN:344-347; RS/FwBaseVc.cs:182; RS/RootSiteEditingHelper.cs:940 | NO | NO | host |
| Right-click on link must not follow it (right mouse installs IP without click action) | SRS/SimpleRootSite.cs:4520-4535 | N/A | N/A | S |
| In-app link (FwLink) navigation after click is deferred via a posted message; mouse events ignored while pending | SRS/SimpleRootSite.cs:4459-4466 `IsFollowLinkMsgPending`; LocalLinkArgs.cs | NO | NO | M |
| Edit link / change URL; remove link | FW/FwEditingHelper.cs:251; FA/FwFieldControls.cs:579-632 | PARTIAL | NO | M |
| Copy location as hyperlink / Go to entry (host commands) | CFG:128 CmdCopyLocationAsHyperlink, CmdGoToEntry | N/A | N/A | host |
| Selected link text and `GetExternalLinkSel` (finds link style around IP) | SRS/SimpleRootSite.cs:6208 | NO | NO | M |

---

## 9. Spelling: squiggles, suggestions, add-to-dictionary

| Feature | Legacy source | FA | TE | Size |
|---|---|---|---|---|
| Background incremental spell check of paragraphs, word by word | V/VwTextBoxes.cpp:8500-8750 (`SpellCheckMethod`); V/VwRootBox.cpp:3665 `IsSpellCheckComplete`, 3678 `DoSpellCheckStep`, 3637 `MaxParasToScan`; run on idle by the site | NO | YES: a bounded number of words of one paragraph in view per idle step, in document order (`RichTextField.CheckSpellingStep`; `SpellIdlePost`; a checker that is an `IBackgroundSpellChecker` is asked on a worker, answers applied by paragraph stamp); scrolling and edits make paragraphs due, and an edit leaves the marks outside it in place | L |
| Red squiggle for misspelled word (underline overrides via `DispPropOverride` on the text source, no model change) | V/VwTextBoxes.cpp:8650 (kclrRed), 8655 `AddDispPropOverrides`, 8761 squiggle drawing | NO | YES: the layout draws spelling marks with the squiggle drawing, red (`ParagraphLayout.SetSpellingMarks`); results are for one state of the paragraph (`Paragraph.ChangeStamp`) and move with an edit | M |
| Blue squiggle for "type-2" words (contains non-checked runs or several writing systems) | V/VwTextBoxes.cpp:8586-8600 (kclrBlue) | NO | YES: `SpellingProblem.Mixed`, blue, without asking a dictionary | S |
| Dictionary per ws via `SpellCheckingId` (Enchant/Hunspell through `SpellingHelper.GetSpellChecker` -> `ISpellEngine`) | V/VwTextBoxes.cpp:8621-8640; V/VwRootBox.cpp:4052 `GetDictionary`, 291 `SetSpellingRepository`; RS/RootSiteEditingHelper.cs:144 `GetDictionary`; Enchant/Hunspell binding UNVERIFIED (grep for "Enchant" in *.cs found nothing in this repo; lives in SIL libpalaso/lcm) | NO | YES: the host supplies `ISpellCheckHost.GetChecker(ws)` and `ISpellChecker` (ADR 0020); no engine in the library | M |
| NFC-normalise the word before checking | V/VwTextBoxes.cpp:8610-8615 | NO | YES: the word is NFC before `Check` and `Suggest` | S |
| Skip rules: ktptSpellCheck doNotCheck; non-editable text unless forceCheck; ORC; style-marked | V/VwTextBoxes.cpp:8552-8562,8635-8650 | NO | YES: do-not-check, non-editable and read-only text unless forced, hidden text; a lone object is not marked, a word with one beside text is `Embedded` (ADR 0026) | S |
| Restart/reset spell check when dictionary or text changes | V/VwRootBox.cpp:4027 `ResetSpellCheck`, 4035 `RestartSpellChecking` | NO | YES: `RestartSpelling`; an edit, a change of the read-only flag of the field or its editor, `RefreshFromDocument` and a direct change a paragraph notices (`Paragraph.ChangeStamp`, a property bag changed in place included) make paragraphs due again | S |
| Right-click on a squiggled word: suggestion list (top 7 inline, rest in "Additional suggestions" submenu, "No suggestions"), Add to Dictionary, with undoable add | RS/SpellCheckHelper.cs:53 `ShowContextMenu`, 74 `MakeSpellCheckMenuOptions`, 188 `GetSuggestions`, 458 `MakeWssSuggestions`, 518 `SpellCorrectMenuItem.DoIt`, 582 `AddToDictMenuItem`, 667-712 `UndoAddToSpellDictAction`; RS/RootSiteEditingHelper.cs:36 (max 7), 158 | NO | PARTIAL: `SpellingSuggestionsAt` gives the word, problem, all suggestions and the 7 inline / rest split; the host builds the menu (ADR 0021) | L |
| Adjusting the word under the mouse beyond `GrowToWord` (adjacent word-forming material, differing spell props) | RS/SpellCheckHelper.cs:293 `AdjustWordBoundary`, 317 `NextCharIndex` | NO | N/A: the word comes from the marks, not from a selection | M |
| Embedded-NSC suggestions (word containing mixed ws or ORC) | RS/SpellCheckHelper.cs:372 `MakeEmbeddedNscSuggestion` | NO | YES: `SpellingSuggestions.WithMissingSpaces()` and `ApplySpellingSuggestion(suggestions, runs)` insert the spaces, each part keeping its writing system and an object kept (ADR 0026); "make the whole word writing system X" is `SpellingSuggestions.WritingSystems` and `WithWholeWordIn(ws)` | M |
| Spelling status commands: Edit Spelling Status, View Incorrect Words in Use, Change Spelling (respeller), Show Vernacular Spelling Errors | CFG:110-113 | N/A (host) | N/A | host |
| Replace misspelled word with suggestion in one undo step, keeps run props | RS/SpellCheckHelper.cs:542 `DoIt` | NO | YES: `ApplySpellingSuggestion(suggestions, string)`, one undo step, keeps the writing system and the first run's properties (ADR 0026) | M |

---

## 10. Find/replace and highlighting

| Feature | Legacy source | FA | TE | Size |
|---|---|---|---|---|
| Find dialog (modeless, shared per app): Find Next, Find Previous, Replace, Replace All, Stop, wrap-around prompts, close | FwCoreDlgs/FwFindReplaceDlg.cs:1133 `OnFindNext`, 1176 `OnReplace`, 1203 `OnReplaceAll`, 1228 `DoReplaceAll`, 1337 `OnStop`, 1881 `FindNext`, 1893 `FindPrevious`, 1905 `Replace`, 1953 `Find`, 2179 `AttemptWrap`; CFG:136-137 (Ctrl+F, Ctrl+Shift+F, Ctrl+H) | NO | host owns the dialog; the editor has `DocumentEditor.TryFind` (next and previous, wrap), `SelectRange`, `ReplaceMatch`, `ReplaceCurrent` and `ReplaceAll`, and the field `Find` and `SelectMatch` (ADR 0025) | L (dialog is host; editor needs the pattern API) |
| Engine: `IVwPattern` find/replace over a root box with options Match case, Match diacritics, Whole word, Match ws, Match old ws, Match exactly, Regular expressions, Match compatibility (NFKC) | V/VwPattern.cpp:184-318 (properties), 608 `put_MatchCompatibility`, 634 `Find`, 667 `FindFrom`, 716 `FindNext`, 1670 `FindIn`, 1718 `ReplaceAllWithAlgorithm`, 1787 `ReplaceAllIn`, 2136 `Compile` | NO | PARTIAL: match case, match diacritics, whole word, regular expressions, writing system, character style (`DocumentEditor.Find.cs`, `FindPlan.cs`); ordinal over NFD with our own folding, no ICU collation rules, no compatibility matching (FieldWorks does not implement it either) | L |
| Search by formatting: find text with a given style / writing system; replace with style/ws | FwFindReplaceDlg.cs:1086 `btnFormat_Click`, 1101,1116, 2538 `PopulateWritingSystemMenu`, 2561 `PopulateStyleMenu`, 2618 `ApplyStyle`, 2642 `ApplyWS`; V/VwPattern.cpp ignores runs with differing tags 1029,1068 | NO | PARTIAL: find and replace by character style and by writing system, with or without text; no tags | M |
| Regular expression helper menu; error message | FwFindReplaceDlg.cs:1388-1421,1600-1621; V/VwPattern.cpp:330 `get_ErrorMessage` | NO | PARTIAL: `FindOptions.IsValid` gives the error; no helper menu (host) | M |
| Replacement text with group references | V/VwPattern.cpp:380 `get_ReplacementText`, 582 `get_Group`; FwFindReplaceDlg.cs:2377 `ReplaceString` | NO | YES: `$1` and the like, in .NET syntax (`ReplaceMatch`, `ReplaceAll`) | M |
| Highlight = the found text becomes the selection (`Install`); no all-matches highlight found | V/VwPattern.cpp:1833 `Install`, 1868 `GetSelection`; UNVERIFIED that no persistent highlight overlay exists | NO | YES: the match is selected and marked as the current match (`CurrentMatch`); every match can be highlighted (`SetFindHighlight`), recomputed on each document change | M |
| Find within non-rootbox views (browse, XML doc views) via collector env | FwCoreDlgs/FindCollectorEnv.cs (742 lines; "doesn't work for styles/tags/WS applied by the VC") ; hosts LexText/Interlinear/InterlinMaster.cs:1165, xWorks/XmlDocView.cs:853, XhtmlDocView.cs:1476, Controls/XMLViews/BulkEditBar.cs:3807 | NO | NO | M |
| Replace guarded by editability (`IsReplacePossible`) and undone as one task | FwFindReplaceDlg.cs:2283,2341 | NO | YES: `CanReplace` (fully editable text only); each replace and each replace-all is one undo step | M |
| Settings persisted (pattern, options, replace text, window) | FwFindReplaceDlg.cs:591 `RestoreAndPersistSettingsIn`, 2950-3220 `VwPatternSerializableSettings` | N/A | N/A | host |
| Find in single FwTextBox widgets (ws-aware text boxes inside dialogs) | Common/Controls/Widgets/FwTextBox.cs (not read in detail; UNVERIFIED) | NO | NO | M |

---

## 11. Context menus

| Feature | Legacy source | FA | TE | Size |
|---|---|---|---|---|
| Right mouse up opens menu at the click; right mouse down keeps an existing range if inside it, else installs an IP in editable text without activating a link | SRS/SimpleRootSite.cs:4449-4535 `OnMouseDown`, 4556 `OnRightMouseUp`, 4669 `DoContextMenu`, 4891 `OnContextMenu` | YES (MenuFlyout) FA/FwFieldControls.cs:725-739 | PARTIAL: the field raises `ContextMenuRequested` on a right click (Avalonia's context request) after keeping a selection clicked inside or moving the caret; the host builds and shows the menu | M |
| Keyboard: Apps key and Shift+F10 open the menu at the IP (computed from selection location) | SRS/EditingHelper.cs:704-717; SimpleRootSite.cs:2387 `ShowContextMenuAtIp`, 4601 `HandleContextMenuFromKeyboard`, 4616 `ProcessCmdKey` | PARTIAL (Avalonia default) | YES: the Menu key and Shift+F10 raise `ContextMenuRequested` for the caret | S |
| Standard items: Cut, Copy, Paste (enabled via CanCut/CanCopy/CanPaste), plus slice-specific menus bound by `contextMenu=` / `menu=` in layouts | SRS/EditingHelper.cs:3624-3780; CFG menus; xCore | PARTIAL: Copy only on non-bridge rows; bridge host menu replaces it (FA/FwFieldControls.cs:836-851) | NO | M |
| Spelling suggestions menu inserted on top of the menu | RS/SpellCheckHelper.cs:74-187 | NO | YES: a context request fills `ContextInfo.SpellingSuggestions` (the strings) and `ContextInfo.Spelling` (the problem, word, writing system and the menu's choices) for a marked word | M (see 9) |
| Rich-text items: Character Style, Writing System, Link, Delete embedded object | (legacy: via Format menu and slice menus) | YES: FwFieldControls.cs:342-669 | NO | M |
| Right-click on object/picture: object-specific menu | SRS `DoContextMenu` override points; DetailControls slice menus (UNVERIFIED) | PARTIAL | PARTIAL: `ContextInfo.EmbeddedObject` reports the object under the pointer (data, range, whether a link); the host builds the menu | M |
| Edit menu commands that act on text: Undo, Redo, Cut, Copy, Paste, Paste Hyperlink, Select All, Find, Replace | CFG:121-137, 445-465 | N/A (host) | NO (Ctrl+Z/Y/C/X/V/A keys only, `RichTextField.HandleKey`) | S |
| Insert menu commands acting on text: Special character, Link to File | CFG:130,134,513-519 | N/A | NO | host |
| Format menu: Styles..., Apply Style..., Writing System submenu, Set up WS | CFG:120-123,520-525 | N/A | NO | host |

---

## 12. Read-only and editable states, and field-level behaviours

| Feature | Legacy source | FA | TE | Size |
|---|---|---|---|---|
| Whole-view read-only (not editable root box) | SRS/EditingHelper.cs:3690 `CanEdit`; selection `IsEnabled` V/VwSelection.cpp:1012; `Callbacks.EditedRootBox` null | YES (IsReadOnly) FA/FwFieldControls.cs:126-132 | YES: IsReadOnly gate on every edit (`DocumentEditor.IsReadOnly` in src/SIL.TextEditing/DocumentEditor.Editability.cs; `RichTextField.IsReadOnly`; automation `RichTextField.FieldPeer`, both in src/SIL.TextEditing.Avalonia/RichTextField.cs) | M |
| Per-property / per-run editability (ktptEditable, semi-editable for type-ahead) | V/VwEnv.cpp:2207; V/VwSelection.cpp:994 `get_IsEditable`; V/VwTextBoxes.cpp:570 `EditableSubstringAt` | PARTIAL: row only | PARTIAL: per run, not per string property (`DocumentEditor.AllowsEditing` and `CanEditRange` in src/SIL.TextEditing/DocumentEditor.Editability.cs; `Run.Editable` in src/SIL.TextEditing/Run.cs) | L |
| Read-only still allows selection and copy; Cut/Paste/Delete disabled; typing ignored; cursor stays I-beam | SRS/EditingHelper.cs:3624-3720; OnKeyDown 663-665 (Enter/Delete guards) | YES (TextBox) | YES: selection and copy work; Cut, paste, delete, typing refused (`DocumentEditor.CanEditRange` in src/SIL.TextEditing/DocumentEditor.Editability.cs; `RichTextField.HandleKey` in src/SIL.TextEditing.Avalonia/RichTextField.cs) | S |
| Read-only value with explanatory tooltip (lossy runs, embedded object, audio) | FA only (new behaviour; legacy edits everything) | YES: FwFieldControls.cs:806-811 | N/A | S |
| Tab / Shift+Tab leave the field; Enter does NOT leave | V/VwRootBox.cpp:~1672-1740; SRS `IsInputKey` 4907-4943 | YES: FA/DetailValueStopGroup.cs:124 | YES: Tab leaves only when there is no further editable place; Enter does not leave (`RichTextField.HandleKey` in src/SIL.TextEditing.Avalonia/RichTextField.cs) | M |
| Arrow keys leave the field at start/end; Up/Down move to previous/next ws row | V/VwRootBox.cpp (S_FALSE paths); FA task LT-22688 | YES: DetailValueStopGroup.cs:136-149 | NO | M |
| Moving between writing-system rows: legacy one root box shows all alternatives; Up/Down and Tab traverse | V/VwSelection.cpp:8487, 10213; MultiStringView (Common/Controls/DetailControls, UNVERIFIED line) | YES: one tab stop per field, arrows between rows (DetailValueStopGroup.cs:15-23) | NO (single field per document; many-fields scenario in playground) | M |
| Entering a field with Tab puts caret at start of text; with Shift+Tab at end (legacy: first/last editable place) | V/VwSelection.cpp:10213 | PARTIAL: start only | PARTIAL: Tab/Shift+Tab land on the start/end of an editable place (`DocumentEditor.TryGetEditablePlace` in src/SIL.TextEditing/DocumentEditor.Editability.cs) | S |
| Ghost / prompt text for empty field | (legacy: view constructors `GhostPrompt`; UNVERIFIED) | YES: Watermark FA/FwFieldControls.cs:817-835 | NO | S |
| Focus: initial selection on load (`WantInitialSelection`), selection kept per field when focus lost, `EnsureDefaultSelection` | SRS/SimpleRootSite.cs:3817-3830, 3132-3170; FA/DetailFocusMemory.cs | YES | NO | M |
| Data-update monitor: ignore input while an update is in progress | SRS/DataUpdateMonitor.cs; SimpleRootSite.cs:4457 | N/A | N/A | S |
| Commit on focus loss / selection change (`CompleteEdits`) | V/VwSelection.cpp:1257 `CompleteEdits`; V/VwRootBox.cpp:3531 `LoseFocus` | YES (focus-loss autosave) | PARTIAL: Committed event per edit | S |
| "Edit boxes with text typed-ahead" (SemiEditable) | KRN:ktptSemiEditable | NO | YES: semi-editable text takes typing, deletion and paste; formatting refused (`DocumentEditor.CanFormatSelection` in src/SIL.TextEditing/DocumentEditor.Editability.cs) See ADR 0024. | M |

---

## 13. Bidi and complex scripts

| Feature | Legacy source | FA | TE | Size |
|---|---|---|---|---|
| Uniscribe shaping/itemisation per run, script-aware clusters, fallback fonts | V/lib/UniscribeSegment.cpp:785 `ShapePlaceRun`, 3064 `CallScriptItemize` | YES (Avalonia) | YES (Avalonia TextFormatter; headless tests on Indic, Thai, Arabic) | M |
| Graphite smart-font rendering (ws-selected) | V/lib/GraphiteSegment.cpp, GraphiteEngine.cpp | NO (Avalonia has no Graphite) | NO, deliberately (ADR 0022) | L |
| OpenType features per ws and per run | UniscribeSegment.cpp:241; ws DefaultFontFeatures V/VwPropertyStore.cpp:~1999 | NO | PARTIAL: ws features only (`ParagraphLayout.MakeProperties`) | M |
| Paragraph direction (ktptRightToLeft) and ws direction; base direction affects alignment (Leading/Trailing) | V/VwPropertyStore.cpp:1357; alignment keyed to direction | PARTIAL | PARTIAL: Rtl flag; alignment not applied | M |
| Direction depth per segment | V/lib/UniscribeSegment.cpp:1335 `get_DirectionDepth`, 1353 `SetDirectionDepth` | N/A | N/A | S |
| Logical vs physical arrows; F7/F8 | see group 3; SRS/SimpleRootSite.cs:2433 `ComplexKeyBehavior` | PARTIAL | PARTIAL (visual instead of logical) | M |
| Ctrl+arrow word moves in RTL (logical) | V/VwSelection.cpp:9492 | NO | PARTIAL (logical, docs/reference/editing.md) | S |
| Home/End in RTL | V/VwSelection.cpp:8777 `HomeKey(fLogical)`, `PhysicalHomeOrEnd` | PARTIAL | PARTIAL (logical only) | S |
| Bidi caret: two-position IP at direction boundaries, caret affinity | V/lib/UniscribeSegment.cpp:2001 `PositionsOfIP` | NO | YES (affinity, CurrentHit; docs/reference/avalonia-versions.md; stops, caret, mouse and selection of a right-to-left paragraph's multi-run line are read from its runs, `LineInfo.UsesRuns`) | M |
| Inline object inside RTL text positioned correctly | Uniscribe | N/A | PARTIAL: Avalonia fault, workaround `ObjectAsGlyph` | M |
| Valid insertion points (no IP inside a cluster/ligature) | V/lib/UniscribeSegment.cpp:1508 `IsValidInsertionPoint`, 2477 `FindValidIPBackward`/`Forward` | YES | YES (shaper stops; grapheme boundaries on a right-to-left paragraph's line of several runs) | M |
| Combining marks and NFC/NFD: layout in NFC, offsets mapped back | V/lib/NfcOffsetMap.h; UniscribeSegment.cpp:1605-1700 `OffsetInNfc`, `OffsetToOrig` | N/A | PARTIAL: no normalisation; offsets are UTF-16 of stored text | M |
| Line breaking per ws locale (ICU) | V/lib/LgLineBreaker.cpp:77-115 | NO | NO (Avalonia default breaker) | M |
| Justification stretch (kashida/inter-word) | V/lib/UniscribeSegment.cpp:3737 `NumStretchableGlyphs`, 3760 `StretchGlyphs` | NO | PARTIAL: inter-word stretch only (equal share per interior space), no kashida; underlines and strikethrough run on as one line across the stretched text (ParagraphLayout.DrawDecorations) | M |
| Surrogate pairs / grapheme clusters / Indic conjuncts | V/VwSelection.cpp:3938-4000 | PARTIAL | YES (`GraphemeClusters` incl. GB9c, TE/M/GraphemeClusters.cs) | M |
| Vertical orientation | SRS/OrientationManager.cs | NO | NO | L |

---

## 14. Accessibility

| Feature | Legacy source | FA | TE | Size |
|---|---|---|---|---|
| MSAA root: name, role TEXT/PANE/GRAPHIC etc., value, state, parent/child navigation, hit test, location, select, focus | V/VwAccessRoot.cpp:122 `accNavigate`, 178-215 children, 267 `get_accName`, 280 `get_accRole`, 301 `get_accValue`, 337 `accLocation`, 363 `accHitTest`; boxes expose cells/rows/tables (:13-20) | PARTIAL: AutomationId/Name on box and field (FA/FwFieldControls.cs:~110-140), tests OwnedControlAutomationConventionTests | PARTIAL: Edit peer, name "Rich text field", value empty (`SetValue` no-op, IsReadOnly true) `RichTextField.FieldPeer` | M |
| Accessible name settable by host | SRS/SimpleRootSite.cs:3724 `SetAccessibleName`; V/VwRootBox.cpp:5240 | YES (AutomationProperties.SetName) | NO (hard-coded name) | S |
| Wrapper for WinForms (`AccessibleObject` bridging IAccessible) | SRS/AccessibilityWrapper.cs:66-264 | N/A | N/A | S |
| UIA TextPattern (caret, selection, ranges, attributes) | disabled in legacy (WM_GETOBJECT comment block: FWR-2874/3045) | via Avalonia TextBox: YES | NO (docs/reference/limitations.md; empty UIA subtree inside WinFormsAvaloniaControlHost unexplained) | L |
| Screen reader reads caret line / word on move | via IAccessible text events UNVERIFIED | via TextBox | NO | L |
| On-screen keyboard compatibility | FWR-2874 note | UNVERIFIED | UNVERIFIED | S |
| High-contrast / system colours for text and selection | V/VwColor.cpp, kclr* constants | YES (theme) | PARTIAL: the selection and caret brushes, and the text, border and page defaults (when the host has not set `Foreground` or `Background`), come from theme or system resources and are re-resolved on a theme change; in a Windows high contrast theme the text, page and selection are the system window text, window and highlight colours, and selected text and its underlines the highlight text colour (`SystemColours`; the selected part is drawn from a second layout of the paragraph in that one colour, `ParagraphLayout.InColour`), as FieldWorks uses the window text colour; a theme or system colour change redraws without a new layout (the default text colour is one brush changed in place); a host-set colour is kept, and `ResetForeground`/`ResetBackground` go back to the theme (docs/reference/rendering.md, "Colours and themes") | S |
| Keyboard-only operation of every command | Edit/Format menus | PARTIAL | PARTIAL | M |

---

## 15. Anything else found

| Feature | Legacy source | FA | TE | Size |
|---|---|---|---|---|
| Printing a view: pages, headers/footers, page breaks (keep-with-next, widow/orphan), print root site | SRS/PrintRootSite.cs; SRS/SimpleRootSite.cs:2831-2985; V/VwPrintContext.cpp (516), V/VwLayoutStream.cpp (1453); V/VwRootBox.cpp:3290-3472 | NO | NO (out of scope?) | L |
| Lazy layout of very large documents (lazy boxes, expand on demand, scroll warm-up) | V/VwLazyBox.cpp; SRS/SimpleRootSite.cs:1738-2300 | N/A | NO (200-paragraph scenario only) | L |
| Multi-view synchronisation: several root boxes showing same data, scroll/selection sync | V/VwSynchronizer.cpp; V/VwRootBox.cpp:3730 `SetSynchronizer`; SRS `IRootSite`, RS/RootSiteGroup | NO | NO | L |
| Data binding: notifiers update boxes on PropChanged (live refresh from model) | V/VwNotifier.cpp (4395 lines); V/VwRootBox.cpp:201 `PropChanged`, 3882 | N/A (re-show) | PARTIAL: `ExternalReplaceParagraph` (`DocumentEditor`) | L |
| View constructors (VC) build the content: fragments, nested views, ghost strings, conditional display | RS/FwBaseVc.cs, StVc.cs, SRS/VwBaseVc.cs; V/VwEnv.cpp (2599) | N/A (ViewDefinition compiler) | N/A (editor is not a view engine) | N/A |
| Drop-cap strings (drop cap invoked by a known named style) | V/VwTextBoxes.cpp:172,201 `VwDropCapStringBox`; V/VwPropertyStore.cpp:1609-1625 | NO | NO | M |
| Tables (VwTableBox, rows, cells, spacing, borders, rules) | V/VwTableBox.cpp (1473 lines), VwTableBox.h | NO | NO | L |
| Check boxes / integer pictures in views | V/VwSimpleBoxes.h:1436-1469 `VwIndepPictureBox`, `VwIntegerPictureBox` | NO | NO | M |
| Overlay tags drawn at line ends and tag-picker menus | V/VwOverlay.cpp (809 lines); V/VwTextBoxes.cpp:8760+ `FindGuidDiffs`, `TagSelectInfo` | NO | NO | M (UNVERIFIED use in FLEx) |
| Text-source overrides (spelling squiggles, TSF display attrs, diff views) | V/VwTxtSrc.cpp (2349 lines) `VwSpellingOverrideTxtSrc` | NO | NO | M |
| Invisible-space click-to-insert, show/hide zero-width spaces | LexText/Interlinear/RawTextPane.cs:192-352; CFG:481 | NO | NO | S |
| Interlinear/"Texts" pane behaviours: select-by-segment, make-word-analysis clicks | LexText/Interlinear (InterlinDocForAnalysis, RawTextPane) | NO | NO | host/UNVERIFIED |
| Audio ws alternatives (play button instead of text) | FA IsAudio, FwFieldControls.cs:126 | YES (read-only) | NO | N/A |
| Gear / field options menu on label cell; slice menus `menu=` | FA/FwFieldControls.cs:40-50, 836-870 | YES | N/A | host |
| Error recovery when Views throws (COMException E_FAIL on paste, wait cursor) | SRS/EditingHelper.cs:3470-3505 | N/A | N/A | S |
| Clipboard retry on busy clipboard | SRS/EditingHelper.cs:3158-3172 | NO | UNVERIFIED (ClipboardCompat.cs) | S |
| Windows right-to-left scrollbar mirroring, DPI, rendering via ManagedVwDrawRootBuffered | Src/ManagedVwDrawRootBuffered; SRS/HoldGraphics.cs | N/A | N/A | N/A |
| Zero-width / non-printing glyph display (`Invisible spaces`) | see above | NO | NO | S |
| Linux support (Mono, Cairo, IBus) | V/lib/VwGraphicsCairo.cpp; SRS/IbusRootSiteEventHandler.cs | UNVERIFIED | UNVERIFIED (net10 target only) | M |
| TE-only extras (not legacy): triple-click paragraph, playground format commands | `RichTextField.OnPointerPressed`; the playground's format commands in `PlaygroundView` | n/a | extra | n/a |

---

## Totals (rows per group, table rows excluding headers; group 1 has two tables)

1: 44 | 2: 11 | 3: 29 | 4: 22 | 5: 13 | 6: 11 | 7: 14 | 8: 10 | 9: 12 | 10: 10 | 11: 9 | 12: 13 | 13: 16 | 14: 8 | 15: 20

Total feature rows: 242. Rows in group 1 are counted from both tables (1a, 1b) plus one separator-free count; small off-by-one differences are possible.

Main UNVERIFIED items: who writes TabDef/TabList/MarkItem; MSW margin-collapse semantics; overlay-tag use in FLEx; spell engine binding (Enchant vs Hunspell); end-to-end Tab/arrow-out chain to DataTree; vertical text users; all-matches find highlight; whether LCModel merges consecutive typing undo units; Keyman/IME in FwAvalonia; Graphite plans; Linux scope.

REPORT DONE
