# Editing, keyboards and host hooks

How `RichTextField` (in `src/SIL.TextEditing.Avalonia`) edits, and the hooks a host uses. The field does layout with Avalonia's
`TextFormatter`, the caret, selection, hit testing, bidi caret stops, the IME client, and copy, cut, paste and "Copy as" through the host's
clipboard forms ([Clipboard](clipboard.md)). Editing itself is `DocumentEditor`, in the UI-free model.

## FieldWorks editing semantics (ADR 0018)

- **Arrow keys are logical by paragraph direction.** Left goes backwards in a left-to-right paragraph and forwards in a right-to-left one;
  Right is the reverse. Inside mixed-direction text the caret may therefore move the other way on screen. Shift+arrows extend the same way,
  Ctrl+arrows go by word in logical order, Up/Down and Home/End are unchanged. The caret is drawn at the visual place of its logical
  offset (trailing edge of the cluster passed after a forward step, leading edge of the next cluster after a backward step).
- **Backspace** removes one code point (a surrogate pair is one); an embedded object goes whole.
- **Delete** removes one base character and the combining marks (Mn, Mc, Me) after it.
- **Normalisation.** `Document.Normalization` (also `RichTextField.Normalization`) is a nullable `NormalizationForm`, null by default (text
  is inserted as it arrives). A host sets `NormalizationForm.FormD` for FieldWorks: typed text, IME commits and pasted text (plain or rich)
  are converted before insertion and the caret lands after the converted text. Plain text the field puts on the clipboard is always NFC;
  the rich forms keep the stored form.

## Selection

Selection by mouse is done: click with bidi and soft-wrap affinity, Shift+click, drag across paragraphs, double-click word, triple-click
paragraph. So is selection by keyboard: Shift+arrows, Ctrl+arrows by word, Home/End by visual line, Ctrl+Home/End, Ctrl+A, Up/Down with the
preferred x.

- Left/Right and Ctrl+Left/Right are logical. Home/End in a right-to-left paragraph are the logical line start and end.
- **The word rule** (`WordBreaks`) is deliberately simple: runs of letters, digits and marks, with an apostrophe between letters inside the
  word. It has no dictionary, so Thai and Chinese are not split, and it is not UAX #29 word breaking.
- The word rule is also the host's. One rule (`IWordRules`: which code points form words in a writing system, and which join the words on
  each side of them) serves the double-click, Ctrl+Left and Right, the whole-word find, the word of a context request and the spell checker.
  Set `RichTextField.WordRules`, or the rule of `SpellChecking` is used. So a writing system that makes a hyphen a letter makes "foo-bar" one
  word everywhere.
- The double-click (and a drag that goes by words) also stops where the writing system or the spell-check property changes, and where two
  different character styles, resolved by the host's `IStyleResolver`, differ in either. A style that only changes bold or colour keeps the
  word, and with no resolver a style name never splits it. This is what FieldWorks does, so a word that runs across such a change is
  selected a piece at a time. The other uses of the rule take the whole word.

## Block features (ADR 0033)

Which of **tables, lists and headings** a field may hold is `RichTextField.BlockFeatures` (forwards to `DocumentEditor.BlockFeatures`), a
`BlockFeatures` flags value (`None`, `Tables`, `Lists`, `Headings`, `All`) beside `SingleParagraph`. The default is `All`, so nothing changes
until a host turns a kind off (owner decision Q79). When a kind is off:

- **Commands that make it are refused.** The editor has no style command (the host applies a paragraph style as an undoable delta), so the
  host asks `RichTextField.IsParagraphStyleAllowed(styleName)` before it applies one and hides what is off. The playground has a switch for
  each kind, hides the styles of a kind that is off in its style box and refuses them in `ApplyParagraphStyle`.
- **A paste, and so a Markdown paste, flattens it.** A table becomes one plain paragraph per row with the cells joined by a space, a bar and
  a space (`a | b`), a list item or a heading becomes a plain paragraph, and `RichTextField.BlocksFlattened` tells the host how many of each
  (the same way `WritingSystemsMapped` does). Blocks the document already holds are kept, undo restores them exactly, and Enter in one
  starts a plain paragraph.
- **How a paragraph is a table, list or heading is decided in one place**, `IBlockRecognizer` (`RichTextField.BlockRecognizer`). The
  default, `StyleBlockRecognizer`, goes by the host's paragraph style names; `MarkdownStyleNames.ToBlockRecognizer()` makes one from the
  names the Markdown reader uses (a host that pastes Markdown gives the field the recogniser made from its Markdown options). Today's table
  is a tab between cells in a paragraph of a table style; a later storage form is another recogniser.

A **record schema's** rich field has the same flags (`Rich(name, paragraphs, ws, blocks)`): `All` for a field of paragraphs and `None` for a
field of one paragraph. `RecordFileReader` reports `fwmd.record.block-not-allowed` for each disallowed block, with a fix that flattens it as
paste does, and the JSON Schema carries `x-fieldworks-blocks` where a field restricts them
([record files](../formats/record-files.md#block-features)). The defaults computed from liblcm (all on for a multi-paragraph field, off
for `Text.Contents` and Scripture) belong to the planned `SIL.TextEditing.Project`; see ADR 0033.

## Keyboards (ADR 0008)

The field never switches keyboards; the host does. Subscribe to `RichTextField.ActiveWsChanged` (old and new writing system) and switch the
OS keyboard or Keyman to the new one.

- The field raises it only while it has focus, as FieldWorks' views set the keyboard only with focus. A change made while it is unfocused
  is not reported; the focus-gain event below carries the result.
- It is raised once per change, never when the writing system stays the same:
  - when the caret moves into text of another writing system;
  - when a selection's first writing system changes (a selection takes the writing system of the first text it covers, as in FieldWorks);
  - when an edit or undo leaves the caret in another one;
  - when the host or the writing-system toolbar calls `Editor.SetActiveWs`.
- It is also raised, with old and new equal, when the field gains focus, so the host can switch to the keyboard of the writing system at
  the caret even though nothing changed in this field.

### Focus in and out

What FieldWorks does, and what a host does to match:

- On Windows, FieldWorks sets the keyboard on focus gain only when focus comes from a window of the same process. So a keyboard the user
  picked in the tray or in another application is kept when focus returns without a click (Alt+Tab, the tray).
- A click into a view makes a selection, and the selection sets the keyboard for it, so a click does set the keyboard.
- On Linux FieldWorks sets it on every focus gain.
- When a view loses focus to a control elsewhere in the same application that is not a view, it restores the default keyboard. Leaving the
  application is left to the operating system.

The field cannot tell where focus came from: Avalonia's `GotFocusEventArgs` gives the navigation method and key modifiers, not the source.
So it raises the focus-gain event every time and the host decides: switch only if the previously focused window was its own, otherwise
leave the keyboard alone. The field's event follows changes of writing system, so a click into text of the same writing system gives no
event of its own.

To restore the default keyboard, subscribe to the field's `LostFocus` and switch when focus has moved to a control of your own application
that is not a field. The field raises nothing when it loses focus.

Avalonia gives the field `LostFocus` when its window is deactivated and `GotFocus` when it is activated again, on both lines. This was
checked in real windows with a second window of the same application; switching to another application was not driven. So a reactivation
gives one focus-gain event. The event arguments cannot tell that from any other focus gain, so a host that must tell uses its window's
`Activated`.

## Host hooks (ADR 0021, ADR 0025)

The field owns one text; the window around it belongs to the host. These are the hooks:

- **Find and replace.** The host's dialog builds a `FindOptions` (text, match case, match diacritics, whole word, regular expression,
  writing system, character style). `Editor.TryFind` searches from the selection (next or previous, optionally wrapping), `Editor.FindAll`
  lists the matches, `Editor.ReplaceMatch`, `ReplaceCurrent` and `ReplaceAll` replace as single undo steps. `RichTextField.Find` and
  `SelectMatch` also select the match, mark it as the current match (`CurrentMatch`) and ask the host to scroll; `SetFindHighlight`
  highlights every match while the dialog is open. Text is compared as NFD, so composed and decomposed forms find each other.
- **Context menu.** `RichTextField.ContextMenuRequested` fires for a right click, the Menu key and Shift+F10 with a `ContextInfo`: the word
  and its range, its writing system, the run and its properties, any link or embedded object, the selection and an (empty, for now) list of
  spelling suggestions. The host builds the menu. Use the event or Avalonia's `ContextMenu` on the field; with both, the event wins.
- **Paging and scrolling.** PageUp and PageDown (with Shift) move the caret by the field's visible height (`VisibleBounds`, or `PageHeight`
  when the host sets it); a drag past the edge extends the selection and asks for scrolling. The field never scrolls anything itself: it
  raises `ScrollRequested` (reason, the part to show, the distance) and a scroll viewer round the field answers when no handler marks it
  handled. Scrolling inside a long field is therefore the host's.
- **Keys a host routes itself.** See [the language service](spelling-and-language-service.md#keys-a-host-routes-itself) for
  `RichTextField.WantsKey`.
