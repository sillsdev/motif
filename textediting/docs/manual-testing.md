# Manual testing

Only what a person has to do is left here: a real installed Keyman keyboard, real input method candidate windows
(Japanese, Chinese), what a screen reader says, mouse feel, how things look, and other applications' clipboards. Every
row says in the **Why a person** column why a test cannot do it. The rest is automated: the headless suites
(`SIL.TextEditing.Avalonia.Tests` and `SIL.TextEditing.Playground.Tests`, which include the scripted input method) and
the real-window input driver (`SIL.TextEditing.InputDriver.Tests`, which types into the three real hosts and checks Tab
and the popup in the WinForms host). Row numbers are unchanged, so a number quoted in an older note still points at the
same step. You use the **playground**, the shared sample UI that all three hosts show. It is meant for a tester who has
not seen the code.

Write **PASS** or **FAIL** in the Result column of every step you do, and put what you saw in Notes when it differs
from Expected. A step you could not do is **SKIP** with the reason. Copy the Model tab or the event log (select the
text, Ctrl+C) into a bug report; they are plain text and show exactly what the model holds.

## Start

From the repository root on Windows (the .NET 10 SDK and the .NET Framework 4.8 targeting pack are needed):

| Command | What it is | Avalonia |
|---|---|---|
| `run desktop10 -InteractiveDesktop` | Plain Avalonia desktop window on .NET 10: Motif's shape | 12.1.2 |
| `run desktop48 -InteractiveDesktop` | The same window on .NET Framework 4.8 | 11.3.17 |
| `run winforms48 -InteractiveDesktop` | A WinForms form: WinForms TextBox, the playground, WinForms TextBox. FieldWorks' shape | 11.3.17 |

`run <host> -InteractiveDesktop --scenario "Inline picture (ORC)"` opens a scenario directly. The first run builds the host.

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

| # | Steps | Expected | Why a person | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|---|
| 1.2 | Pick each scenario in turn. | Each loads without a delay you notice; the description under the toolbar changes; the Model tab shows the new document; no text is clipped or overlaps. | whether text is clipped or overlaps is judged by eye | | | | |

## 2. Keyboard layout switching on caret moves (all hosts)

The library reports the writing system at the caret; it does not switch layouts yet. This checks what a host would
be told.

| # | Steps | Expected | Why a person | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|---|
| 2.2 | Change the Windows layout with Win+Space while the caret is in an `fr` run. | Nothing happens in the field or the log, and the typed text stays `fr`: switching layouts does not change the writing system yet. Record what you see; this is the gap a future host wiring closes. | needs the real Windows layout switcher (Win+Space) | | | | |

## 3. Real typing and IME (all hosts)

Add the input methods under Settings, Time and language, Language: Japanese (Microsoft IME) and Chinese (Pinyin).

| # | Steps | Expected | Why a person | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|---|
| 3.1 | Type a sentence of English, French accents (AltGr or a French layout) and punctuation. | Everything appears as typed; dead keys compose (`e` after `^`); no doubled or lost letters. | real keyboard layouts, AltGr and dead keys | | | | |
| 3.2 | Japanese IME: in the *Empty* scenario type `nihongo`. | The composition shows inline with an underline; the candidate window sits at the caret; Space cycles candidates; Enter commits the text into the field; the Model tab shows it only after the commit. | needs a real Japanese IME and its candidate window (the model side is `ScriptedInputMethodTests`) | | | | |
| 3.4 | Chinese Pinyin: type `zhongwen`. | Same as 3.2. | needs a real Chinese IME and its candidate window | | | | |
| 3.5 | Compose while the caret is inside Arabic text, then in the RTL paragraph. | The composition appears at the caret, the candidate window is not left behind, the committed text lands at the caret. | where a real candidate window sits beside right-to-left text is seen on screen (the model side is `ScriptedInputMethodTests`) | | | | |
| 3.7 | Move the window while a composition is open. | The candidate window follows the caret. | a real candidate window following a moved window | | | | |

## 4. Keyman (all hosts; needs Keyman Desktop and a keyboard that reorders or deletes, such as Khmer or Tamil 99)

| # | Steps | Expected | Why a person | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|---|
| 4.1 | Select the Keyman keyboard, type a sequence in the *Empty* scenario. | The result matches typing the same keys in Notepad. | needs Keyman Desktop and a real keyboard | | | | |
| 4.2 | Type a sequence in which a later key rewrites earlier characters (reorder, or replace the last syllable). | The earlier characters are changed correctly: the field reports its surrounding text to the keyboard. Check the Model tab for stray characters. | needs Keyman Desktop and a real keyboard | | | | |
| 4.3 | Move the caret into the middle of existing text and type the same sequence. | Same result as at the end of the text. | needs Keyman Desktop and a real keyboard | | | | |

## 5. Right-to-left: caret and selection by mouse and arrows (all hosts)

Scenario *Latin and Arabic*. **Known:** on Avalonia 11.3.17 (D48 and W48) clicking in an RTL paragraph can put the
caret about two characters from where you clicked (fixed in Avalonia 12). Record it either way.

| # | Steps | Expected | Why a person | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|---|
| 5.3 | Click in the middle of an Arabic word. | The caret is where you clicked (D10). On D48 and W48 it may be two characters off: note it (known). | where a real mouse click lands is judged against what is drawn | | | | |
| 5.4 | Click and drag across the boundary between Arabic and English text. | The selection is one visual block or the correct set of blocks for the logical range; the Model tab's `selection a -> b` matches the highlight. | a real mouse drag and the drawn highlight are compared by eye | | | | |
| 5.6 | Scenario *Inline picture*: arrow across the picture in the Arabic paragraph. | The caret passes the picture in one step. (Known: the picture may sit in the wrong place in RTL text on both Avalonia versions.) | how a picture is drawn in right-to-left text is judged by eye | | | | |
| 5.7 | Scenario *Devanagari, Tamil, Thai*: arrow and Backspace through conjuncts and stacked marks. | The caret stops between clusters, never inside; Backspace removes what the language expects (compare with Notepad). | what a reader of the script expects is a human judgement, compared with Notepad | | | | |

## 5a. FieldWorks editing: Backspace, Delete and normalisation (all hosts)

Scenario *NFD, marks, surrogates, PUA* (it sets the field's normalisation form to NFD, as FieldWorks does).

| # | Steps | Expected | Why a person | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|---|
| 5a.5 | With NFD set, copy `é` and paste it into Notepad, then check its form (for instance by Backspace: one press removes the whole `é`). | Notepad receives the precomposed form (NFC). Pasting back into the field inside the same window keeps the stored NFD. | another application (Notepad) reads the clipboard | | | | |

## 6. Tab and focus in and out of the WinForms host (W48)

Tab order is: first WinForms TextBox, the playground field, second WinForms TextBox. The toolbar and the tabs
are not tab stops (click them).

| # | Steps | Expected | Why a person | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|---|
| 6.4 | In the field press Left, Right, Up, Down, Home, End, Delete, Ctrl+C, Ctrl+X, Ctrl+V, Ctrl+Z, Ctrl+Y and Enter. | Each acts in the field; none moves WinForms focus or beeps; Enter makes a new paragraph and does not press a default button. | a beep is heard, not read from a model | n/a | n/a | | |
| 6.5 | Click a toolbar button (Bold), then type. | Focus stays in the field; typing continues. | a real click on the toolbar inside the real WinForms form (no driver test for it yet) | | | | |
| 6.6 | In the Many fields scenario click different fields, then Tab. | The Model tab and `ws:` follow the clicked field; Tab leaves the control rather than walking through every field (note what it does). | exploratory: a person decides whether Tab behaves sensibly | n/a | n/a | | |
| 6.7 | Alt+Tab to another program and back. | The caret returns to the field where it was; typing works at once. | a real switch to another program and back | | | | |
| 6.8 | D10 and D48: press Tab in the field. | Focus leaves the field (the toolbar is not a tab stop), or stays; note which. | exploratory: record which of the two happens | | | n/a | |

## 7. Screen reader (all hosts)

| # | Steps | Expected | Why a person | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|---|
| 7.1 | Start Narrator (Win+Ctrl+Enter), focus the field. | **Known gap:** Narrator reads the field as an edit named "Text field" and not its content; there is no TextPattern. Record exactly what it says. | what a screen reader says | | | | |
| 7.2 | Navigate to the toolbar buttons and the writing-system buttons. | Each is announced with its name (B, I, U, Undo, Redo, Picture, Cut, Copy, Paste, en, fr, ...). | what a screen reader says | | | | |
| 7.3 | W48 only: look for any Avalonia content in the Narrator tree. | **Known:** the Avalonia subtree is empty inside the WinForms host. Record what Narrator or Accessibility Insights shows. | what a screen reader says | n/a | n/a | | |

## 8. Clipboard with other applications (all hosts)

The playground copies two forms: the field's debug dump (`SIL.TextEditing.Runs.v1`, runs, writing systems and properties, registered
only by the playground and the tests) and plain text. A FieldWorks host also copies TsString and FieldWorks Markdown (ADR 0029).

| # | Steps | Expected | Why a person | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|---|
| 8.1 | Select text in *Latin and Arabic* that crosses the English/Arabic boundary, Copy, paste into Notepad. | The plain text arrives, in logical order, with paragraph breaks as line breaks. | another application (Notepad) reads the clipboard | | | | |
| 8.2 | Same, paste into Word. | The text arrives. (Formatting need not.) | another application (Word) reads the clipboard | | | | |
| 8.3 | Type text in Notepad and in Word (with some Arabic), copy it, Paste in the field. | The text appears at the caret in the active writing system; the Model tab shows it as one run; a selection is replaced. | another application (Notepad, Word) writes the clipboard | | | | |
| 8.6 | Copy in one host window, paste in another (run `run desktop10 -InteractiveDesktop` and `run desktop48 -InteractiveDesktop`). | The runs and properties arrive. | two host processes and the real clipboard (the clipboard tests use one process) | | | | |
| 8.8 | From a FieldWorks build, if you have one: copy styled text out of a FieldWorks field, paste here, and the reverse. | Record what arrives; FieldWorks does not read our format yet. | needs a FieldWorks build | | | | |

## 9. Look and feel (all hosts)

| # | Steps | Expected | Why a person | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|---|
| 9.1 | Drag-resize the window quickly; scroll the *Long document* with the wheel and the scrollbar. | No white flashes or tearing; text re-wraps; the caret stays on the same character. | flicker and tearing are seen, not measured | | | | |
| 9.2 | Move the window to a monitor at 150 percent (or change the scale), or set the scale to 150 percent. | Text is sharp; the caret and selection stay aligned with the glyphs; the picture is not blurry. | needs a real display at another scale | | | | |
| 9.3 | *Long document*: press Ctrl+End, type, press Ctrl+Home. | The view follows the caret; typing is not slower than in a short document; the Model tab shows a window around the caret. | how typing feels in a long document | | | | |
| 9.4 | *Many fields* with 1000 fields (Fields picker, then pick the scenario again): scroll and type in a field far down. | The window stays usable. Record how long it takes to load and whether scrolling stutters. | how loading and scrolling feel with 1000 fields | | | | |
| 9.5 | Select all (Ctrl+A) in the *Long document* and press B. | It finishes in a reasonable time; Undo restores it in one step. | whether a wait is reasonable is a human judgement (no wall-clock assertions) | | | | |
| 9.6 | Switch Windows between light, dark and a high contrast theme (Settings, Accessibility, Contrast themes) while the playground is open, with the fields' Foreground and Background not set. | The text and page follow at once, without a pause: in high contrast the page is the system window colour, the text the window text colour and a selection the highlight colour. Record anything unreadable. A field whose host set both colours keeps them (the host's choice, docs/reference/rendering.md, "Colours and themes"). | needs the real Windows themes, including high contrast | | | | |

## 10. Mouse and keyboard selection (all hosts)

Scenarios *Latin and Arabic* and *Long document*. Double-click is the system double-click speed; click the same spot.
The word rule: a word is a run of letters, digits and marks (an apostrophe between letters stays in the word, so
`don't` is one word); a run of spaces or of punctuation counts as one unit; there is no dictionary, so Thai and Chinese
are not split into words. Ctrl+Left and Ctrl+Right go by the same rule in logical order, so in a right-to-left paragraph
Ctrl+Left goes to the next word.

| # | Steps | Expected | Why a person | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|---|
| 10.4 | Drag from the middle of one paragraph to the middle of a later one, then drag back up past the start of the document and down past the end. | Everything between is highlighted, including the whole middle paragraphs; going past the edges selects to the start or the end. | a real mouse drag, and how it feels | | | | |
| 10.5 | Double-click an English word, an Arabic word, a space and a comma. | The word, the Arabic word, the space run and the punctuation are selected, nothing more. Double-click on the last letter of a word, right half, still picks that word. | a real mouse at the system double-click speed | | | | |
| 10.6 | Double-click a word and, holding the second press, drag over two more words. | The selection grows a whole word at a time, in both directions. | a real mouse at the system double-click speed | | | | |
| 10.7 | Triple-click in an English paragraph, an Arabic paragraph, and a wrapped one. | The whole paragraph is selected, over all its lines; holding the third press and dragging adds whole paragraphs. | a real mouse at the system double-click speed | | | | |

## 11. Host hooks: find and replace, context menu, paging (all hosts)

Scenarios *Find and replace* (the find bar under the writing-system bar) and *Long document*. See ADR 0021 and ADR 0025.

| # | Steps | Expected | Why a person | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|---|
| 11.11 | Drag from the middle of the field to well below its bottom edge, and above its top edge. | The selection grows and the view scrolls a little at each mouse movement (more when far outside); it stops when the mouse stops. | how automatic scrolling feels with a real mouse | | | | |
| 11.13 | In *Spell checking* tick *Slow dictionary, off the UI thread* and type in the last paragraph while the squiggles come back. | The dictionary takes 15 ms a word, but typing is not held up, the squiggles arrive a few words at a time, and none is ever drawn on text that is no longer there. Unticking asks the dictionary on the UI thread again. | how typing feels while squiggles come back | | | | |
## 12. Language service box: the environment (all hosts)

Scenario *Environment (language service)*. See ADR 0030. These are the phase 0 questions the headless tests cannot answer: whether the chips read as chips,
whether completion works with an input method or Keyman active, and whether the popup behaves in the WinForms host.

| # | Steps | Expected | Why a person | D10 | D48 | W48 | Notes |
|---|---|---|---|---|---|---|---|
| 12.1 | Load the scenario and look at `/ [V] _ #`. | One line. The slash is outlined, `[V]` is a blue chip, `_` a purple chip, `#` an orange one, each behind the plain text; nothing changes in the Model tab's runs. | colours are judged by eye | | | | |
| 12.2 | Type `/ [X] _`. | `[X]` gets a red outline and a red squiggle under the X; the log shows `fwmd.env.unknown-class`. Nothing about the squiggle is in the Model tab. | a drawn squiggle is judged by eye | | | | |
| 12.3 | Rest the pointer on the class `[V]`, then on the red squiggle. | A tooltip: `Natural class V: Vowels.`; for the squiggle, the problem's message. | a real mouse resting on a word (tooltip) | | | | |
| 12.11 | Switch to dark: in Windows settings, or by running `Playground.Desktop` with the theme set to dark. Then turn on a high contrast theme. | The chips take their dark styles; in a high contrast theme there are no chips and the text is in the system colours. | needs the real dark and high contrast themes | | | | |
| 12.12 | With a Keyman keyboard or an input method active, type in the box and press Ctrl+Space. | The popup opens under the caret and does not take the focus; composing text gets no popup and any open one closes. | needs a real input method or Keyman | | | | |
| 12.13 | In the WinForms host (W48) open the popup near the bottom edge of the form. | The popup shows (it may reach beyond the Avalonia control), is left-aligned under the caret, keeps the keyboard in the field and closes on Esc (the form is not cancelled) or when the form loses focus. It does not follow the form if the form is moved while it is open. | where a real popup lands at the edge of a real form is seen on screen | | | | |
| 12.14 | With a screen reader on, open the popup and move through it, and rest on a problem. | The field reports its popup as expanded, and its status says the worst problem's message and the selected item; the popup is a list whose items are named with their label and detail. The highlight's change is not announced by itself (an open question in ADR 0030, with the accessibility question Q59). | what a screen reader says | | | | |

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
