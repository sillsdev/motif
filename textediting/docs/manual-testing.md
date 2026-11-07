# Manual testing

The automated tests cannot do these: real keyboards, input method editors, Keyman, a screen reader, the mouse, other
applications' clipboards and focus handoff in a real WinForms form. A person does them with the **playground**, the
shared sample UI that all three hosts show. It is meant for a tester who has not seen the code.

Write **PASS** or **FAIL** in the Result column of every step you do, and put what you saw in Notes when it differs
from Expected. A step you could not do is **SKIP** with the reason. Copy the Model tab or the event log (select the
text, Ctrl+C) into a bug report; they are plain text and show exactly what the model holds.

## Start

From the repository root on Windows (the .NET 10 SDK and the .NET Framework 4.8 targeting pack are needed):

| Command | What it is | Avalonia |
|---|---|---|
| `run desktop10` | Plain Avalonia desktop window on .NET 10: Motif's shape | 12.1.2 |
| `run desktop48` | The same window on .NET Framework 4.8 | 11.3.17 |
| `run winforms48` | A WinForms form: WinForms TextBox, the playground, WinForms TextBox. FieldWorks' shape | 11.3.17 |

`run <host> --scenario "Inline picture (ORC)"` opens a scenario directly. The first run builds the host.

Where a step says **all hosts**, do it on each of the three and write one result per host (D10, D48, W48). Steps for
`W48` only need the WinForms host.

## The window

Turn on **Developer mode** (the check box at the top right, or start the host with `--dev`): most steps below use its controls. Without it
the window shows only the scenario picker, the field, the selection bar and the Markdown.

* **Selection bar**: select text and it appears above the selection: **B I U**, the writing system, the character style, and under
  **More** the paragraph style and Picture. It never takes focus from the field and goes away when the selection does.
* **Top** (developer mode): field count (for the Many fields scenario); **B I U**; Undo, Redo; Picture, Cut, Copy,
  Paste. The toolbar never takes focus from the field.
* **Writing-system bar**: `ws:` shows the writing system at the caret. Default analysis (en) and default vernacular
  (swh) buttons, then every sample writing system: en, fr (analysis) and ar, swh, hi, ta, th (vernacular). With a
  selection a button changes the selected text; with a caret it sets what you type next, as a keyboard switch would.
  Sample data only; nothing is read from a project.
* **Field** (left): the editor. Ctrl+B, Ctrl+I, Ctrl+U, Ctrl+Z, Ctrl+Y, Ctrl+A, Ctrl+C, Ctrl+X and Ctrl+V work in it.
* **Markdown tab** (top right, shown first): the field as FieldWorks Markdown, the form a person or an agent edits. Edit it and press
  Apply to put it in the field as one undo step; an error, read-only text or a second paragraph in a single-paragraph field changes nothing
  and says why. Revert shows the field again.
* **Model tab** (beside the Markdown tab; click it for the steps below that read the model): caret, anchor and selection as paragraph:offset; undo counts; every paragraph with its key (`(none)` until the host assigns one),
  direction, style and runs. A run reads `r1 [9,13) ws=ar "text" props{3=1@3, 9001=42@1}`: offsets, writing system,
  text with escapes (`́` a combining mark, `\U0001F600` a surrogate pair, `￼` an object), then the property
  bag as `number=value`, `@n` for a variation. Documents over 30 paragraphs show a window around the caret.
* **Event log** (bottom right): `caret ws en -> ar: would switch keyboard to ar` (the library only reports; it never
  switches a keyboard), undo and redo requests, clipboard actions, edits other than typing.

## 1. Basics (all hosts)

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 1.1 | Start. Click in the text of the first paragraph and type a few Latin letters. | Letters appear at the caret; the caret moves; `ws:` stays `en`; the Model tab's run text and caret offset update with each key. | | | | |
| 1.2 | Pick each scenario in turn. | Each loads without a delay you notice; the description under the toolbar changes; the Model tab shows the new document; no text is clipped or overlaps. | | | | |
| 1.3 | In Latin and Arabic, click inside the Arabic word, then inside an English word. | `ws:` follows the caret (`ar`, then `en`); the log gets `would switch keyboard to ar` and `... to en`. | | | | |
| 1.4 | Put a caret in English text, click the `fr` button, type `bonjour`. | `ws: fr`; the typed text is a new run `ws=fr` in the Model tab. | | | | |
| 1.5 | Click Default vernacular, type `habari`; then Default analysis. | `ws: swh`, then `ws: en`; run `ws=swh "habari"` in the Model tab. | | | | |
| 1.6 | Select a word with Shift+Right, click the `hi` button. | Only the selected text becomes `ws=hi` and is drawn in the Hindi font; the selection stays. Undo restores it. | | | | |
| 1.7 | Select a word, press B, then B again; then Ctrl+I and Ctrl+U. | Bold, italic, underline are drawn; the Model tab shows `props{3=1@3}` (bold), `2=1@3` (italic), `5=1@3` (underline), and the Markdown tab `**word**`, `*word*` and `[word]{underline=single}`; pressing B again removes it and the runs merge back. Paragraph keys do not change. | | | | |
| 1.8 | Type, press Undo, Undo, Redo, Redo. Then Ctrl+Z and Ctrl+Y in the field. | Text, caret and the Model tab's `undo n done, n undone` follow; the log says `undo requested (button)` or `(Ctrl+Z in the field)`. | | | | |
| 1.9 | Press Enter in the middle of a paragraph, then Backspace at the start of the new one. | Two paragraphs, then one. The first keeps its key on a split and on the join; the new right-hand paragraph has `key=(none)` until the host assigns one (compare in the Model tab). | | | | |
| 1.10 | Press Enter at the very start of a paragraph. | A new empty paragraph appears above; the new paragraph shows `key=(none)` and the original key stays on the paragraph that holds the text. | | | | |
| 1.11 | Scenario *Inline picture*: put the caret after a picture, Backspace; press Picture to insert one; Delete before a picture. | A picture is deleted or inserted as one unit (one character, `￼`), never half. Works in the Arabic paragraph too. | | | | |
| 1.12 | Scenario *Unknown properties*: select the whole second paragraph, press B twice, then split and join it with Enter and Backspace. | Properties 9001, 9002, 9003 and the paragraph style are still in the Model tab, unchanged. | | | | |

## 2. Keyboard layout switching on caret moves (all hosts)

The library reports the writing system at the caret; it does not switch layouts yet. This checks what a host would
be told.

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 2.1 | Add an Arabic keyboard in Windows. In Latin and Arabic, arrow through a boundary between English and Arabic text. | At the boundary the log gets exactly one `would switch keyboard to ...` line per change of writing system, none while the writing system is unchanged. | | | | |
| 2.2 | Change the Windows layout with Win+Space while the caret is in an `fr` run. | Nothing happens in the field or the log, and the typed text stays `fr`: switching layouts does not change the writing system yet. Record what you see; this is the gap a future host wiring closes. | | | | |

## 3. Real typing and IME (all hosts)

Add the input methods under Settings, Time and language, Language: Japanese (Microsoft IME) and Chinese (Pinyin).

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 3.1 | Type a sentence of English, French accents (AltGr or a French layout) and punctuation. | Everything appears as typed; dead keys compose (`e` after `^`); no doubled or lost letters. | | | | |
| 3.2 | Japanese IME: in the *Empty* scenario type `nihongo`. | The composition shows inline with an underline; the candidate window sits at the caret; Space cycles candidates; Enter commits the text into the field; the Model tab shows it only after the commit. | | | | |
| 3.3 | Same, then press Esc during composition. | The composition disappears and nothing is inserted. | | | | |
| 3.4 | Chinese Pinyin: type `zhongwen`. | Same as 3.2. | | | | |
| 3.5 | Compose while the caret is inside Arabic text, then in the RTL paragraph. | The composition appears at the caret, the candidate window is not left behind, the committed text lands at the caret. | | | | |
| 3.6 | Compose with text selected. | The selection is replaced on commit, not on the first keystroke. | | | | |
| 3.7 | Move the window while a composition is open. | The candidate window follows the caret. | | | | |

## 4. Keyman (all hosts; needs Keyman Desktop and a keyboard that reorders or deletes, such as Khmer or Tamil 99)

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 4.1 | Select the Keyman keyboard, type a sequence in the *Empty* scenario. | The result matches typing the same keys in Notepad. | | | | |
| 4.2 | Type a sequence in which a later key rewrites earlier characters (reorder, or replace the last syllable). | The earlier characters are changed correctly: the field reports its surrounding text to the keyboard. Check the Model tab for stray characters. | | | | |
| 4.3 | Move the caret into the middle of existing text and type the same sequence. | Same result as at the end of the text. | | | | |

## 5. Right-to-left: caret and selection by mouse and arrows (all hosts)

Scenario *Latin and Arabic*. **Known:** on Avalonia 11.3.17 (D48 and W48) clicking in an RTL paragraph can put the
caret about two characters from where you clicked (fixed in Avalonia 12). Record it either way.

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 5.1 | In the all-Arabic paragraph press Home, then Left repeatedly. | Arrows are logical (as in FieldWorks): in a right-to-left paragraph Left goes forward through the text, so the caret moves to the left on screen, one cluster at a time, never inside a joined letter. Right goes back. | | | | |
| 5.2 | Press Left repeatedly across an English word inside the RTL paragraph, then Right back. | Left walks the text in logical order and Right reverses it, one cluster per key, never skipping or repeating an offset (check the Model tab). Inside the English word the caret moves right on screen for Left, and at the language boundary it may jump across the line: that is expected, FieldWorks does the same. | | | | |
| 5.3 | Click in the middle of an Arabic word. | The caret is where you clicked (D10). On D48 and W48 it may be two characters off: note it (known). | | | | |
| 5.4 | Click and drag across the boundary between Arabic and English text. | The selection is one visual block or the correct set of blocks for the logical range; the Model tab's `selection a -> b` matches the highlight. | | | | |
| 5.5 | Shift+Left and Shift+Right from the middle of the RTL paragraph, then the same in an LTR paragraph. | Shift+Left grows the selection forward in the RTL paragraph (backward in LTR), Shift+Right the reverse; the Model tab's caret offset moves by one cluster per key and the anchor stays. | | | | |
| 5.6 | Scenario *Inline picture*: arrow across the picture in the Arabic paragraph. | The caret passes the picture in one step. (Known: the picture may sit in the wrong place in RTL text on both Avalonia versions.) | | | | |
| 5.7 | Scenario *Devanagari, Tamil, Thai*: arrow and Backspace through conjuncts and stacked marks. | The caret stops between clusters, never inside; Backspace removes what the language expects (compare with Notepad). | | | | |
| 5.8 | Scenario *Latin and Arabic*, LTR paragraph with Arabic inside: put the caret just before the Arabic word, press Right once; then click after it, press Left until you are back at the same offset. | The offset is the same but the caret is drawn on different sides of the boundary, depending on the direction you arrived from. | | | | |
| 5.9 | Arrow with a selection in an RTL paragraph (Shift+Right three times, then Left without Shift). | The selection collapses to its logical end that the key points to (Left: its later end in RTL), not to the screen-left edge. | | | | |

## 5a. FieldWorks editing: Backspace, Delete and normalisation (all hosts)

Scenario *NFD, marks, surrogates, PUA* (it sets the field's normalisation form to NFD, as FieldWorks does).

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 5a.1 | Type `e`, then a combining acute (or a keyboard that sends it), then Backspace. | Only the accent goes; `e` stays. | | | | |
| 5a.2 | Put the caret before a letter with two marks after it (`e` plus acute plus circumflex) and press Delete. | The letter and both marks go in one press. | | | | |
| 5a.3 | Backspace after an emoji (a surrogate pair), and Delete before one. | The whole emoji goes each time, never half. | | | | |
| 5a.4 | With NFD set, type or paste a precomposed `é` (U+00E9). | The Model tab shows two code points (`e` plus U+0301); the caret is after both. | | | | |
| 5a.5 | With NFD set, copy `é` and paste it into Notepad, then check its form (for instance by Backspace: one press removes the whole `é`). | Notepad receives the precomposed form (NFC). Pasting back into the field inside the same window keeps the stored NFD. | | | | |

## 6. Tab and focus in and out of the WinForms host (W48)

Tab order is: first WinForms TextBox, the playground field, second WinForms TextBox. The toolbar and the tabs
are not tab stops (click them).

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 6.1 | Click the first TextBox, press Tab. | Focus enters the playground field: the caret shows and typing goes into the field. | n/a | n/a | | |
| 6.2 | Press Tab again. | Focus moves to the second TextBox. | n/a | n/a | | |
| 6.3 | Press Shift+Tab twice. | Focus returns to the field, then to the first TextBox. | n/a | n/a | | |
| 6.4 | In the field press Left, Right, Up, Down, Home, End, Backspace, Delete, Ctrl+A, Ctrl+C, Ctrl+X, Ctrl+V, Ctrl+Z, Ctrl+Y. | Each acts in the field; none moves WinForms focus or beeps; Enter makes a new paragraph and does not press a default button. | n/a | n/a | | |
| 6.5 | Click a toolbar button (Bold), then type. | Focus stays in the field; typing continues. | | | | |
| 6.6 | In the Many fields scenario click different fields, then Tab. | The Model tab and `ws:` follow the clicked field; Tab leaves the control rather than walking through every field (note what it does). | n/a | n/a | | |
| 6.7 | Alt+Tab to another program and back. | The caret returns to the field where it was; typing works at once. | | | | |
| 6.8 | D10 and D48: press Tab in the field. | Focus leaves the field (the toolbar is not a tab stop), or stays; note which. | | | n/a | |

## 7. Screen reader (all hosts)

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 7.1 | Start Narrator (Win+Ctrl+Enter), focus the field. | **Known gap:** Narrator reads the field as an edit named "Text field" and not its content; there is no TextPattern. Record exactly what it says. | | | | |
| 7.2 | Navigate to the toolbar buttons and the writing-system buttons. | Each is announced with its name (B, I, U, Undo, Redo, Picture, Cut, Copy, Paste, en, fr, ...). | | | | |
| 7.3 | W48 only: look for any Avalonia content in the Narrator tree. | **Known:** the Avalonia subtree is empty inside the WinForms host. Record what Narrator or Accessibility Insights shows. | n/a | n/a | | |

## 8. Clipboard with other applications (all hosts)

The playground copies two forms: the field's debug dump (`SIL.TextEditing.Runs.v1`, runs, writing systems and properties, registered
only by the playground and the tests) and plain text. A FieldWorks host also copies TsString and FieldWorks Markdown (ADR 0029).

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 8.1 | Select text in *Latin and Arabic* that crosses the English/Arabic boundary, Copy, paste into Notepad. | The plain text arrives, in logical order, with paragraph breaks as line breaks. | | | | |
| 8.2 | Same, paste into Word. | The text arrives. (Formatting need not.) | | | | |
| 8.3 | Type text in Notepad and in Word (with some Arabic), copy it, Paste in the field. | The text appears at the caret in the active writing system; the Model tab shows it as one run; a selection is replaced. | | | | |
| 8.4 | Select bold and fr text in the field, Copy, move the caret, Paste. | The pasted runs keep their writing systems and bold (compare the Model tab before and after). | | | | |
| 8.5 | *Unknown properties*: copy a run with 9001/9002/9003, paste it into another paragraph. | The properties come with the text, unchanged. | | | | |
| 8.6 | Copy in one host window, paste in another (run `run desktop10` and `run desktop48`). | The runs and properties arrive. | | | | |
| 8.7 | Cut with a selection across paragraphs, then Undo. | The text and paragraph keys return. | | | | |
| 8.9 | Select text, right-click, open *Copy as*, choose *Plain text*, paste into Notepad, then Paste in the field. | Notepad gets the text; the field pastes it as one run in the active writing system, with no formatting. | | | | |
| 8.8 | From a FieldWorks build, if you have one: copy styled text out of a FieldWorks field, paste here, and the reverse. | Record what arrives; FieldWorks does not read our format yet. | | | | |

## 9. Look and feel (all hosts)

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 9.1 | Drag-resize the window quickly; scroll the *Long document* with the wheel and the scrollbar. | No white flashes or tearing; text re-wraps; the caret stays on the same character. | | | | |
| 9.2 | Move the window to a monitor at 150 percent (or change the scale), or set the scale to 150 percent. | Text is sharp; the caret and selection stay aligned with the glyphs; the picture is not blurry. | | | | |
| 9.3 | *Long document*: press Ctrl+End, type, press Ctrl+Home. | The view follows the caret; typing is not slower than in a short document; the Model tab shows a window around the caret. | | | | |
| 9.4 | *Many fields* with 1000 fields (Fields picker, then pick the scenario again): scroll and type in a field far down. | The window stays usable. Record how long it takes to load and whether scrolling stutters. | | | | |
| 9.5 | Select all (Ctrl+A) in the *Long document* and press B. | It finishes in a reasonable time; Undo restores it in one step. | | | | |
| 9.6 | Switch Windows between light, dark and a high contrast theme (Settings, Accessibility, Contrast themes) while the playground is open, with the fields' Foreground and Background not set. | The text and page follow at once, without a pause: in high contrast the page is the system window colour, the text the window text colour and a selection the highlight colour. Record anything unreadable. A field whose host set both colours keeps them (the host's choice, docs/reference/rendering.md, "Colours and themes"). | | | | |

## 10. Mouse and keyboard selection (all hosts)

Scenarios *Latin and Arabic* and *Long document*. Double-click is the system double-click speed; click the same spot.
The word rule: a word is a run of letters, digits and marks (an apostrophe between letters stays in the word, so
`don't` is one word); a run of spaces or of punctuation counts as one unit; there is no dictionary, so Thai and Chinese
are not split into words. Ctrl+Left and Ctrl+Right go by the same rule in logical order, so in a right-to-left paragraph
Ctrl+Left goes to the next word.

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 10.1 | Click in the middle of an English word, then on the left half and the right half of one letter. | The caret appears at the nearer side of the letter; no selection; the Model tab shows `selection none`. | | | | |
| 10.2 | Click at the edge between English and Arabic text, first on one side of the boundary and then on the other. | The caret is drawn on the side you clicked (two different places can be the same offset); typing there continues the text you clicked next to. | | | | |
| 10.3 | Click a word, then Shift+click further on, then Shift+click before the anchor. | The selection grows from the first click, flips when you pass the anchor, and the Model tab's `anchor` never moves. | | | | |
| 10.4 | Drag from the middle of one paragraph to the middle of a later one, then drag back up past the start of the document and down past the end. | Everything between is highlighted, including the whole middle paragraphs; going past the edges selects to the start or the end. | | | | |
| 10.5 | Double-click an English word, an Arabic word, a space and a comma. | The word, the Arabic word, the space run and the punctuation are selected, nothing more. Double-click on the last letter of a word, right half, still picks that word. | | | | |
| 10.6 | Double-click a word and, holding the second press, drag over two more words. | The selection grows a whole word at a time, in both directions. | | | | |
| 10.7 | Triple-click in an English paragraph, an Arabic paragraph, and a wrapped one. | The whole paragraph is selected, over all its lines; holding the third press and dragging adds whole paragraphs. | | | | |
| 10.8 | Click in a long wrapped paragraph, press End, then Home, then End again; then Shift+End. | End puts the caret at the end of the visual line, drawn on that line, not at the start of the next; Home goes to the start of the same line; Shift+End selects to the line end only. | | | | |
| 10.9 | From the End position in 10.8 press Up, Down, Down. | Each key moves one visual line. After a short line the caret returns to about the original horizontal position (the preferred position), not to the short line's end. | | | | |
| 10.10 | Click in a wrapped line just right of its last word. | The caret is drawn at the end of that line, not at the start of the next line. | | | | |
| 10.11 | Ctrl+Right and Ctrl+Left through an English sentence, then through the Arabic paragraph; add Shift. | The caret jumps a word at a time (English: to the start of the next word); at the end of a paragraph the next press enters the neighbouring paragraph; with Shift the selection follows. | | | | |
| 10.12 | Ctrl+Home, Ctrl+End, Ctrl+A, and with Shift for the first two. | The caret goes to the very start and end of the document; Ctrl+A selects everything, and the Model tab shows `selection 0:0 -> last:end`. | | | | |
| 10.13 | Select across three paragraphs with the mouse and press Delete (or Backspace, or type a letter). | One paragraph is left holding the text before and after the selection; its key in the Model tab is the first paragraph's key (unless the first part was empty, then the last paragraph's key). Undo restores all three. | | | | |

## 11. Host hooks: find and replace, context menu, paging (all hosts)

Scenarios *Find and replace* (the find bar under the writing-system bar) and *Long document*. See ADR 0021 and ADR 0025.

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 11.1 | In *Find and replace* type `cat` in Find and press Find Next repeatedly. | Each match is selected in turn and drawn in the current-match colour; with Highlight all ticked every other match is tinted. After the last one the status says it wrapped. Find Previous goes backwards. | | | | |
| 11.2 | Tick Match case, then Whole word; each time repeat 11.1. | Match case skips `Cat` and `CAT`; Whole word skips `concat` and `category` but finds `cat-like`. | | | | |
| 11.3 | Find `cafe` with Match diacritics ticked, then unticked. Find the composed `café` (type it with the accent). | Ticked finds only the bare `cafe`; unticked finds all four spellings; `café` finds both the composed and the decomposed forms, whichever you typed. | | | | |
| 11.4 | Tick Regular expression and find `c[aeiou]t`; then type `(`. | Matches are found; `(` reports that it is not a valid regular expression and finds nothing. | | | | |
| 11.5 | Find `Label` (the read-only label in the last paragraph); type `Tag` in Replace with and press Replace. | The label is selected exactly and nothing is replaced. | | | | |
| 11.6 | Find `mat`, Replace with `rug`, press Replace twice, then Undo. | The first press selects the match, the second replaces it and looks for the next; one Undo brings `mat` back. | | | | |
| 11.7 | Find `cat`, Replace with `catalog`, press Replace All, then Undo. | Every editable match is replaced (the one in the read-only label is not), the new text is not searched again, and one Undo restores everything. | | | | |
| 11.8 | Right-click a word, then a picture, then inside a selection, then just after it. | The log says what the host was told: the word, its range and writing system, the picture's data, the selection. Inside a selection keeps it; elsewhere moves the caret first. A menu opens with the word and no spelling suggestions. | | | | |
| 11.9 | Put the caret in a word and press the Menu key, then Shift+F10. | One request each, for the word at the caret. | | | | |
| 11.10 | In *Long document* press PageDown and PageUp, then with Shift. | The caret moves by what shows in the window and keeps its place on the screen; Shift extends the selection; the log says `scroll requested: Page`. At the ends the caret stops on the first or last line. Ctrl+PageDown puts the caret at the end of the last visible line and Ctrl+PageUp at the start of the first, without scrolling; with Shift the selection extends there. | | | | |
| 11.11 | Drag from the middle of the field to well below its bottom edge, and above its top edge. | The selection grows and the view scrolls a little at each mouse movement (more when far outside); it stops when the mouse stops. | | | | |
| 11.12 | In *Spell checking* double-click `known` in `well-known`, then tick *Hyphen is a letter* and double-click it again; press Ctrl+Right from the start of `well-known`; untick the box. | Unticked the double-click takes `known` and Ctrl+Right stops at the hyphen; ticked the double-click takes `well-known`, Ctrl+Right goes past it and its space, and `well-known` gets a red squiggle (the word list does not have it). Unticking brings the first behaviour and its squiggles back. | | | | |
| 11.13 | In *Spell checking* tick *Slow dictionary, off the UI thread* and type in the last paragraph while the squiggles come back. | The dictionary takes 15 ms a word, but typing is not held up, the squiggles arrive a few words at a time, and none is ever drawn on text that is no longer there. Unticking asks the dictionary on the UI thread again. | | | | |
## 12. Language service box: the environment (all hosts)

Scenario *Environment (language service)*. See ADR 0030. These are the phase 0 questions the headless tests cannot answer: whether the chips read as chips,
whether completion works with an input method or Keyman active, and whether the popup behaves in the WinForms host.

| # | Steps | Expected | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|
| 12.1 | Load the scenario and look at `/ [V] _ #`. | One line. The slash is outlined, `[V]` is a blue chip, `_` a purple chip, `#` an orange one, each behind the plain text; nothing changes in the Model tab's runs. | | | | |
| 12.2 | Type `/ [X] _`. | `[X]` gets a red outline and a red squiggle under the X; the log shows `fwmd.env.unknown-class`. Nothing about the squiggle is in the Model tab. | | | | |
| 12.3 | Rest the pointer on the class `[V]`, then on the red squiggle. | A tooltip: `Natural class V: Vowels.`; for the squiggle, the problem's message. | | | | |
| 12.4 | Press Enter, then Shift+Enter. | Enter: the log says it was passed to the host; Shift+Enter does nothing. The text stays one line. | | | | |
| 12.5 | Clear the box, press Ctrl+Space. | A popup under the caret offers `/`. Down, Up move the highlight; Tab accepts (focus stays in the field) and the text is `/ `. | | | | |
| 12.6 | Type `[`. | The popup opens at once with the classes; typing `+` narrows it; Esc closes it and the text keeps `[+`. | | | | |
| 12.7 | With the popup open press Enter on a class, and click on another class in a second try. Then type a whole environment (`/ [V] _ a`) and press Enter and Tab. | The class replaces the bracket and what was typed; Enter does not reach the host while the popup is open; a click accepts the row clicked. After the `]` the popup is gone, and Enter then reaches the host (the log says so) and Tab leaves the field. In the WinForms host (W48) Enter, Tab and Esc with the popup open go to the popup, not to a form button. | | | | |
| 12.8 | Use the Insert buttons at different carets. | Each is enabled as FieldWorks' Insert menu is (slash only while there is none; bar once; `#` only at the edges; a class or `( )` inside the boundaries); `( )` leaves the caret between the parentheses; each insert is one undo step. | | | | |
| 12.9 | Type `/ [v] _`, right-click the `v`. | The menu shows the problem and *Change to V*; choosing it fixes the text as one undo step. | | | | |
| 12.10 | Click *Drop class V*, then *Restore class V*. | Every `[V]` is marked as unknown after the first and clean after the second, without typing. | | | | |
| 12.11 | Switch to dark: in Windows settings, or by running `Playground.Desktop` with the theme set to dark. Then turn on a high contrast theme. | The chips take their dark styles; in a high contrast theme there are no chips and the text is in the system colours. | | | | |
| 12.12 | With a Keyman keyboard or an input method active, type in the box and press Ctrl+Space. | The popup opens under the caret and does not take the focus; composing text gets no popup and any open one closes. | | | | |
| 12.13 | In the WinForms host (W48) open the popup near the bottom edge of the form. | The popup shows (it may reach beyond the Avalonia control), is left-aligned under the caret, keeps the keyboard in the field and closes on Esc (the form is not cancelled) or when the form loses focus. It does not follow the form if the form is moved while it is open. | | | | |
| 12.14 | With a screen reader on, open the popup and move through it, and rest on a problem. | The field reports its popup as expanded, and its status says the worst problem's message and the selected item; the popup is a list whose items are named with their label and detail. The highlight's change is not announced by itself (an open question in ADR 0030, with the accessibility question Q59). | | | | |

## Record

| | |
|---|---|
| Tester | |
| Date | |
| Windows version | |
| Commit (`git rev-parse --short HEAD`) | |
| Input methods and keyboards installed | |
| Display scale | |
| Screen reader and version | |
