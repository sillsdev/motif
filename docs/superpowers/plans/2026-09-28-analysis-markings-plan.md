# Building the Round 4 analysis markings

**In plain terms:** the window's Analyze texts page is getting the design the owner approved. Each word becomes a compact FieldWorks-style strip:
- the word, with FieldWorks' opinion mark;
- a line showing what FieldWorks has;
- a line showing what PanGloss finds now;
- one obvious action.

Clicking a word opens a stacked card, and Review changes lists every staged change as "now" above "after Apply". A labelled picture of the word strip, made from a real screenshot of the app, goes into the documentation website.

**Design:** `docs/superpowers/specs/2026-09-28-analysis-markings-design.md`, and the canvas's Round 4 row (`R4-InText`, `R4-Popups`, `R4-Review`, `R4-WordCard-Explained`). Round 3 is the base; Round 4 is the owner's refinement of it. **Decision:** [ADR 0049](../../adr/0049-fieldworks-opinions-and-now-after-apply.md).

## Lanes

These are Luna xhigh implementers, each in its own worktree. Each lane gets an Opus review on claude-work, the lead's full suite, and a merge in `main-lead`.

| Lane | Branch | Builds | Depends on |
|---|---|---|---|
| **M1 · the word model** | `feat/markings-model` | One tested place that turns a word into its strip state: FieldWorks analyses and opinion; PanGloss agreement (same, different, extra, no parse, capped, not assessed), using the single ADR 0027 matcher; the one primary action and its label; the Fix choices; the staged, Uncertain and no-longer-fits overlays. View models only, no views. | — |
| **M2 · marks and components** | `feat/markings-components` | Intent and Component tokens, and small controls: the A/U/D/none mark; the PanGloss line styles; the action chip; the Staged strip; hover- or focus-revealed secondary links; Compact and Normal density. Covered by `ComponentStyleTests` and the token gate. | — |
| **M3 · Analyze texts** | `feat/markings-analyze` | The in-text interlinear strips, the hover summary (which never has buttons), the stacked click card (FieldWorks, then PanGloss, then what to do, as interlinear), the morpheme card, the Fix menu, FW ↗ through the existing `silfw:` link builders, and the "Needs a look" filter. | M1, M2, Uncertain lane 2 |
| **M4 · Review changes** | `feat/markings-review` | Groups by transition, with Now above and After below in each item, Undo and Undo all, Go to text, Show context, and the Uncertain group. | M1, M2, Uncertain lane 2 |
| **M5 · the explained card** | `feat/markings-explained` | A headless capture of the real word strip (see "Screenshot and overlay" below), and a labelled derivative for the website. | M3; the website's capture pipeline |

| **M6 · Matrix** | `feat/markings-matrix` | Matrix cells in the new marks, at Compact density. | M1, M2 |
| **M7 · new operations** | `feat/analysis-remove-accept` | **Remove analysis**: a new operation family meeting the whole definition of done, including a preview of where the analysis is used in texts. Also **Accept the new set as present**, which adds every missing parser analysis as Unknown. Commands and CLI only. | — |
| **M8 · Read and Unread** | `feat/word-read-state` | A word the linguist has looked at is **Read**, and a word nobody has looked at yet is **Unread**. Read is stored in the Motif store, never sent to FieldWorks, and cleared by the same fingerprint rules as a pending change: a new PanGloss result for the word, a change to its FieldWorks analyses or opinion, or a changed sentence. There is a colour, and an Unread filter that replaces "Needs a look". | M1 |
| **M9 · the PanGloss page** | `feat/markings-analyze` (M3) | One short linguist page inside Motif: "PanGloss parses XAmple and HermitCrab grammars fast. Fully compatible." | M3 |

The UI handles **every** operation. That covers approving, disapproving and making one analysis Unknown in the text; adding, accepting a set, removing, undoing and marking a spelling Incorrect for one word, a selection or a whole Text; and Uncertain with Check again.

## Test levels

These follow the test-plan rulings.
- **U:** view models over the fake client, for every state in the spec's §3 and §4 tables.
- **I:** the real `CommandClient` against a seeded LibLCM project, for agreement and staging round trips.
- **S:** one walkthrough per lane through the real startup: choose a word, act, see the strip change, then Review, then Apply.

No instant fake where a wait could hide a bug: hold the fake parser. For S tests, show red evidence before green.

## Screenshot and overlay

- **The capture.** Avalonia headless already renders real frames, through `PageScreenshots` and `CaptureRenderedFrame()`. M5 renders the real word strips at a fixed scale, font, theme and clock, using a synthetic sample: the Round 3 Turkic-style exercise, never project data.
- **The overlay.** Numbered leader lines and captions are drawn from each part's control bounds, which come from its stable `Name` or automation name, not from pixel coordinates. The output is a clean PNG and an annotated one.
- **Where it goes.** The website's research (`docsite-research.md`, on the `feat/generated-help` branch of another session) already plans "annotate from controls". M5 plugs into that pipeline rather than inventing its own format. The owner decides the details in the open questions.

## The owner's answers, 2026-09-28

- **Read and Unread.** "A 'unread' and 'read' status / color / filter makes sense. Should follow same fingerprinting rules." That became lane M8.
- **The explained card** is built on main, by M5, in the website's planned annotate-from-controls shape (option a).
- **"PanGloss" is a window word.** Motif carries a simple, linguist-focused PanGloss page: "can parse XAmple and HermitCrab grammars fast. Fully compatible." That is about all a linguist needs to know. It became lane M9.
- **Scope:** "Do it all. The UI needs to handle all of the operations." That brings in M6 to M8, and every operation in the UI.
