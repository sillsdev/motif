# Compare a Trial with decisions

`difference produce` compares what the parser did with a [Trial](term:trial) against the decisions FieldWorks already records for one Text: approved and disapproved analyses, and words marked as misspelled. It saves the result, so later reads do not depend on the Trial's working files.

Pass the Proposal with `--proposal`, the Text with `--text` and, optionally, a specific completed Trial with `--job`. Without `--job` the newest completed Trial is used. The command reads stored evidence only; it does not run the parser. If the project was captured again after the Trial, it refuses with `difference.baseline-changed` because the recorded decisions may no longer be the ones the Trial was measured against. Trial the Proposal again.

Each word is placed in one state before and after the Proposal: no parse, parse without an approved match, approved match with alternatives, or one approved match. A word with no approved decision shows only how its parses changed. Words that gained, lost or changed readings, or that gained a misspelling or a disapproved reading, are listed as could be bad. Words that gained an approved match or lost a disapproved or misspelled parse are listed as improved. These are advice: they never decide whether a Proposal can be applied.

A token in the Text is counted as unmatched, never guessed, when it cannot be tied to exactly one measured word.

A Proposal keeps one comparison per Text. Running the command again for the same Trial returns the saved one; a different Trial replaces it. The saved summary names the parser, grammar, Selection and tokeniser of both sides, so it stays readable after the Assessments behind it are removed. A parser or tokeniser that differs between the two sides is shown as a warning, not a refusal.

Once the Proposal is applied, rejected, withdrawn or superseded, the word list is deleted and only the small summary stays. The command then returns the saved summary, and refuses with `difference.proposal-decided` if there is none for that Trial.

```powershell
motif difference produce --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --proposal <proposalId> --text <textGuid> --json
```
