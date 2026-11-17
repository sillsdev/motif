# Parser and build performance findings

Timings describe the recorded builds and inputs; they are not current performance guarantees.

## Parser work

On 2026-08-06, a release PanGloss build on a 20-core Windows machine loaded a 1 MB grammar and started in 123–147 ms. With a five-second per-word cap, three 40-word samples had mean per-word times of 1 ms, 151 ms and 1,327 ms; the slowest sample timed out on 7 of 40 words. The FST run was 12–19 times faster on the measured harder cases, with no abandoned words; 40 of 40 outcome digests agreed. Compile overhead gave a crossover near 90 words on the fast sample and four on the hard sample. Workload and timeout determine the conclusion.

PanGloss 0.13.0's optional cost column combined work steps, allocated bytes and FST registers. Its measured direction matched instructions 17/17 times at a 2% change, versus 16/17 for work alone, with correlations 0.978 and 0.908 respectively. The source did not record the machine or date. It could not be combined with `--stats`, lacked per-rule shares and required one thread for exact per-word values. Motif uses parser work instead.

## Building a Home and a Trial

On 2026-11-12, a Debug spike on a 20,000-entry log (machine not specified) found a per-entry MSA scan dominating Build: removing the quadratic trigger reduced a single run from 142.5 s to 15.9 s with the same objects and links. The slow operation alone grew from 2.4 s at 2,500 entries to roughly 125 s at 20,000. A batch-creation mode was unnecessary for that cause.

A Trial spike on the same 20,000-entry, 123 MB log (date and machine not stated) measured medians of five runs. Copy took 0.27–0.35 s; committing 5–50 changes 0.03–0.06 s; generating `.fwdata` 98.0–162.7 s; parsing 500 words 3.1–3.6 s. Build noise was 78–174 s, with no demonstrated dependence on Proposal size.

In a WSL2 Release spike on 20 cores and 31 GB RAM with SQLite on ext4 (date not stated), 700,000 links and 60,000 analyses took about 398 s to bulk write, occupied 1,250 MB, and a full sync to an empty peer took 205.3 s with 4,303 MB peak RSS. A 1,000-change incremental sync took 8.2 s. Backup took 2.8 s and file copy 0.9 s. These are observations of the spike, not product limits.

Sources: `docs/research/2026-08-06-parser-timing-measured.md@0697348c8`; `docs/pangloss-cost-column.md@0697348c8`; `docs/source-build/spike-build-speed.md@0697348c8`; `docs/source-build/spike-scale.md@0697348c8`; `docs/source-build/spike-trial.md@0697348c8`.
