# Spell-checking rules the earlier ADRs left open

ADR 0020 says the host supplies the dictionaries and the field checks visible paragraphs when idle, draws the squiggles
and applies FieldWorks' skip rules. This records how the field does it. Where FieldWorks' behaviour was read in its
sources (ADR 0001), the field matches it. Where it was not confirmed, or not clear, or the field deliberately differs,
the choice is listed under defaults or deviations and can change without superseding anything. Entries there are not
owner decisions.

**Matches FieldWorks**
- **The host owns the dictionaries.** `ISpellCheckHost.GetChecker(ws)` returns an `ISpellChecker` per writing system, or
  null (that writing system is not checked). The checker is asked `Check(word)` with the word in NFC and, for the
  menu, `Suggest(word)`. A writing system with no checker is skipped, but a word that mixes writing systems is still
  marked.
- **A word is a maximal run of word-forming characters, across runs.** Word-forming is asked of the host per writing
  system (`ISpellCheckHost.IsWordForming`; a host with no opinion returns `SpellScanner.DefaultIsWordForming`: letters,
  combining marks and modifier symbols). Digits always form words, and so does an object character, so "abc123" is one
  word and a word split over two runs of one writing system is one word. FieldWorks' writing systems can make an
  apostrophe or a hyphen a letter; the host says so through `IsWordForming`.
- **Skipped text.** A run whose spell-check property (26) is do-not-check is never checked. A run that may not be edited
  is not checked unless the property says force-check; such a run is cut off the ends of a word.
- **Two kinds of problem word are not spellings.** After the ends are cut, a word with text that is not checked inside
  it, or text of more than one writing system, is `Mixed` (blue) and the dictionary is not asked. A word that holds an
  embedded object next to text is `Embedded` (red; FieldWorks offers "insert missing spaces" for it). Neither offers "Add to
  dictionary".
- **Menu.** The first seven suggestions go in the menu and the rest in an "Additional suggestions" submenu
  (`SpellingSuggestions.MaxInline`, `Inline`, `Additional`); a wrong word with none answers an empty list ("No
  suggestions"), distinct from not being wrong. "Add to dictionary" hands the word to the host's checker
  (`ISpellChecker.Add`), which does its own undo.
- **Insert missing spaces.** For a mixed or embedded word the host calls `SpellingSuggestions.WithMissingSpaces()` and
  `RichTextField.ApplySpellingSuggestion(suggestions, runs)`: a space goes between parts that a dictionary cannot judge
  together, each part keeps its writing system and properties, and an object stays. A string replacement is for a
  misspelling and is refused for an embedded word.
- **Make the whole word writing system X.** For a word with text in more than one writing system, FieldWorks also offers one item per writing
  system that makes the whole word that writing system, keeping each part's other properties; the text does not change, so the dictionary of
  that writing system judges it next. The host builds these items from `SpellingSuggestions.WritingSystems` and `WithWholeWordIn(ws)` and applies
  them with `ApplySpellingSuggestion(suggestions, runs)`.
- **The word under the pointer.** FieldWorks takes it by the writing system's own word-forming test (by default letters, combining marks and
  modifier symbols, and digits) for each character, so an underscore, a hyphen and other punctuation end it, and an embedded object ends the
  word it selects. The field's context request does the same (`SpellScanner.TryWordAt`, with the host's `IsWordForming` while spell checking is on), so
  the word of a request is the squiggled word.
- **Red squiggle, no change to the model.** The marks are drawn like the squiggle underline and are not text properties.

**Defaults (ours; not confirmed in FieldWorks)**
- **Which layers set the spell-check property.** The field reads the paragraph style, the paragraph's own properties, the
  character style and the run's own properties, later layers winning, as FieldWorks' drawing engine layers its
  properties. FieldWorks' menu code disagrees: there the character style overrides the run's own value and the
  paragraph style is not read. The field has one answer for marks and menu and follows the first.
- **Edits made behind the editor's back (issue 101).** A paragraph keeps a change stamp (`Paragraph.ChangeStamp`) that moves when its runs, a run's
  text, writing system, object data or property bag, or its style, direction, default writing system or property bag, change, a bag being changed
  in place (`SetInt`, `SetStr`, a write to `IntProps` or `StrProps`) as well as replaced: a bag tells the runs and paragraphs that hold it. A result
  remembers the stamp it was made for, so a direct change to the model is noticed without the host saying so. The field keys the paragraph's
  format, bullet or number label and layout by it too, so a changed paragraph is laid out again (only that one) and the numbers after it are worked
  out again; and it listens to its paragraphs (a weak link, so a field that is dropped is not kept alive by the document), so it measures, draws and
  checks again by itself, on its own thread when the change was made on another. The marks of the paragraph are dropped. This replaces "call
  `RefreshFromDocument()` after a direct change", which stays for what a paragraph cannot tell: a changed or replaced paragraph in
  `Document.Paragraphs`, and a style resolver whose answers changed (`InvalidateAllLayouts()`, `RestartSpelling()`). A default, ours (not an owner decision). A change of `DocumentEditor.IsReadOnly` raises `IsReadOnlyChanged`, which restarts
  checking, ends a composition and tells automation, whoever changed it.
- **An edit keeps the marks it leaves alone.** A mark outside the edit moves with the text, and only the words around the edit are checked
  again: the stretch the edit touched, widened to whole words and past an apostrophe or joiner next to it (a mark that ends one
  character before the edit stays unless that character joins words). The word being typed has no squiggle until the next idle step; no other
  squiggle blinks. A mark the edit drops is checked again over its own stretch, so an edit that splits a surrogate pair (a host's or a regular
  expression's; the field's own caret never does) cannot lose the mark of a word beside it. A stale squiggle can show beside the edit for one idle
  step: a mark that ends just before the edit is kept unless that character joins words, so when the edit changes what the neighbouring word is (it
  mixes writing systems or unchecked runs now, so it is blue, or is no longer wrong) the old red mark stays until the next step checks that stretch.
  Once the idle work has caught up the marks equal a full recheck. This is ours: FieldWorks checks a changed paragraph again from scratch. An undo, a redo, an external replacement, an edit the
  field was not told the range of (the host's `Apply`) and any edit to a paragraph with a line limit (the edit moves where the limit cuts
  it) check the paragraphs they replace again from scratch. A result is for one state of its paragraph
  (`Paragraph.ChangeStamp`) and is never used for another.
- **Apostrophes and joiners inside a word, switchable.** An apostrophe (U+0027, U+2019) or a zero-width joiner or
  non-joiner (U+200D, U+200C) with word-forming characters on both sides stays in the word, so "don't" is one word.
  FieldWorks has no such rule: there "don't" splits into "don" and "t" unless the writing system's valid characters make
  the apostrophe a letter. The host decides through `ISpellCheckHost.JoinsWords`; `SpellScanner.DefaultJoinsWords` is
  the rule above and a host that returns false for everything gets FieldWorks' behaviour. The rule agrees with the
  double-click word rule. Hyphens end a word ("well-known" is two words).
- **Supplementary-plane letters are word-forming (a deviation).** The field walks code points. FieldWorks walks UTF-16 units, so
  it splits a word at a surrogate pair. The field's answer is better for scripts outside the basic plane, and a host
  that wraps a UTF-16 based writing-system test receives the whole code point.
- **Numbers alone are not asked.** A word of only numbers goes to no dictionary; FieldWorks asks, and relies on Hunspell
  accepting them. "abc123" is asked.
- **All capitals are asked like any word.** No special case.
- **A read-only field checks only forced text.** The field's read-only flag counts as "may not be edited"; changing
  it checks again. Semi-editable text counts as editable.
- **Objects.** An object, or a no-break space (U+FEFF, the character FieldWorks puts before a footnote marker) and an
  object, is not a word to mark: a lone object, an object between spaces and a footnote marker get no squiggle. FieldWorks
  counts the object and U+FEFF as word-forming and asks the dictionary about them, which the field does not. A word with an object
  next to text is `Embedded` and is not asked.
- **One word rule (issue 102).** The double-click, Ctrl+Left and Right, the whole-word find, the word of a context request and the spell checker use
  one rule, as FieldWorks' views use the writing system's rule for all of them: a code point forms words as the host says for the writing system of
  that character (`IWordRules.IsWordForming`; digits always), and a joiner (`IWordRules.JoinsWords`: the apostrophes and the zero-width joiners by
  default) between two word-forming code points stays in the word. An `ISpellCheckHost` is an `IWordRules`. The host can also give the rule with no spell
  checking (`RichTextField.WordRules`, which wins over the spell checker's for everything, the marks included, and starts checking over when set;
  `DocumentEditor.WordRules` for the editor's own context request and find, which a field sets itself, so a host with a field sets the field's);
  with neither the defaults apply. A rule that throws is traced once and the defaults answer instead. `WordBreaks` has overloads that take the paragraph
  (so the writing system of each character is known) and the rule; the string overloads keep the defaults. The classification is the spell
  checker's, over code points; `WordBreaks` then keeps a grapheme cluster whole (the caret's unit) and gives it the class of its first code point. That
  reconciles the two exotic differences the first version had: an apostrophe followed by a combining mark stays in the word for both, and a zero-width
  non-joiner between a word and an apostrophe, or two in a row, no longer joins what follows (a joiner is in the word only between two word-forming
  characters). What is left, on purpose: a trailing zero-width joiner or non-joiner stays in the double-click's word (it is part of the cluster
  before it; the spell checker's word stops before it), and a combining mark with no letter before it to stand on (after a space or a hyphen) stays
  with that character for a double-click while the spell checker, which walks code points as FieldWorks walks UTF-16 units, starts a word at the mark.
  A third difference, from FieldWorks, not ours to have chosen: FieldWorks stops a double-click word at a change of writing system, spell-check property or
  significant style; the field's double-click and Ctrl+arrows cross run boundaries (the spell checker marks such a word blue, `Mixed`). So "one rule" is
  one word-forming rule, not full parity; the parity fix is a separate issue.
  The whole-word find's use of the same rule is ours (FieldWorks' was not read), and so is a zero-width joiner or non-joiner staying with the
  grapheme cluster before it. The spell checker's word goes through an embedded object (FieldWorks widens its menu word the same way) and is
  `Embedded`, so `ContextInfo.Spelling` can reach further than `ContextInfo.Word`. Taking the host's rule for the double-click and Ctrl+arrows
  matches FieldWorks, which uses the writing system's rule there; the cluster handling and the joiners are ours.
- **`SpellingSuggestions.WritingSystems` leaves out an object's writing system.** FieldWorks counts every run's, an object's included; an
  object in another writing system alone makes the word `Embedded`, not `Mixed`, so the field does not offer the whole-word items for it.
- **Insert missing spaces splits at every difference.** FieldWorks puts spaces only where the writing system changes when a word has several, and
  where the spell-check property changes or an object stands otherwise. `WithMissingSpaces()` does all of them at once: a space wherever the
  writing system, the spell-check or editable property differs or an object is next to text, so the result is words a dictionary can judge.
- **Choosing a suggestion keeps the first run's properties.** The string `ApplySpellingSuggestion` replaces the word with a run
  in the word's writing system carrying the first run's properties (FieldWorks' replacement keeps only the writing
  system). Either overload is one undo step of its own, refused when the text can no longer be edited, is hidden by a line
  limit, or is not the word the menu was made for. The caret ends after the replacement.
- **The menu is the host's.** The field offers `SpellingSuggestionsAt(position)` as the spelling part of the context
  information ADR 0021 describes, and a context request carries it: `ContextInfo.Spelling` is the `SpellingSuggestions` object (the host
  needs it to apply, add or ignore) and `ContextInfo.SpellingSuggestions` its strings. `ContextInfo.Word` and the marked word are one definition
  (see above), except that the marked word has no unchecked ends and may have an object in it. A pointer over the space beside a marked word, or over an
  object alone, gets no spelling; a caret at the end of the word does. Building the menu and its wording are the host's.
- **A host base class, and one wrapper per checker.** `SpellCheckHostBase` implements the interface from `GetChecker` alone, with the default
  word rules and a `RaiseDictionariesChanged()` safe from any thread. The field wraps each checker once (it still asks the host every
  time, and makes a new wrapper only when the host answers with another checker), until a restart.
- **Ignore is optional.** FieldWorks' dictionaries have no ignore operation and its menu has no "Ignore". A checker that
  can ignore a word also implements `ISpellIgnorer`; `SpellingSuggestions.CanIgnore` says so and the host offers the item
  only then.
- **The host announces dictionary changes.** `ISpellCheckHost.DictionariesChanged` is raised by the host (any thread) when an
  add, the undo of an add, or a dictionary change alters what its checkers answer; every field using the host checks
  again. A field listens only while it is in the visual tree. The field that asked for an add also checks again itself.
- **When, and where.** Idle steps are bounded by words: a step checks at most 25 words of the first paragraph in view that is not
  checked (the rest of a long paragraph, or what an edit left to check, in later steps), so a slow checker holds up input for at most that many
  words, and a step looks at the text around those words, not the whole paragraph. Only paragraphs in view are checked, and only while the field
  is in the visual tree: the paragraph's top and bottom against the scroll viewer the field is in, else against its window, else against a fixed
  span (2000 device-independent pixels) below its top. FieldWorks checks the whole view, including paragraphs out of sight; the field's choice
  is ADR 0020's, and it is bounded so a long document in a host that scrolls some other way is not checked whole. Scrolling, an edit to a
  paragraph, a new host, `RestartSpelling` and a change of the read-only state make paragraphs due again. Idle means the dispatcher's
  background priority, one request pending at a time; a test replaces the post (`SpellIdlePost`). A host's checker is called on the UI thread, as
  FieldWorks' is; a host that cannot answer quickly answers from a cache and raises `DictionariesChanged` when it has more, or opts in to the next entry.
- **A checker may answer off the UI thread (issue 103).** A checker that is also an `IBackgroundSpellChecker` (the optional capability, as `ISpellIgnorer` is
  for ignoring; adding a member to `ISpellCheckHost` would have broken every host) says that `Check` may be called on a worker thread, and a host whose
  dictionary is slow does not hold up input for even one word. The field still finds and judges the words of a step (at most 25) on its own thread,
  where it reads the model, and sends only the dictionary questions (the checker, the NFC word) to a worker, one batch at a time and one question at a
  time within it. The checker must be fully thread-safe all the same: a context menu or request asks `Check` on the UI thread (through
  `SpellingSuggestionsAt`) while a batch is out, so `Check` can run on the UI thread and a worker at the same time. (The field does not lock around
  it: that would make the menu wait for the batch.) The answers are posted back and applied on the field's thread only if
  the paragraph is the one that was asked about, in the state it had (a restart, a width change that moves a line limit, an edit through the editor
  or a direct change all replace or invalidate it) and at the stamp it had (`Paragraph.ChangeStamp`), in the same epoch; otherwise they are dropped and
  the text is asked again, so no mark is ever drawn for text that is not there. While a batch is out the idle work starts nothing new and does not
  busy-wait (`CheckSpellingStep` returns false, `IsSpellingComplete` is false) and the answer asks for the next step. A batch that is out is cancelled (a flag the worker checks before each word) when the host is replaced or removed, the field leaves the screen or checking starts over: one question already running cannot be interrupted, but no further word is asked, and a new host is not held up by a checker that never returns. A checker that throws on the
  worker is left alone for the rest of the batch (its words taken as right) and is reported through `SpellCheckerFailed` on the field's thread,
  once per writing system as before, unless checking started over meanwhile. A checker without the interface, or a step with none of its words,
  takes the synchronous path exactly as before; a step with some of each asks the others at once and the rest on the worker. `Suggest` and `Add` stay on
  the UI thread (a menu asks one word on purpose, and the checker must be safe to call from the UI thread while a worker is in its `Check`). Defaults,
  ours: a tested seam (`SpellBackgroundRun`, with `SpellIdlePost` for the way back) makes the order deterministic; the real scheduler is the thread pool
  and the field's dispatcher.
- **Menu answers are remembered.** `SpellingSuggestionsAt` looks at the word at the position only, however long the paragraph is, and remembers its
  last answer for the paragraph state, the checkers (until a restart) and the read-only state, so a hover over one word asks the host nothing
  the second time; the checker's suggestions for a word are remembered until a restart too.
- **Hidden text is not checked.** Text a line limit hides is not checked, no menu is offered for it and no replacement is
  applied to it. A word that crosses the limit is checked whole. FieldWorks checks it. A narrower or wider field makes a
  limited paragraph due again.
- **A failing host does not end the application.** An exception from a checker or from the host's word tests is caught in the
  idle step; the field stops checking that writing system (until `RestartSpelling`, a `DictionariesChanged` notice or a new host), takes the failing
  word as right, and raises `RichTextField.SpellCheckerFailed` once with the writing system and the exception. When nothing handles
  the event the failure goes to `System.Diagnostics.Trace` instead (once per writing system until a restart), so it is never silent; a
  handler that throws is traced too. A host that repairs the dictionary raises `DictionariesChanged`. A failure while a menu is made gives
  no suggestions. An exception from `Add` or `Ignore` is the host's to see.
- **The word of a mixed word is NFC.** `SpellingSuggestions.Word` is NFC for every kind; an embedded word keeps its objects.
- **A mark does not cover the input method's composing text.** One that starts where composing text is inserted starts after
  it, one that ends there ends before it, and one with the composing text inside it is not drawn.

Written at the author's request, 2026-11-04; amended the same day after review of the first version.
