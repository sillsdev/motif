# Host-hook choices ADR 0021 left open

ADR 0021 says find and replace, context menus and paging go through host hooks. This records what the field does for each.
Where FieldWorks' behaviour is known (ADR 0001) the field matches it. Where it was not confirmed, the choice is a default,
marked as one, and can change without superseding anything.

## Find and replace

The host's dialog owns the options, the buttons and the wrap prompts. The editor (UI-free) searches, selects and replaces; the
field draws the highlights. `DocumentEditor.TryFind`, `FindAll`, `SelectRange`, `ReplaceMatch`, `ReplaceCurrent` and `ReplaceAll`
take a `FindOptions`; a match is a `TextRange` inside one paragraph.

- **Options that apply to one field's text.** Match case, match diacritics, whole word, a writing system, a character style and
  regular expressions. Not offered: compatibility matching (NFKC; FieldWorks does not implement it either), searching by tags
  and the finding of text across several fields (the host runs one search per field).
- **Matching is ordinal over NFD text, with our own folding.** The pattern and the text are both turned into NFD, so a
  precomposed letter and its decomposed form find each other whatever form the document stores, and offsets are mapped back to
  the document's own text. A match never starts inside a precomposed letter (the accent alone does not find the accent of
  "é" stored precomposed, but does find it in decomposed text) and never splits a surrogate pair. Case is folded per UTF-16
  unit with the invariant culture, and combining marks are removed from the comparison by our own rule, not by the platform's
  culture-aware comparison, because .NET Framework and .NET disagree on those (and the field wants one answer on both).
  **Known gap:** the case folding (`char.ToLowerInvariant`) and the mark test (the runtime's Unicode category tables) are still the runtime's, so the two lines can differ for characters that one runtime's Unicode version has and the other's lacks (U+1E9E, capital sharp s, lowers to a sharp s on .NET and not on .NET Framework, so a search for the one finds the other on one line only). Own fold and mark tables would close it; not done, so this is a default until then.
  This is a default: FieldWorks compares with the writing system's own ICU collation, which the field does not have, so a
  language whose rules treat "ch" or "ñ" as one letter will not match as it does in FieldWorks.
- **Case and diacritics, as FieldWorks' collation strengths do.** Default is to ignore case and match diacritics. Matching case
  also matches diacritics exactly, whatever the diacritics option says. With diacritics matched, a match that a combining mark
  follows is refused ("e" does not find the first letter of "é" in decomposed text). With them ignored, marks are skipped in the
  text and the pattern, and a match swallows the marks that follow it, so the whole letter is selected.
- **A combining mark is a character of general category Mn, Mc or Me.** FieldWorks uses ICU's diacritic and extender properties,
  which .NET does not expose; modifier letters and modifier symbols are treated as letters here. A default.
- **Whole word** uses the field's word rule (`WordBreaks`), the same one as double-click: a match must start and end where a
  word, a run of white space or a run of punctuation starts or ends. A default for the rule (the host's word rule for each character's writing system, ADR 0026); FieldWorks uses the writing
  system's ICU word breaker. Whole word and diacritics are not used with regular expressions (FieldWorks disables them too).
- **Embedded objects are ignorable.** Whole word judges word boundaries on the text without objects too, so "cat", an object and "s" is the word "cats" and not the word "cat". A regular expression sees the text without objects as well. A match may span one (the pattern "abcd" finds "ab", an object and "cd"), and one never
  forms the start or end of a match. Replacing keeps the objects of the match, after the new text, as FieldWorks does. Read from
  FieldWorks' replace, which re-appends the objects of the replaced range; whether its search ignores them was not confirmed,
  so ignoring them is a default.
- **An empty pattern searches by properties.** With a writing system or character style set and no text, each stretch of
  consecutive runs that has them is a match; replacing it with nothing keeps the text and changes only the writing system or
  style (the replacement's own writing system and style, when given). With neither set, an empty pattern finds nothing.
- **Regular expressions** are .NET's, over the NFD text, with case folded unless match case is on. FieldWorks uses ICU's
  syntax, which differs in the corners. A pattern that does not compile matches nothing, and `FindOptions.IsValid` says why. A
  zero-length match is skipped (FieldWorks widens one to a character); a default. Every search of a paragraph has a time limit (`FindOptions.RegexTimeout`, a quarter of a second): a pattern that runs over it finds nothing and `DocumentEditor.FindTimedOut` says so, and the field gives up the highlight of all matches without running the expression again from the paint. A match that starts or ends beside a combining mark, so would cut a letter from its marks, is refused, in every stored form (`\p{L}+` over a word with an accent finds nothing; `\p{L}\p{M}*` finds whole letters); a default. Matching is over decomposed text, so a jamo pattern is compared with the jamo of a syllable; a match that would split a Hangul syllable (a lead jamo alone, or a lead and vowel without the final consonant) is refused too, in the plain search and the regular expression alike, so a composed syllable and the same syllable decomposed give the same results (a pattern of all of a syllable's jamo, or the syllable itself, matches both); a default.
- **Find Next and Find Previous start from the selection** (after its end, before its start), as FieldWorks does. Wrapping is
  the host's to ask for: `TryFind(..., wrap: true, ...)` continues from the other end and says it wrapped. A backward search
  finds overlapping matches a forward one steps over (the forward set is greedy, left to right); a default.
- **Selecting a match selects exactly it**, in read-only text too; it does not move to the nearest editable place the way a
  caret does.
- **Text a line limit hides is not searched**, because a selection cannot reach it (ADR 0024). A default.
- **Replace** needs the whole match to be fully editable: FieldWorks refuses a replacement in read-only and in semi-editable
  text alike, as it refuses formatting there. The replacement takes the properties of the first run of the match; the host can
  give it another writing system or character style. It is normalised to the form the host stores. A line break in the
  replacement becomes a hard line break (U+2028), never a paragraph break; a default. The replacement is one undo step, and
  the caret ends after it; the host then asks for the next match, as FieldWorks' Replace button does.
- **Replace All is one undo step** (one task in FieldWorks too) and replaces the matches from the last to the first, so no
  replacement moves another. Text that was put in is not searched again, so replacing "a" with "aa" cannot loop. The selection
  is kept and moves with the text (FieldWorks restores the old selection without adjusting it for the change in length); a
  default. Matches that cannot be replaced are left, and the count says how many were replaced.

## Context-menu information

The host builds the menu; the field reports what it was asked about. `RichTextField.ContextMenuRequested` carries a
`ContextInfo` (`DocumentEditor.DescribeContext` makes it) and where to open the menu.

- **One request, one event.** The field rides on Avalonia's own context request, which both lines raise for a right click
  (on release, as FieldWorks opens its menu on right-button up) and for the Menu key. Neither line raises it for Shift+F10, so the
  field raises the standard request itself, which gives the event to a host that listens and opens the `ContextMenu` of a host that only sets one. A host builds its menu from the event or sets Avalonia's `ContextMenu` on the field; when it does both the event wins, because the field marks the request handled and the `ContextMenu` does not open (a test pins this). If
  Avalonia ever raises one for Shift+F10 too, a host would see two events for that key; a default to revisit then. A right click in the empty space past the end of a line reports no word and no object. A find match asks the host to show the whole match (`ScrollRequestedEventArgs.Target` is its rectangle), not only its end.
- **Right click inside a selection keeps it; outside it moves the caret there first.** "Inside" means the pointer is over a
  selected character, so a click on the character just after the selection moves the caret. FieldWorks' rule for a click at the
  very edge was not confirmed; a default.
- **What is reported.** The word under the pointer (or at the caret) by the field's word rule, its range and its writing system;
  a copy of the run there with its properties; the embedded object at the place, with its data, its range and whether it is a
  link; the selection and its text; and a list for spelling suggestions, empty until ADR 0020 fills it. Everything is a copy, so
  the host cannot change the document through it.
- **A keyboard request means the caret.** The word is the one after the caret, else the one before it (the caret at the end of a
  word means that word); an object is likewise the one after the caret, else before. A default.
- **A link is an object whose data starts with a hot-link kind.** The first character of object data is the kind FieldWorks
  stores (3 and 5 are hot links to a name and GUID, 4 an external path name); the field reports `IsLink` for those. These are
  persisted facts, like property numbers. How a link is activated stays the host's.

## Paging and scrolling

Scrolling the view that shows the field is the host's (ADR 0021); the field moves the caret and the selection, and asks.

- **PageUp and PageDown move the caret by the field's visible height**, as FieldWorks moves it by the height of its window, and
  keep its place along the line (the caret's own horizontal place, not the remembered one Up and Down keep). Shift extends the
  selection from the anchor. Ctrl+PageUp and Ctrl+PageDown do not page, as in FieldWorks: they put the caret at the top-left or bottom-right corner of what shows, the start of the first visible line or the end of the last, and scroll nothing; with Shift they extend the selection to there. A page move
  stops on the first or last line. With a range selected, the move starts from the end the caret is at; a default (FieldWorks
  measures from the lower end whichever it is). PageUp and PageDown never leave the field and the key is always consumed, even when the field fits (then
  the caret goes to its first or last line): FieldWorks' root site consumes it and its detail tree has no paging of its own, so a slice never hands the key on.
  A host that wants to page its own view from inside a field handles the key before the field gets it.
- **The visible height** is the part of the field that shows: its bounds cut down to the scroll viewers round it, so a field that
  fits moves to its first or last line. A host that knows its view height can set `PageHeight`.
- **The field asks the host to scroll** through `ScrollRequested` (a reason, the part of the field to show in its own
  coordinates, and a distance). A page move carries how far the caret moved, so a host that scrolls by that keeps the caret where
  it was on the screen. When no handler marks the request handled, the field asks the nearest scroll viewer: it scrolls by the
  distance, or brings the part into view. A field with neither a listener nor a scroll viewer works out nothing, which keeps an
  unscrolled field's cost where it was.
- **The caret is asked to be shown after a move the user makes** (keys, a click, typing), with a line of margin, as FieldWorks
  scrolls a selection into view. Not while dragging, which has its own rule.
- **Auto-scroll while drag-selecting** goes per pointer-move event, with no timer, as FieldWorks does: a pointer past the top or
  bottom of what shows extends the selection only to just past the edge (by the scroll distance) and asks to scroll by 10
  pixels when within 30 pixels of the edge and by 30 beyond that. A pointer held still scrolls no further, as in FieldWorks.

## Window reactivation (issue #71)

Checked in real windows on both lines (a field in one window, a second window of the same application shown and activated, then the first activated again
with no click; switching to another application with Alt+Tab was not driven; Avalonia 11.3.17 on net48 and 12.1.2 on net10.0, Win32 backend): the field gets `LostFocus` when its window is
deactivated and `GotFocus` (navigation method unspecified) when it is activated again, so the field already raises
`ActiveWsChanged` on the return (old and new equal), raised nothing while its window was in the background, and a caret change made
meanwhile is reported once, in that event. No activation event is added: FieldWorks does not reset the keyboard when focus returns
without a click, so a host that wants to keep the keyboard the user chose elsewhere ignores the focus-gain event when the previous
focus was not in its own process (README, "Keyboards"), as before. The event arguments cannot tell a reactivation from any other focus
gain (the navigation method is unspecified for both); a host that must tell uses its window's `Activated` event. The report in the
issue of `ActiveWsChanged` firing while another window was active came from headless windows, where activating a second window
leaves the first window's focused control focused; headless activation is approximate and is not a fault of the field.

Written for the host-hook work, 2026-11-03. Entries marked as defaults are not owner decisions and can change without superseding this ADR; the entries not so marked follow FieldWorks' behaviour as read for this work or are facts of Avalonia's behaviour that a test pins.
