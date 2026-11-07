# Rendering

What the field draws, and how. For the per-version Avalonia faults the layout works around, see
[Avalonia versions](avalonia-versions.md#avalonia-faults-pinned-by-tests).

## Paragraph and character properties

Drawn, with the host's `IStyleResolver` supplying named styles (ADR 0004).

**Character properties:**

- weight (any 100 to 900, invert), italic;
- foreground and background colour;
- underline type and colour: dotted, dashed, single, double, strikethrough, and the squiggle, which the layout draws itself as a zigzag
  below the baseline;
- superscript and subscript: sized, and raised or lowered, as the font's own OS/2 table says, with FieldWorks' two thirds where it gives
  nothing. The ratios are applied to the size of the text beside it, which is a default;
- font size (absolute, relative, named), font family and per-run font features;
- baseline offset: drawn by drawing the run ourselves, shifted. The line's slot makes room for it and the caret, selection and squiggle
  follow it.

**Paragraph properties:** alignment, first-line and hanging indents, margins, space before and after, line height, paragraph colour,
borders and padding, a line limit, bullets and numbers.

Pagination properties and the rest are carried untouched. Lengths in points are drawn as device-independent pixels, as font sizes are.

A writing system the host did not register (`RichTextField.WritingSystems`) is drawn in Segoe UI at 10 point, FieldWorks' own default size.

## Justified paragraphs

A justified paragraph is stretched by the field itself (Avalonia's justifier is internal).

- Each line but the last shares its slack equally among its interior spaces, so it reaches both edges of the text area.
- The last line stays at the leading edge, as in FieldWorks.
- The caret, selection, mouse and run backgrounds follow the stretched text.
- Each stretched line is drawn piece by piece, with its underlines and strikethrough drawn by the field as lines across the whole stretched
  run, so a dotted or dashed pattern is not restarted or scaled.

## Line limit

Text a line limit hides stays in the model but cannot be reached.

- The caret, Right, End, Up/Down and mouse stop at the end of the last line shown, as FieldWorks puts an insertion point in truncated text
  at the last visible position.
- Typing, Delete, Enter, paste and composition are refused at that point, and Backspace at the start of the next paragraph does not join
  it onto the hidden end.
- Select All and Copy still take the hidden text, because the selection model holds it.
- As in FieldWorks, the last visible line of a cut-off paragraph ends with "..." in the paragraph style's character format, on the trailing
  side of the text (left of it in a right-to-left paragraph). Room is kept for it when the text area is wide enough for one beside text.
- The caret and hit testing stay on the text, and a hard line break that ends the paragraph is not cut-off text.

## Spaces a wrapped line ends with

They hang past the line's text, outside the alignment (a line is aligned by its text). The caret across them moves one space width per
space, as in FieldWorks, even past the text area.

- The control draws nothing outside itself, so a caret that would be there is held at the control's edge: the End caret at the edge, the
  carets of the spaces before it 2 px apart inside it.
- When the text itself reaches the edge (a justified line, a right-aligned one, a line that just fits) there is no room for those carets to
  show apart: they crowd on the text's last letters.
- A host that wants each caret seen gives the text area room on the side the spaces hang to, with the paragraph's trailing indent
  (property 20: the side lines end on, in either direction) of a few spaces' width. A margin around the control does not help, as the field
  cannot draw there.
- The mouse follows the carets as they are drawn: a click at one gives it, and a click beyond the End caret gives the End caret.

## Very long paragraphs

Runs that look alike are one stretch of text to Avalonia's formatter, and a paragraph's ranges are found by halving, so a layout grows with
the runs.

- Avalonia's formatter itself (about nine tenths of a layout's time, on both versions) carries what is left of the text from each line to
  the next, so a paragraph of tens of thousands of runs costs more than its length: 64,000 runs lay out in about 2.0 s on 11.3 and 1.6 s
  on 12.
- From 16,000 runs (after runs that look alike are one), a paragraph of Latin text (letters, their marks, punctuation) that is not right to
  left is formatted line by line from a window of its text instead (`ParagraphLayout.FormatWindowed`). It gives the same lines and carets,
  but for the kerning of a pair across a line start (widths move by a fraction of a pixel), in about 1.1 s on 11.3 and 0.46 s on 12 for
  those 64,000 runs.
- A paragraph with any other script is formatted whole, as the directions of right-to-left text are resolved for all of it at once and a
  neutral character at a line start keeps the fallback font of the text before it (CJK, Thai).

## Graphemes

`GraphemeClusters` is `StringInfo` plus Indic conjuncts (GB9c, six scripts; .NET 10 lacks it).

- The netstandard2.0 build also fuses Extend, SpacingMark, emoji ZWJ sequences and flags per UAX #29, with approximated property tables.
- Caret stops in the field come from the shaper, except on a right-to-left paragraph's line of several runs (`LineInfo.UsesRuns`). There
  the layout steps by this class's grapheme boundaries and measures each on the run's glyphs (with the approximated tables on
  netstandard2.0).

## Colours and themes

Until the host sets `Foreground` (text, and a border with no colour of its own) and `Background` (the page), the field takes them from the
theme:

- the text colour the caret uses and the theme's window colour;
- in a Windows high contrast theme, the system's window text and window colours, as FieldWorks does;
- the selection takes the system highlight, and selected text the system's highlight text colour, whatever colour its runs have.

The text keeps one default brush that the field changes in place when the theme or the system's colours change, so a theme switch redraws
the text and does not lay out the paragraphs again.

**The contract.** A colour the host sets is kept as it is under every theme, high contrast included. So a host that sets one sets the other,
or neither: a host that sets only `Foreground = Black` on a dark theme gets black text on the theme's dark page. `ResetForeground()` and
`ResetBackground()` go back to following the theme. A run's own colours (text property 8 and 9) are the document's and do not change with
the theme.
