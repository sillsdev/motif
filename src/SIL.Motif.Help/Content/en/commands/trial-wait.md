# Read Trial progress

Waits for a Trial job and reads its state. When the job completes, `motif trial --wait` prints the Trial's review summary; with `--json` it is the same summary `motif_trial` returns, with a `gate` that a script can gate on.

| Gate | Meaning | Exit code |
| --- | --- | --- |
| `green` | Every measured word kept its analysis, or its loss was listed as expected. | 0 |
| `loss` | A measured word went from analysed to no analysis and was not listed as an expected loss. | 6 |
| `unmeasured` | The Trial measured no words, left required cases unmeasured, or did not finish measuring, for example when the parser was killed. | 2 |

The gate reads the Trial's retained evidence, so `--review-set none` does not hide a loss. `--expect-loss <file>` names words, one per line, that the change intends to lose, such as retirements. They appear under `expectedLosses` and do not make the gate a loss; any other lost word still fails. The summary prints whatever the gate. Read the saved report later with [Read a Trial report](cmd:proposal%20trial).
