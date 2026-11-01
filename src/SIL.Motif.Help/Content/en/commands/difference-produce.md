# Compare a Trial with decisions

`difference produce` compares what the parser did with a [Trial](term:trial) against the decisions FieldWorks already records for one Text: approved and disapproved analyses, and words marked as misspelled. It saves the result, so later reads do not depend on the Trial's working files.

Pass the Proposal with `--proposal`, the Text with `--text` and, optionally, a specific completed Trial with `--job`. Without `--job` the newest completed Trial is used. The command reads stored evidence only; it does not run the parser. If the project was captured again after the Trial, it refuses with `difference.baseline-changed` because the recorded decisions may no longer be the ones the Trial was measured against. Trial the Proposal again.

Each word is placed in one state before and after the Proposal: no parse, parse without an approved match, approved match with alternatives, or one approved match. A word with no approved decision shows only how its parses changed. Words that gained, lost or changed readings, or that gained a misspelling or a disapproved reading, are listed as could be bad. Words that gained an approved match or lost a disapproved or misspelled parse are listed as improved. These are advice: they never decide whether a Proposal can be applied.

Every word is counted, but only words that changed or fall in a category are saved as rows, and each before and after pair, category and limitation keeps its best 100 words, those with the most places in the Text first and the spelling breaking a tie. Each saved word keeps at most 10 of its places, the first in reading order; the word's count of places is always the whole Text's. A word that did not change and is in no category is counted and not listed, so the saved comparison stays a few megabytes however long the Text is. `difference show` says how many words are counted but not listed.

By default the decisions are the ones people made in FieldWorks. With `--reference source-validated` the parses are read instead against the decisions a named program staged from cited sources through Applied Proposals. They are not a person's decisions, and the summary says so. If staged agents disagree about an analysis, the word is marked `conflicting-decisions` and neither opinion counts as approved. A typo or ill-formed record makes a word form count as incorrect for this reference only; a person's spelling decision is never changed. Each listed word names the evidence tier and agent behind it; `evidence show` gives the full record. A Proposal keeps one comparison per Text and reference, so a comparison against each sits beside the other; producing again replaces only the one for the same Text and reference.

Only Applied Proposals count; the compared Proposal's own staged decisions never do. An Applied Proposal that cannot be read is named in a warning and left out, and the rest still count. The MCP surface offers this reference only through the `motif_difference_produce_source_validated` tool of the opt-in `source-validated` profile; `motif_difference_produce` always compares with people's decisions.

A token in the Text is counted as unmatched, never guessed, when it cannot be tied to exactly one measured word.

Running the command again for the same Trial, Text and reference returns the saved one, unless the saved word rows can no longer be read, in which case it makes the comparison again; a different Trial replaces it, and comparisons for other Texts or the other reference are left alone. The saved summary names the parser, grammar, Selection and tokeniser of both sides, so it stays readable after the Assessments behind it are removed. A parser or tokeniser that differs between the two sides is shown as a warning, not a refusal.

Once the Proposal is applied, rejected, withdrawn, superseded or deferred, the word list is deleted and only the small summary stays. For an applied, rejected, withdrawn or superseded Proposal the command then returns the saved summary, and refuses with `difference.proposal-decided` if there is none for that Trial. A deferred Proposal is set aside rather than decided, so the command makes the comparison again, which brings its words back until the Proposal next changes status.

```powershell
motif difference produce --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" --proposal <proposalId> --text <textGuid> --json
```
