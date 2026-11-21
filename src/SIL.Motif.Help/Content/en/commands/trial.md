# Trial a Proposal

A Trial measures a Proposal on a throwaway copy. It records a Dry Run and parser evidence so that you can review changes before deciding what to do.

Run `motif trial --project <fwdata> <proposalId> --wait`, which prints the report summary when the Trial completes (with `--json`, the same summary `motif_trial` returns). Its `gate` is `green`, `loss` or `unmeasured`, and the exit code is 0, 6 or 2 (see [Read Trial progress](cmd:trial%20--wait)); `--expect-loss <file>` lists words you mean to lose. Read the saved report again later with `motif proposal trial --project <fwdata> <proposalId>`; it needs no developer setting. Select named review sets with `--review-set <id,...>`, or use `--review-set none` to omit optional sets. Sets required by `[gate] word-sets` in `<project>.motif.toml` are always included. Word files contain one word per line and are limited to 8 MiB of valid UTF-8 text.

The summary names observed red flags and separately reports incomplete evidence. No red flags means only “not bad”, never “good”. Use `--detail failures` for bounded samples and `--detail full` for pages of all captured cases. Select a section with `--section`, and use its continuation cursor to read another page. With `--case`, choose the `readings`, `expectations`, `memberships` or `sources` section to page that case’s details; `--side` selects before or after readings. `--word-results` is an alias for the first full word page.

Use `--job` or `--report` to select a particular Trial or saved report. `--refresh-report` explicitly produces a new report from retained evidence; a normal read does not rerun the parser. A report can have limitations when the original Trial did not retain a comparable check, set or other evidence.

For one exact case, use `--case <caseKey> --trace --side before` or `--side after`. Tracing may run a diagnostic against the report’s recorded grammar, parser and settings; a missing input requires a new Trial. A diagnostic can differ from the saved batch evidence and does not change the report.

Add `--wait-terminal` when running a Trial to cancel its job at the wait limit or on Ctrl+C. Motif allows up to eight more seconds to record cancellation and confirm cleanup. The refusal includes the job ID, the last observed status and whether cleanup was confirmed.

After Motif's own Apply, the next Trial captures a new Baseline. After a save made outside Motif, measuring pending changes is refused with `change.refresh-required` until you capture a new Baseline with `motif baseline capture`.

A new second reading on an attested word is held for review: `report.heldWords` lists the words and the gate is `unmeasured` until the added reading is independently approved and a fresh Trial captures that approval. The named holds, losses and attributed stops survive the detail budget, including stops with no morphology packet. An undispatched case remains not measured and has no invented per-word stop.

A Trial that loses words or confirmed readings names them in `report.regressions` (the summary prints them as `Regressed words`). There is one group per cause, `new-word-failures` (no parse, or an Approved reading lost) and `confirmed-readings-lost`. A group lists at most 10 words, each with its reading counts and its analyses before and after (up to three per side; `no analysis` when the word no longer parses), then the count of the rest and a `nextStep` command that pages all of them: `motif proposal trial --project <fwdata> <proposalId> --detail full --section wordforms --flag <cause> --limit 200`. The text summary shows the first two words of each cause on one line (`--json` has all 10), and also stops the `Unexpected losses` and `Expected losses` lists at 10 words; `--json` gives every one. It names the words and what changed for them, not the operation that caused it. A report whose saved word detail cannot be read keeps its summary and carries the limitation `regressions-unavailable` instead.

Older saved reports without compact gate facts are explicitly `unmeasured`. Use `--refresh-report` to rebuild named facts from the original retained evidence, or rerun the Trial when those inputs are unavailable. Motif never guesses lost-word names from counts.

Autonomous Apply also requires a [declared gate](cmd:config%20show), the current declaration and exact required word-set scope, a green newest Trial, and unchanged live project and writing-system state.

## Working-set Trials

`motif trial --project <fwdata> <proposalId> --words <file> --no-base-sample --wait` (MCP `no_base_sample`) parses exactly the words in the file and adds no review word set, including the sets `[gate] word-sets` requires. Use it in development loops of up to 500 chosen words; run the ordinary Trial, with its base sample, once per round. It needs `--words`, and it cannot be combined with `--all-words` or `--review-set`.

The report and the saved Trial record `sample: "working-set"` (an ordinary Trial records `stratified`) with `sampleWords` and `wordListDigest`. A working-set Trial is never a gate or a baseline: it has no declared gate, Autonomous Apply and ordinary Apply readiness ignore it, and `difference produce` refuses it. `proposal compare` compares working-set Trials only with each other, over the same `wordListDigest`.

## Touched words

`--touched` (MCP `touched`) needs `--words` and adds to the list every attested word whose stored Baseline analysis uses an object the Proposal edits, so a change to a stem or affix is measured on the words that carry it, not only on the words you chose. The Proposal's touched objects are each operation's target, the owner a create adds to and the neighbours an ordering is placed against. An entry stands for its lexeme form, alternate forms and grammatical infos, a sense for its grammatical info, and an environment for the allomorphs that use it. Matching is by exact id, never by spelling, against the Baseline Assessment of the current Baseline, so it reaches only the words that Assessment holds and ignores a disapproved analysis. At most 500 words are added; standard error says how many matched and were not added. It is refused with `trial.touched-needs-words` without `--words`, `trial.touched-no-baseline` when no Assessment matches the Baseline, and `trial.touched-no-objects` when no edited object is in the Baseline (a Proposal that only creates new objects touches no existing word). Rules, slots, features, inflection classes and other objects a reading does not name are not matched, and are never dropped silently: standard error (and the MCP tool's next step) names each kind not traced to words with a count and says the touched set may be incomplete, so add `--words` for the words those edits affect. It also says how many Baseline words were searched, and of how many attested when the Assessment covers fewer, so "added 0" is not read as "nothing is touched". If every target is of a kind that is not matched, `trial.touched-no-objects` names them.

## Foreground-only

`--no-background` (MCP `no_background`) needs `--wait`. A Baseline recapture the Trial needs runs inline, and no Parsimony job is queued after the Trial completes, so nothing commits after the command returns.

Add `--verbose` with `--wait` to print the Trial's phase timings (where the time went, and whether the before-side parse was reused) to standard error after the report.

The default wait is 120 seconds (`--wait-timeout-ms 120000`). When it expires the job keeps running, and the command exits with code 3 and the `job.wait-timeout` refusal, which is not a failed Trial: its `jobId` fact names the job, and `motif jobs wait <jobId> --project <fwdata> --wait-timeout-ms <ms>` resumes the wait.
