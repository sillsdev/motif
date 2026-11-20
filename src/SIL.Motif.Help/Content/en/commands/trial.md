# Trial a Proposal

A Trial measures a Proposal on a throwaway copy. It records a Dry Run and parser evidence so that you can review changes before deciding what to do.

Run `motif trial --project <fwdata> <proposalId> --wait`, which prints the report summary when the Trial completes (with `--json`, the same summary `motif_trial` returns). Its `gate` is `green`, `loss` or `unmeasured`, and the exit code is 0, 6 or 2 (see [Read Trial progress](cmd:trial%20--wait)); `--expect-loss <file>` lists words you mean to lose. Read the saved report again later with `motif proposal trial --project <fwdata> <proposalId>`; it needs no developer setting. Select named review sets with `--review-set <id,...>`, or use `--review-set none` to omit optional sets. Sets required by `[gate] word-sets` in `<project>.motif.toml` are always included. Word files contain one word per line and are limited to 8 MiB of valid UTF-8 text.

The summary names observed red flags and separately reports incomplete evidence. No red flags means only “not bad”, never “good”. Use `--detail failures` for bounded samples and `--detail full` for pages of all captured cases. Select a section with `--section`, and use its continuation cursor to read another page. With `--case`, choose the `readings`, `expectations`, `memberships` or `sources` section to page that case’s details; `--side` selects before or after readings. `--word-results` is an alias for the first full word page.

Use `--job` or `--report` to select a particular Trial or saved report. `--refresh-report` explicitly produces a new report from retained evidence; a normal read does not rerun the parser. A report can have limitations when the original Trial did not retain a comparable check, set or other evidence.

For one exact case, use `--case <caseKey> --trace --side before` or `--side after`. Tracing may run a diagnostic against the report’s recorded grammar, parser and settings; a missing input requires a new Trial. A diagnostic can differ from the saved batch evidence and does not change the report.

Add `--wait-terminal` when running a Trial to cancel its job at the wait limit or on Ctrl+C. Motif allows up to eight more seconds to record cancellation and confirm cleanup. The refusal includes the job ID, the last observed status and whether cleanup was confirmed.

After Motif's own Apply, the next Trial captures a new Baseline. After a save made outside Motif, measuring pending changes is refused with `change.refresh-required` until you capture a new Baseline with `motif baseline capture`.

A new second reading on an attested word is held for review: `report.heldWords` lists the words and the gate is `unmeasured` until the added reading is independently approved and a fresh Trial captures that approval. The named holds, losses and attributed stops survive the detail budget, including stops with no morphology packet. An undispatched case remains not measured and has no invented per-word stop.

Older saved reports without compact gate facts are explicitly `unmeasured`. Use `--refresh-report` to rebuild named facts from the original retained evidence, or rerun the Trial when those inputs are unavailable. Motif never guesses lost-word names from counts.

Autonomous Apply also requires a [declared gate](cmd:config%20show), the current declaration and exact required word-set scope, a green newest Trial, and unchanged live project and writing-system state.

## Working-set Trials

`motif trial --project <fwdata> <proposalId> --words <file> --no-base-sample --wait` (MCP `no_base_sample`) parses exactly the words in the file and adds no review word set, including the sets `[gate] word-sets` requires. Use it in development loops of up to 500 chosen words; run the ordinary Trial, with its base sample, once per round. It needs `--words`, and it cannot be combined with `--all-words` or `--review-set`.

The report and the saved Trial record `sample: "working-set"` (an ordinary Trial records `stratified`) with `sampleWords` and `wordListDigest`. A working-set Trial is never a gate or a baseline: it has no declared gate, Autonomous Apply and ordinary Apply readiness ignore it, and `difference produce` refuses it. `proposal compare` compares working-set Trials only with each other, over the same `wordListDigest`.

## Foreground-only

`--no-background` (MCP `no_background`) needs `--wait`. A Baseline recapture the Trial needs runs inline, and no Parsimony job is queued after the Trial completes, so nothing commits after the command returns.

