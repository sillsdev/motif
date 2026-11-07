# A box with a language service: the host explains the text, the field draws and applies it

Owner decisions Q58 and Q61 (2026-11-04): the field takes a host **language service**, shaped like the spell-check host (ADR 0020)
and with data shaped like the Language Server Protocol's, so a box whose text is a notation (the first is FieldWorks' phonological
environment, `/ [V] _ #`) can colour it, mark what is wrong, explain it, complete it and fix it; a later VS Code adapter and agents'
findings can wrap the same interface. The first prototype is the environment box, in the playground (both hosts). The service that
reads a project's real phonology is liblcm-backed and lives in a project of its own, later (open question Q54); this ADR is the
library side only, and no liblcm type is in it.

The rest of this record is **defaults**: how the first version does it, where the owner has not decided. Entries are not owner
decisions and can change without superseding anything (as in ADR 0026).

**What the field does with a service**
- **One interface, `ITextLanguageService`** (the name the scope report suggested), set on `RichTextField.LanguageService`. It is
  one service for the whole field, not one per writing system as the spell host is: a notation box has one meaning. Members:
  `AnalyzeAsync(text)` (tokens and diagnostics together, so an implementation parses once), `CompleteAsync(text, position, trigger)`,
  `HoverAsync(text, position)`, `CanInsert(text, selection, kind)`, `CompletionTriggerCharacters` and a `Changed` event (the spell
  host's `DictionariesChanged`: a natural class added or removed, every field using the service asks again). `TextLanguageService` is a
  base class for a service that answers at once. Optional capabilities are separate interfaces or overrides, not default interface members
  (netstandard2.0 has none).
- **Async by `Task`.** A service in the same process answers with a task that is already done; Motif's backend answers when it can.
  The field never calls from a worker thread. A cancelled task gives no answer; a faulted one, or a service that throws, is reported
  once through `LanguageServiceFailed` (or `Trace`) and the field stops asking until `RestartLanguageAnalysis` or `Changed`.
- **Staleness is the spell checker's rule.** The field asks when idle (its dispatcher at background priority; a seam of its own, `LanguageIdlePost`,
  as spelling has `SpellIdlePost`, for tests) and applies an answer only if the paragraph is the one asked about, at the stamp it had
  (`Paragraph.ChangeStamp`), in the same epoch (a new service or a restart starts a new one; a field that leaves the visual tree cancels what is
  out and asks again when it returns). Otherwise it is dropped and the text asked again; the service is told through its `CancellationToken` that
  it need not go on. After an edit the
  results that lie clear of it are **carried** to where the text moved them, so chips do not flicker while the answer is out; a
  diagnostic carried this way loses its fix, which was made for text that has changed, and a result the edit touched is dropped.
- **Data is the protocol's.** Positions are zero-based line and character, characters in UTF-16 units; ranges are start and end
  (end excluded); severities are the protocol's numbers (error 1 to hint 4); completion kinds and trigger kinds are its numbers too. The
  text a service gets is the field's one paragraph (embedded objects are U+FFFC); the line is 0 unless the text holds CR, LF or CR LF
  (U+2028, the hard line break, is not a line end in the protocol). A field of several paragraphs has no language service at work (a
  later version can send the paragraphs joined by line feeds, the line being the paragraph's index). Token kinds are the host's strings
  (`class`, `segment`, `boundary`, `slot`, `optional`, `punct`, `unknown` for the environment). Diagnostic ids are `fwmd.env.*`.
- **Diagnostics are a view.** They are drawn as squiggles with the spelling marks' drawing (a second list in the layout, so spelling and
  language marks coexist and each is its own) and are never written into the document: the runs, the property bags (no property 5) and
  the undo history are unchanged by them. Error is red, warning blue, information grey; a hint is reported, not drawn. They may overlap.
  liblcm's habit of storing a squiggle in the environment string is not copied.
- **Tokens are chips.** The host gives a `TokenStyleMap`: for each kind a style for the light theme and one for the dark theme, a fill
  and an outline, drawn behind the glyphs from the same rectangles as a selection. The text keeps its colours and runs, so copy, Markdown
  and undo see plain text, and a theme change is a redraw, not a layout. In a high contrast theme no chip is drawn at all and the text
  stays in the system's colours. A kind with no style is not drawn.
- **Hover.** The pointer resting on text sets the field's tooltip: the messages of the diagnostics there (worst first), then the service's
  help, one per line. `GetLanguageHoverAsync(position)` is the same answer for a host that shows its own, and is null when the text
  changed meanwhile. The tooltip's delay is Avalonia's.
- **Completion.** Ctrl+Space, or typing one of the service's trigger characters, asks at the caret. The popup is one Avalonia `Popup`, left-aligned
  under the caret (a native popup window where the platform has one, the overlay layer otherwise), holding plain bordered rows, at
  most eight at a time (the wheel scrolls the window; the row under the pointer is tinted), not a `ListBox`: it needs no theme. In a high
  contrast theme the selected row is in the system's highlight colours. **The popup lives as long as its context**: the thing being completed
  (the stretch the first answer's items replace, else the caret it was asked at). A refresh keeps it only while the new items replace a stretch
  that starts where that began; typing a space or a closing bracket, or deleting back before the start, closes it, as does an empty answer. So
  Enter and Tab reach the host again once the word or class is done. A trigger character typed with the popup open starts a new context and is
  asked as `TriggerCharacter`. The keyboard stays in the
  field and the popup is never focused. Up, Down, PageUp and PageDown move; Enter or Tab accepts (so Enter does not reach
  `EnterRequested` and Tab does not leave the field while it is open); Esc closes (and goes to the host when it is closed); a click on a row
  accepts. Typing asks again once the edit's events are over (`Refresh`); a caret move, a selection, losing focus, a read-only field
  or an input method composing closes it. An item replaces its `ReplaceRange` (else the selection or the caret), as one undo step, and may
  put the caret inside what it inserted (`CaretOffset`: `()` leaves it between the parentheses). If no popup can be shown (a window
  with no theme has no overlay layer) the state and `CompletionChanged` still work, and a host can draw the list itself.
- **UI Automation.** The popup is a `List` and its rows are `ListItem`s in the content view, each named with its label and detail
  (`AutomationProperties.ControlTypeOverride` and `AccessibilityView`, on both Avalonia lines). The field's peer implements the expand/collapse
  pattern (expanded while the popup is open; `Expand` and `Collapse` open and close it) and its item status says the worst diagnostic's message
  and, while open, the popup's size and selected item. ADR 0017's bar for the field itself (name, edit role, value, read-only, bounds) is
  unchanged. A change of the highlight is still not announced by itself (no live region in Avalonia).
- **The host's side of the keys.** A host that routes keys itself (WinForms: `IsInputKey` and `ProcessCmdKey`, with a dialog's default and cancel
  buttons and the Tab order) must leave Enter, Tab, Esc, Up, Down, PageUp and PageDown to the field while its popup is open, and Ctrl+Space
  always. `RichTextField.WantsKey(key, modifiers)` answers exactly that; call it from both overrides. `samples/Host48` does. Without it
  Esc cancels the dialog and Enter presses its default button with the popup on screen. An Avalonia-only host needs nothing.
- **Cancellation, failure and cost.** `CompleteAsync` and `HoverAsync` get a token that is cancelled when the next request, an edit, closing or
  leaving the tree supersedes them. A failed analysis holds off asking about that text only (the next edit asks again) and a service that cancels
  what nobody cancelled is asked three more times for the same text; a failed completion or hover costs that answer only (it is still reported
  through `LanguageServiceFailed`). `RichTextField.LanguageDebounce` (default zero) waits that long after the last edit before asking, for a
  service in another process; the first answer for a new service or text is never held back. `BackgroundTextLanguageService` is a
  `TextLanguageService` whose overrides run on the thread pool.
- **Fixes.** `ApplyDiagnosticFix(diagnostic)` applies the diagnostic's fix as one undo step, only when the answer is for the text as it
  is, the field is not read-only and the range may be edited. Fixes with several edits replace the stretch they cover as one run in the
  formatting of its first character. The context request carries the diagnostics at the place (`ContextInfo.Diagnostics`); the host
  builds the menu, as for spelling (ADR 0021).
- **Insert commands.** `RichTextField.CanInsert(kind)` asks the service about the selection, the way FieldWorks enables its Insert
  menu items; the host types the text (the playground's buttons and context menu do).
- **Single line.** `RichTextField.SingleLine` (`DocumentEditor.SingleLine`) is single-paragraph mode (ADR 0012) and also has no hard
  line break: Shift+Enter does nothing, and a line separator in typed or pasted text becomes a space. A notation box sets it.
- **Both Avalonia lines.** One source tree and no `#if`. The popup could not be tried inside `WinFormsAvaloniaControlHost` here (open
  questions below).
- **Spelling and language marks coexist** (the fourth phase 0 question): they are separate lists in the layout, drawn together; a field can
  have both a spell checker and a language service, and each leaves the other's marks alone (tests in `LanguageServiceFieldTests` and
  `EnvironmentPlaygroundTests`).

**The sample environment service (playground only, not shipped)** matches FieldWorks' behaviour as read in its sources and tests,
written from that behaviour and not from its code (ADR 0010):
- The notation: a slash, a left context, an underscore, a right context, at least one context not empty; a context is items, each a
  segment (one or more phonemes written together; compared in Unicode form NFC, found by trying every split), a natural class in
  square brackets (one to four words, joined by one space whatever the spacing, with an optional caret and one digit after a known
  abbreviation for reduplication) or one of those in parentheses; `#` first on the left or last on the right, once each.
- Empty text has nothing to describe. **One problem is reported, the first from the left**, as FieldWorks reports one: its kinds are
  syntax, unknown class, unknown segment and a missing opening or closing parenthesis or square bracket. An earlier syntax error beats a
  later unknown class. A word followed by `]` is a missing opening bracket whether or not the word is a phoneme.
- The insert rules: the slash while there is none; the underscore after the slash, once, with both ends of the selection after the
  slash; an item (natural class or optional item) after the slash and not wholly outside a boundary that closes the context; a boundary at
  the very start or very end only, at most one on each side. An optional item puts the caret between its parentheses.
- Completion offers the slash, underscore, boundary and `()` where they may go, the classes (all of them after `[`, narrowed as typed,
  replacing a closing bracket that is already there) and the phonemes that start with a word being typed. The natural classes and
  segments are a made-up list.

**Clean room.** The sample's insert rules are written from the observed behaviour as tables (the word boundary rule is written out in
`SampleEnvironment.cs` from the tests alone: the text that results must keep each boundary at an edge, at most one per edge), with `EnvironmentServiceTests` as the oracle. Those tests use
the notation's own examples, several of them in the order and with the phoneme and class names of liblcm's `PhonEnvRecognizerTests`: data
that is behaviour, not code. Only empty text has nothing to describe: whitespace alone is a syntax problem, as in FieldWorks.

**Where the sample deliberately differs** (not owner decisions):
- Messages are written afresh (FieldWorks' wording is LGPL text); a phoneme error underlines from the first letter no split of the word
  can get past to the end of the word, where FieldWorks' position counts every split it tried; the diagnostic range is the offending
  item (the closing bracket's own item, the word, the class name), not a single position.
- A closing parenthesis or bracket with nothing before it is reported as the missing opening one; FieldWorks reports a general syntax
  error. An optional item with nothing in it, or a class with no abbreviation, is a syntax error with its own explanation. A lone `[` is a syntax problem, as in FieldWorks; a class of five words is a syntax problem where FieldWorks says a bracket is missing.
- Exact fixes FieldWorks does not offer: insert the missing parenthesis or bracket; change a class to the one abbreviation that matches
  it but for case; insert a missing leading slash. Nothing is offered where there is no single right answer (an unknown phoneme).

**Open questions** (not owner decisions; none blocks the library):
- Phase 0 gates that need a person (scope report 18, section 5; `docs/manual-testing.md` steps 12.1 to 12.14): do the chips read as chips on a
  real screen in both themes; does completion work with an input method and Keyman active; does the popup place, focus and dismiss correctly
  on Avalonia 11.3 inside `WinFormsAvaloniaControlHost`. Headless tests cover the logic, the keys and the popup in the playground's window.
- Accessibility (owner question Q59, open): a change of the highlight is not announced by itself (Avalonia 11 and 12 have no live region); the
  popup and the field's state are exposed as above, to be checked with a screen reader (manual step 12.14).
- API gaps for a second consumer, to decide before one exists: the trigger character is not passed to `CompleteAsync`; no `isIncomplete`,
  `filterText` or additional edits; one fix per diagnostic; no document version; `CanInsert` is synchronous (a remote service answers from a
  cache); no optional `Constrain` hook (scope report 3.2). The popup does not follow a WinForms form that is moved while it is open.
- A tooltip a host sets on the field itself is replaced while a service is set (the field sets the tip as the pointer moves).
- Fields of several paragraphs (Markdown source mode, a later box): the paragraphs would go to the service joined by line feeds, the line being the
  paragraph's index; not done.
- Where the real service lives, and what it is called (owner question Q54, open).

**Consequences:** the real service (liblcm's `PhonEnvRecognizer` with a project's phonemes and class abbreviations, then the Markdown
checker for source mode) implements `ITextLanguageService` in a project that references liblcm; the field and the interface stay
free of it. Agents' findings, a VS Code adapter and the box can read the same diagnostics, because the data is the protocol's.
