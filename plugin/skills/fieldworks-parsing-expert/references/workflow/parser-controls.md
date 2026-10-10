# Parser menu, trace and approval

**Scope:** FieldWorks 9.3.11 Windows/WinForms, source tag `FieldWorks9.3.11`
(`96da794961d20d82d53719824f3049aa19572533`). Help: official FLEx 9.3 CHM,
`sillsdev/FwHelps` develop `d468f9ca501f421616f622965e674f5f4678c9ce`;
`markdown-export` unavailable. Model vocabulary: `SIL.LCModel 11.0.0-beta0182`.
Help is older than the source tag; source configuration resolves differences. No Avalonia UI claims.
Engine gotcha source check: `sillsdev/machine` at `b9e7db4435c325494bdb2c68ec569cadeb10df23`;
this is documentary engine evidence, not a claim that this revision ships in FLEx 9.3.11.

Use Parser → Choose Parser: Default Parser (XAmple) or the phonological rule-based HermitCrab parser.
The released configuration calls the latter “HermitCrab.NET”; older 9.3 Help wording says “Hermit Crab”.
Do not claim this is a third engine.

Try a Word tests an arbitrary surface and exposes detailed parsing steps. Inspect the proposed
complete morphology, not merely whether any path appears. Parse Current Word works from a word
view; Parse Words in Text works from Interlinear Texts. Parse/Reparse all words runs at lower
priority; a high-priority one-word request can interrupt queue processing. Distinguish queue delay
from one word's engine time.

Reload Grammar / Lexicon makes changed lexical/grammatical content available; use the subsequent
parse command to test it. Stop Parser cancels background work. Edit Parser Parameters changes the
selected engine's settings: inspect the exact installed help and dialog rather than proposing an
engine property as a visible control.

Run Tests can generate a report for the current text, a genre or all texts. **Updates Word Analyses**
controls whether it also changes stored word analyses; the Help says the default is selected.
Try a Word and a report-only test are not equivalent to queue commands that update analyses.
Human-approved, human-disapproved and opinion-unknown readings remain distinct. Colors depend on
Text Glossing versus Parsing Development mode: identify mode and status/tooltip before interpreting
a color. Never use a color alone as a ground-truth label.

A trace that shows reverse rule application must still be followed through synthesis. If a required
stem-label check appears only on synthesis, that is expected; see the
[trace gotcha](../broken/stem-name-affix-requirement-trace-misreading.md).

Primary F08: [Parser menu](https://downloads.languagetechnology.org/fieldworks/Documentation/en/User_Interface/Menus/Parser/Parser_menu_overview.htm),
[interlinear modes](https://downloads.languagetechnology.org/fieldworks/Documentation/en/User_Interface/Menus/Parser/About_Parser_Modes.htm).
F07: [released Words area commands](https://raw.githubusercontent.com/sillsdev/FieldWorks/96da794961d20d82d53719824f3049aa19572533/DistFiles/Language%20Explorer/Configuration/Words/areaConfiguration.xml).
These are authored paraphrases, not copied help pages.
