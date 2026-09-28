# How the window marks a word's analyses

**In plain terms:** every analysis a linguist sees in Motif answers three questions at a glance:
- what FieldWorks holds now;
- what it will hold after "Apply to FieldWorks project";
- whether the parser still agrees.

Each analysis reads left to right: the opinion now, in FieldWorks' own words and colours, then an arrow to the result of Apply, then a small agreement mark. Every analysis offers the one action that fits its state.

Decided in [ADR 0047](../../adr/0047-fieldworks-opinions-and-now-after-apply.md). It marks up the pages of [ADR 0046](../../adr/0046-pages-not-stages.md), and its matching follows [ADR 0027](../../adr/0027-what-counts-as-the-same-word-analysis.md).

The design canvas, which is private to the owner: https://claude.ai/artifact/EoyG5QPQxU5B4BkhoDTMnt.
- **Options A, B and C** are the first round. C was chosen.
- **Round 2** is C extended with parser agreement, Remove analysis, Uncertain, grouped Review, and Compact density. It is in progress.

This document supersedes the reading marks in the app-shell screens (`2026-09-24-app-shell-screens/04`, `05` and `09`) where they differ. Those screens stay as they are, as the record of that round.

## 1. The owner's rulings

- **The vocabulary is FieldWorks'.** A judgement is Approved, Disapproved or Unknown, in cyan, red and brown. "The users will be switching between the two."
- **"Now → after Apply" was chosen** over two marks side by side (Option A) and colour on the reading (Option B). The owner also chose the U, A and D letters, with glyphs as well as colours.
- **Checkboxes, and "apply this to everything checked".**
- **Remove an analysis completely**, as a staged real deletion.
- **Uncertain after Refresh.** If any word in a sentence line changes in the new `.fwdata`, every staged decision in that line becomes "these are uncertain — we need to check again". A decision is an approve, a disapprove or a make Unknown.
- **Review changes is grouped by state change,** for example "all that were Unknown and are now Approved". Undo works on one item or the whole group. The linguist can go back to the text in Motif, or see the broader context.
- **Density close to VS Code or FieldWorks,** well beyond Word, with Normal and Small sizes.
- **Documentation for linguists** on how it all works.
- **Parser agreement is shown,** with a dotted or half-faded mark and the action beside it. The owner's table:

| FieldWorks now | Parser now | Action |
|---|---|---|
| Nothing | has an analysis | Add analysis |
| Nothing | has none | none |
| Present (Unknown) | the same analysis | Approve one, from a dropdown, only in the text itself |
| Present | a different analysis or set | Accept the new set as present |
| Approved | includes the approved analysis | none; a positive "this looks good" |
| Approved or Present | no matching analysis | none prescribed; show the conflict |
| Spelling Incorrect, or Disapproved | an analysis | none prescribed; show the conflict |
| Spelling Incorrect, or Disapproved | none | none; a positive "this looks good" |

## 2. The marks

| Mark | Meaning | Drawn as |
|---|---|---|
| **A** | Approved | a check and "A", in FieldWorks cyan |
| **D** | Disapproved | a cross and "D", in FieldWorks red |
| **U** | Unknown (a candidate) | a question mark and "U", in FieldWorks brown |
| empty | Not in FieldWorks | a dashed empty outline |
| **→** | a staged change | a blue arrow to the result, with a verb in words |
| agreement | the parser still agrees | a small positive tick beside the reading |
| conflict | the parser disagrees | a small warning mark with a one-line reason |
| parser-only | the parser builds an analysis FieldWorks lacks | a half-faded dashed reading with **Add** beside it |
| Uncertain | a word in the sentence changed | a hatched overlay on the arrow, "Uncertain — check again", and the changed word highlighted |

**Colour rules:**
- Blue belongs only to staged work.
- No opinion is told by colour alone. Remove the colour and a check, a cross and a question mark are still three different shapes. That matters because red against brown is a documented colour-vision confusion.
- Parser-only readings are drawn at reduced contrast, but their text still meets 4.5:1.

**The verbs:** Will add, Will add as Approved, Will approve, Will disapprove, Will make Unknown, Will remove, and Will mark spelling Incorrect. There is no bare "Updated" and no "New".

**Density:**
- **Compact is the default.** Rows are about 22–24 px tall, text is 12–13 px, and marks are 14–16 px.
- **Normal** is a size up.
- Icon buttons show on hover or focus, as in VS Code.
- An analysis's one action is a small text link or chip, not a full button.

## 3. Parser agreement, precisely

- **The same analysis:** the same number of morpheme bundles, in the same order, each with the same Form, MSA and inflection-type identity. A guessed form counts as the same when its text matches after NFD. This is ADR 0027's rule. Glosses, labels, sense and category don't count.
- **The same set:** the search completed, and the parser's distinct analyses equal FieldWorks'.
- **A different set:** the search completed, the parser returned something, and the two sets differ. That includes the case where they only partly overlap. It says the sets differ, nothing more. It never means "replace" or "delete".
- **No parser analysis:** the search completed with zero readings. That is the only way to show absence. A step-limit stop, a timeout, a skipped word or an unassessed word proves nothing.

**Per analysis, with a completed search:**

| FieldWorks holds | The parser builds it | The parser doesn't build it |
|---|---|---|
| **Approved** | agreement: positive | conflict |
| **Disapproved** | conflict | agreement: positive |
| **Unknown** | Approve one is offered, in the text only | conflict |
| nothing (parser-only) | **Add**, as Unknown | — |

**Per word:**
- **Spelling marked Incorrect:** any parser reading is a conflict, and none is a positive.
- **Nothing in FieldWorks, and the parser builds nothing:** no action.
- **A different set:** **Accept the new set as present** adds only the parser's missing analyses, as Unknown. It keeps every stored analysis and opinion. An exact match stored as Unknown can still be chosen for Approve one.
- **An approved analysis with extra parser readings:** the approved analysis stays positive. Each extra reading shows as parser-only, with its own Add. Nothing is accepted automatically.
- **A word holding several opinions** gets one mark per reading. The marks are never merged into a single opinion for the word.

**When the search didn't complete:**
- A reading it did return is still evidence. It can be added, or approved if it matches.
- Missing readings and set comparisons stay **unresolved**, never a conflict.
- There is no set-level Accept.
- The INCOMPLETE badge stays visible.

## 4. Staged changes: now → after Apply

| Action | Now | After Apply | Scope |
|---|---|---|---|
| Add analysis | not in FieldWorks (parser-only) | **U** | one, a selection, or a Text |
| Accept the new set as present | a different set | each missing parser analysis becomes **U** | one word, a selection, or a Text |
| Approve | **U** | **A** | one analysis, in Analyze texts only |
| Disapprove | **A** or **U** | **D** | one analysis, in Analyze texts only |
| Make Unknown | **A** or **D** | **U** | one analysis, in Analyze texts only |
| Remove analysis | **A**, **D** or **U** | removed | one, a selection, or a Text; shows the analysis's uses in texts before Apply |
| Mark spelling Incorrect | spelling not Incorrect | Incorrect (for the word) | one, a selection, or a Text |
| **Undo** | any staged change | the staged change is gone | the same scope as the change |

**Rules for combined changes:**
- An add can carry an opinion. Approving a parser-only analysis stages the add and the approval together, and reads "empty → A", "Will add as Approved".
- Bulk actions stage each distinct target once, never once per occurrence. The target is:
  - for an Add, the wordform and the analysis;
  - for an opinion change or a Remove, the stored analysis;
  - for spelling, the wordform.

**After Apply:**
- A successful Apply clears the staged changes and records a Receipt.
- The old Assessment stays on screen, labelled "Applied · Refresh to see FieldWorks' copy", until the next Refresh. It isn't shown as a new parse.
- If Apply is refused, everything stays staged. Only the changes that no longer fit are marked "FieldWorks changed · Review".

## 5. Uncertain

A staged decision (an approve, disapprove or make Unknown) becomes **Uncertain — check again** when a Refresh captures a new Baseline and the words in its source sentence line differ.

**What counts as a change:**
- Motif compares the ordered words by Text, paragraph, sentence and occurrence, then by wordform and NFD form.
- Inserting, deleting or reordering a word counts, and so does changing one.
- Punctuation and spacing alone don't count.
- If the sentence can't be matched without ambiguity, the decision is Uncertain.

**While it is Uncertain:**
- Uncertain is an overlay on the original transition. It doesn't replace it: the item still reads Unknown → Approved.
- The item can't be applied until it is reconfirmed or undone.
- Reconfirming works only while the change still fits. If the analysis itself moved, the change no longer fits, and that is a separate state.
- Spelling decisions are not covered, by default.

## 6. Review changes, grouped

The groups come in this order:
1. Unknown → Approved
2. Unknown → Disapproved
3. Approved → Disapproved
4. Approved → Unknown
5. Disapproved → Approved
6. Disapproved → Unknown
7. Added as Unknown, from an Add or from accepting a set; each item names its source
8. Removed
9. Spelling → Incorrect
10. Uncertain — check again, where each item also names its transition

**Actions:**
- **Undo** acts on one item, **Undo all** on a whole group.
- **Go to text** opens Analyze texts at the word. For a group, it steps through the group's items.
- **Show context** expands the sentence or paragraph in place.

Items within a group are sorted by Text, sentence and word.

## 7. Selection

- Rows have checkboxes, with a select-all that can be partly checked.
- An "N selected" bar lists only the actions that apply, with counts, for example "Add 7 as Unknown" or "Undo 3".
- A scoped link offers "Select all 42 parser analyses in *Genesis 1*".
- Opinion actions never appear in the bar, and the bar says so: "Approve one analysis at a time, in the text."
- The Apply bar counts by verb, for example "Apply 12 changes to FieldWorks — 7 adds, 3 approvals, 2 disapprovals". It shows each of its states: ready, applying, applied (refresh needed), and refused.
- **Next analysis to review** moves to the next analysis that still needs a decision.

## 8. What has to be built

- **Remove analysis:** a new delete operation, with its own family meeting the definition of done, and a preview of the analysis's uses in texts.
- **Uncertain:**
  - every staged opinion change records where it came from: its Text, paragraph, sentence and word;
  - Refresh compares the old and new sentence words before it replaces the stored Text projection, and keeps the explanation until the change is reconfirmed or undone;
  - the stored projection keeps stable paragraph and sentence identities.
- **Parser agreement:** a single matcher. The change composer and the Assessment's parse evidence each apply ADR 0027's rule separately today, and their NFD handling of guessed forms can differ.
- **The linguist's guide:** "How to read Motif's marks", drafted on the canvas, becomes user documentation beside the window's help.

## 9. Open questions for the owner, with defaults

| Question | Default |
|---|---|
| Does a Disapproved analysis conflict only when the parser still builds that analysis, or whenever the parser builds anything for the word? | Per analysis. Only an Incorrect spelling is word-wide. |
| Should spelling decisions also become Uncertain when the sentence changes? | No, for now. |
| Should Disapprove and Make Unknown, like Approve, happen only in Analyze texts, so each has a sentence to anchor it? | Yes. |
| Should there be an action to set a spelling back to Correct or Undecided? | Not in this round. |
