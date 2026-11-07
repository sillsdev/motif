# Caret movement, deletion and normalisation follow FieldWorks

Where FieldWorks and other editors differ, the field does what FieldWorks does (ADR 0001):
- **Arrow keys are logical**, by paragraph direction. Left moves backwards in a left-to-right paragraph and forwards in
  a right-to-left one, as in FieldWorks, even where the caret then moves the other way on screen inside mixed text.
- **Backspace removes one character** (a surrogate pair counts as one). Typing e plus a combining accent and pressing
  Backspace removes only the accent.
- **Delete removes a character together with the diacritics that follow it.**
- **Typed and pasted text is normalised to the form the host stores.** For FieldWorks that is NFD. Text put on the
  clipboard for other programs is NFC.

**Considered options:** visual arrow movement and whole-cluster deletion, as in Word and browsers. Rejected: those
are design choices, not WinForms limits, so they wait for a separate decision.

Owner decision, 2026-11-03.

Amended 2026-11-04: Markdown import normalises run text to the form the host stores, as pasting does. Attribute
values are never normalised.
