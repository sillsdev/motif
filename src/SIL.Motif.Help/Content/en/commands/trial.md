# Trial a Proposal

A Trial measures a Proposal on a throwaway copy. It records a Dry Run and parser evidence so that you can review changes before deciding what to do.

Run `motif trial --project <fwdata> <proposalId> --wait`, then read the saved report with `motif proposal trial --project <fwdata> <proposalId>`. Select named review sets with `--review-set <id,...>`, or use `--review-set none` to omit them. Word files contain one word per line and are limited to 8 MiB of valid UTF-8 text.

The summary names observed red flags and separately reports incomplete evidence. No red flags means only “not bad”, never “good”. Use `--detail failures` for bounded samples and `--detail full` for pages of all captured cases. Select a section with `--section`, and use its continuation cursor to read another page. `--word-results` is an alias for the first full word page.

Use `--job` or `--report` to select a particular Trial or saved report. `--refresh-report` explicitly produces a new report from retained evidence; a normal read does not rerun the parser. A report can have limitations when the original Trial did not retain a comparable check, set or other evidence.

After Motif's own Apply, the next Trial captures a new Baseline. After a save made outside Motif, measuring pending changes is refused with `change.refresh-required` until you capture a new Baseline with `motif baseline capture`.
