# PanGloss `--cost-column`: read, not used

PanGloss 0.13.0 adds `batch --cost-column`, a `cost` column equal to `work_steps` x 676 + bytes allocated x 2 +
FST registers copied x 268. It estimates instructions better than work alone: the direction of a change matched
instructions 17 of 17 times at a 2 percent change, against 16 of 17 for work, and the correlation is 0.978
against 0.908.

Motif does not read it. The column is refused with `--stats`, appears only in the batch rows, has no split by
rule or kind, and is exact for a word only with `--threads 1`, so using it would cost Motif its per-rule shares
or a second parse. Motif keeps reading `work_steps` as the one number, named **parser work** (ADR 0081).

Revisit when PanGloss's next releases show the final shape of its counters.
