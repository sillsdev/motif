# Spell checking and the language service

Both are host services the field asks when idle. Neither writes anything into the document.

## Spell checking (ADR 0020, ADR 0026)

The host gives the field its dictionaries. Set `RichTextField.SpellChecking` to an `ISpellCheckHost` that:

- returns an `ISpellChecker` (`Check`, `Suggest`, `Add`; `ISpellIgnorer` adds `Ignore` when a dictionary has it) for each writing system;
- says which characters form words and which join them;
- raises `DictionariesChanged` when a dictionary changes.

Derive from `SpellCheckHostBase` to write only `GetChecker`: it has the default word rules and `RaiseDictionariesChanged()` (any thread).
The same word rules (`IWordRules`) serve the double-click, Ctrl+arrows and whole-word find; `RichTextField.WordRules` gives them without
spell checking (see [the word rule](editing.md#selection)).

### What the field draws

The field checks the paragraphs in view when idle, a few words at a time, and draws squiggles:

- red: a misspelled word, or a word with an object in it;
- blue: a word that mixes writing systems or has unchecked text inside.

The rules are in `SpellScanner` (UI-free). Words break at characters that are not word-forming. Do-not-check text and text that may not be
edited (unless forced) are skipped. The word of a context request and the squiggled word are one definition (an underscore ends both, as in
FieldWorks).

### What a host's menu calls

`SpellingSuggestionsAt`, `ApplySpellingSuggestion`, `AddToDictionary`, `IgnoreWord` and `RestartSpelling` are what a host's context menu and
dictionary changes call. `ApplySpellingSuggestion` also takes runs, for "insert missing spaces" and, with `WithWholeWordIn`, for "make the
whole word writing system X" in a word with several writing systems.

### A checker that fails

A checker that throws raises `SpellCheckerFailed` (written to `Trace` once per writing system when nothing handles it) and is left alone; it
does not end the application. A host that repairs the dictionary raises `DictionariesChanged` (or calls `RestartSpelling`), which tries that
writing system again.

### Slow dictionaries

A dictionary that is slow can be asked off the UI thread. Its checker also implements `IBackgroundSpellChecker`: `Check` may then run on a
worker, and the field finds the words and applies the answers on its own thread, and drops an answer for a paragraph that has changed since.

`CheckSpellingStep` then returns false while a batch is out, and `IsSpellingComplete` says when everything in view is checked. A loop of
`while (!field.CheckSpellingStep()) { }` on the UI thread therefore never ends with such a checker (the answer is applied by the dispatcher
the loop blocks). Let the dispatcher run and wait for `IsSpellingComplete`, or use a checker without the interface where a synchronous check
is wanted.

The playground's "Spell checking" scenario shows it with a short English and French word list.

### What a host calls after a change

The field restarts checking itself on an edit through the editor, `Apply`, `ExternalReplaceParagraph`, a change of `IsReadOnly` on the
field or its editor, a word added or ignored, and a `DictionariesChanged` notice.

After a host changes a paragraph directly it calls nothing. A paragraph tells the field when any of these change (`Paragraph.ChangeStamp`
moves):

- its runs;
- a run's text, writing system, object data or property bag (replaced, or changed in place with `SetInt`, `SetStr` or a write to
  `IntProps` or `StrProps`);
- its style, direction, default writing system or property bag.

The field then measures and draws again, lays out that paragraph again (and only that one, keyed by the stamp), numbers the list again, drops
the marks of that paragraph and checks it again when idle. A notice from another thread is marshalled to the field's own, but the model is
not thread-safe (ADR 0024): the edit itself must still not overlap a layout, so a host edits on the UI thread.

What a host does call:

- `RichTextField.RefreshFromDocument()` for what a paragraph cannot tell: the list of paragraphs (`Document.Paragraphs`) changed, or a
  paragraph was replaced in it. It restarts checking too.
- `InvalidateAllLayouts()` when a style resolver's answers change (not the resolver itself, which the field takes again when it is set),
  and `RestartSpelling()` when what is checked changed.
- `DictionariesChanged` after repairing a failed dictionary.

Setting `DocumentEditor.IsReadOnly` directly tells automation as `RichTextField.IsReadOnly` does.

## Language service (ADR 0030)

A box whose text is a notation (the first is a FieldWorks phonological environment, `/ [V] _ #`) takes a host **language service**.

`RichTextField.LanguageService` is an `ITextLanguageService` (in `SIL.TextEditing`, no Avalonia or liblcm). It is shaped like the
spell-check host and carries Language Server Protocol data: zero-based line and character in UTF-16 units, ranges, severities 1 to 4,
completion items. So a VS Code adapter and agents' findings can use the same answers.

- `AnalyzeAsync` gives tokens (a kind string and a range) and diagnostics (range, severity, id, message, an optional exact fix) together.
- `CompleteAsync`, `HoverAsync` and `CanInsert` do the rest.
- `Changed` tells every field to ask again.
- Every call that can take time is a `Task`, so Motif's backend can answer from another process.
- `TextLanguageService` is a base class for one that answers at once.

The field serves a field of one paragraph, and `SingleLine` (single-paragraph and no hard line break) is what a notation box sets.

### How answers are applied

The field asks when idle and applies an answer only to the paragraph and stamp it was asked about (as spelling does). After an edit it
carries the results the edit left alone, so nothing flickers.

- **Tokens are chips.** A `TokenStyleMap` (per kind, a fill and an outline for the light theme and the dark one) is drawn behind plain text,
  so copy, Markdown and undo see only text. A high contrast theme gets none.
- **Diagnostics are squiggles**, drawn with the spelling marks' drawing (red error, blue warning, grey information; a hint is not drawn).
  They are never written into the document: no run, bag or property 5 changes.
- Hovering sets the tooltip from the diagnostics' messages and the service's help (`GetLanguageHoverAsync`).
- `ContextInfo.Diagnostics` and `ApplyDiagnosticFix` give the host's context menu a fix as one undo step.
- `CanInsert(kind)` enables a host's Insert commands.

### Completion

Ctrl+Space, or a trigger character the service names, opens a popup left-aligned under the caret.

- The popup is a `List` of named `ListItem` rows: no theme needed, the keyboard stays in the field, the wheel scrolls it.
- It closes when the word or class being completed is done, so Enter and Tab reach the host again.
- Keys: Up, Down, PageUp, PageDown, Enter or Tab (accepts, and neither reaches the host), Esc.
- Typing asks again. A caret move, a selection, losing focus, read-only or an input method composing closes it.
- Where no popup can be shown the state and `CompletionChanged` still work.
- The field's peer is an expand/collapse provider for the popup and gives the worst diagnostic as its item status.

### Keys a host routes itself

A host that routes keys itself (WinForms `IsInputKey` and `ProcessCmdKey`, a dialog's default and cancel buttons) asks
`RichTextField.WantsKey(key, modifiers)` and leaves the keys it wants to the field: Ctrl+Space, and Enter, Tab, Esc and the arrows while the
popup is open. `samples/Host48` shows it.

### Timing and threads

`LanguageDebounce` waits after the last edit before asking, and `BackgroundTextLanguageService` runs the analysis on the thread pool.

### The sample

The playground's **Environment** scenario is the sample (`SampleEnvironmentService`, over a made-up phonology; not shipped). The real
service that reads a project's phonology comes later, in a project that references liblcm.
