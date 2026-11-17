# Read Trial progress

Waits for a Trial job and reads its state. When the job completes, `motif trial --wait` prints the Trial's review summary; with `--json` it is the same summary `motif_trial` returns. The exit code is non-zero when the Trial measured no words, left required cases unmeasured, or did not finish measuring, for example when the parser was killed. Read the saved report later with [Read a Trial report](cmd:proposal%20trial).
