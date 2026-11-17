# Read Trial progress

Waits for a Trial job and reads its state. When the job completes, `motif trial --wait` prints the Trial's review summary; with `--json` it is the same summary `motif_trial` returns, with a `gate` that a script can exit on.

| Gate | What it means | Exit code |
| --- | --- | --- |
| `green` | Every measured word kept its analysis and its confirmed readings, or its loss was listed as expected, and no word that was analysed before the change was stopped early. | 0 |
| `loss` | A measured word went from analysed to no analysis, or lost a confirmed reading, and the word was not listed as an expected loss. A loss wins over `unmeasured`. | 6 |
| `unmeasured` | The Trial measured no words, left required cases unmeasured, was interrupted, did not finish (for example the parser was killed), or stopped a word by budget or timeout that had an analysis before the change. | 2 |

A word that was stopped but had no analysis before the change does not change the gate. The gate names stopped words that had a Baseline analysis under `stoppedWords`, expected losses under `expectedLosses` and unlisted losses under `unexpectedLosses`. The report also keeps every stopped word in `report.stoppedWords`, including words with no Baseline analysis: each names its before or after side, reason codes and specific parser explanation. For `invalid-evidence`, the explanation identifies the invalid shape or morphology evidence. These diagnostics remain available when the optional detail index is not retained.

The gate reads the Trial's retained evidence, so `--review-set none` does not hide a loss. `--expect-loss <file>` names words, one per line (blank lines are ignored, and a word matches whatever its Unicode normalisation), that the change intends to lose, such as retirements; it needs `--wait`. Listed words do not make the gate a loss; any other lost word still fails. The summary prints whatever the gate. Read the saved report later with [Read a Trial report](cmd:proposal%20trial).

If PanGloss is killed by a signal or terminated by the operating system, the Job is `failed` with `failureCategory` `parser-killed` and `failureCode` `assess.parser-killed`. The waited command exits 2, even when the parser wrote partial or complete-looking output. If there is memory evidence, the category is `out-of-memory` and the code is `assess.out-of-memory` instead. A completed Dry Run and retained partial review evidence remain available; the failed run does not publish a completed Trial.

With `--json`, a terminal Job that could not finish the Trial is printed as one JSON object on stdout, with its status and failure category. Warnings and diagnostics go to stderr. An enqueue refusal is a FailureEnvelope on stderr.
