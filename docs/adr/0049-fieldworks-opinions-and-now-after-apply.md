# ADR 0049: FieldWorks' opinion words, and "now → after Apply"

When a linguist changes an analysis, Motif needs to show the opinion FieldWorks records and what changed after Apply. This decision uses FieldWorks’ terms and makes uncertainty visible when the sentence moved.

**Status:** accepted, 2026-09-28. This ADR:
- renames what the window calls a person's judgement on an analysis;
- adds **Opinion**, **Parser agreement**, **Uncertain** and **Remove analysis** to `CONTEXT.md`, and amends **Candidate**.

The design is `docs/superpowers/specs/2026-09-28-analysis-markings-design.md`. It builds on [ADR 0046](0046-pages-not-stages.md), whose pages it marks up, and on [ADR 0027](0027-what-counts-as-the-same-word-analysis.md), whose match rule decides parser agreement.

**In plain terms:** Motif now uses FieldWorks' own words for a linguist's judgement: Approved, Disapproved and Unknown, in FieldWorks' colours. People move between the two programs all day, and one word for one idea saves a translation each time. Each analysis now reads left to right:
- what FieldWorks holds now;
- an arrow to what it will hold after "Apply to FieldWorks project";
- a small note of whether the parser still produces it.

A change is never shown as a new kind of opinion. It is always the step between two.

## Context

The window called a judgement "Rejected" and "Candidate", and it marked a pending change as part of the reading's state. The owner's ruling was: "Let's keep the FieldWorks lingo — the users will be switching between the two." A pending change is a second, separate fact. So is whether the parser agrees with FieldWorks. The owner raised this after the first designs: "it was approved, but the parser doesn't show it now". The canvas compared three layouts:
- **Option A:** two marks side by side;
- **Option B:** the opinion colour on the reading itself;
- **Option C:** "now → after Apply".

The owner chose C, with the letters U, A and D, each with its own glyph as well as its colour.

## Decision

1. **The window says Approved, Disapproved and Unknown.** These are FieldWorks' User Opinion words. The window never says Rejected, Candidate or No opinion. "Candidate" stays the domain term for an analysis nobody has judged, and the window shows it as Unknown.
2. **An opinion is never told by colour alone.** Each one has a glyph and a letter as well as its colour:
   - Approved: a check and A, in cyan;
   - Disapproved: a cross and D, in red;
   - Unknown: a question mark and U, in brown.

   An analysis FieldWorks doesn't hold is a dashed empty outline.

   Red against brown is a documented colour-vision confusion, and this pair carries FieldWorks' two most-confused meanings.
3. **A staged change is the arrow between two opinions, and is never a mark of its own.** "Now → after Apply" reads left to right, with a verb in words: Will add, Will approve, Will disapprove, Will make Unknown, Will remove. Blue is used only for what is staged.
4. **Parser agreement is a third fact, shown separately.** It says whether the current Assessment still produces what FieldWorks holds, using ADR 0027's match. It can be a positive agreement mark, a conflict, or a suggested action. An unfinished, skipped or missing parse is never counted as "the parser doesn't produce it".
5. **Remove analysis is a real deletion,** staged like any other change. Cancelling any staged change is **Undo**. "Remove" no longer means cancel.
6. **A staged decision becomes Uncertain** when Refresh finds that any word in its sentence line has changed. A decision here means an approve, a disapprove or making an analysis Unknown. It stays staged but can't be applied until the linguist reconfirms it or undoes it.
7. **Opinions change one analysis at a time, and only in the text.** Bulk actions (on a selection, or a whole Text) may add, accept a parser set as Unknown, remove, undo, or mark a spelling Incorrect.

## Consequences

- **What changes in the window.** The window's opinion labels, Review changes groups and Matrix cells change. The CLI's words don't change: its readers are agents, and ADR 0046's split between window words and CLI words still holds.
- **Uncertain needs every staged decision to record where it came from:** its Text, paragraph, sentence and word. Refresh must compare the old sentence with the new one before it replaces the stored copy. Neither happens today.
- **Remove analysis needs a delete operation,** which Motif's operation catalog doesn't have yet. It must meet the definition of done for an operation family, including showing where the analysis is used in texts before Apply.
- **Parser agreement needs one matcher.** The change composer and the Assessment evidence each apply ADR 0027's rule separately today, and they must agree exactly.
- **Earlier screens and documents** that say Rejected or Candidate are historical records. They are not counter-examples.

## Amendment, 2026-09-28: Round 3's marks and blue

The owner chose Round 3's compact design over Option C, and ruled on two points of this ADR.
- **Decision 2 is amended.** An opinion mark is its **letter and its shape**, with no glyph: A and D in a square box, U round, and an analysis FieldWorks lacks shown as a dashed box. Each also has its colour and an accessible name. Losing the colour still leaves the letter, which carries the meaning.
- **Decision 3 is amended.** Blue marks what is **new or different**: staged work, a PanGloss reading that differs from FieldWorks, and an extra reading's +N. Links, such as FW ↗, use a separate, darker link colour.
