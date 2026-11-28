# Configuration

The call log (see [call-log.md](call-log.md)) keeps 30 days of daily files. `MOTIF_CALL_LOG` sets its folder, `off` or `full`.

The newest three Trial copies per project are retained for inspection and tracing; older copies and copies of
applied or discarded Proposals are removed. `MOTIF_TRIAL_SCRATCH_MAX_COUNT` sets another positive count.
At runner startup, orphan copies older than 24 hours are removed; set `MOTIF_TRIAL_SCRATCH_MAX_AGE_HOURS`
to a positive integer up to 87,600 to change that age. Active Trials are protected. Set
`MOTIF_RETAIN_TRIAL_SCRATCH=1` in the runner's environment to keep all Trial copies.

Pruning removes this project's unreferenced invocation directories when their Assessments are removed or the
directories are older than 30 days. `MOTIF_STATS_CACHE_MAX_AGE_DAYS` changes the age (positive integer, at most
3,650). Directories whose ownership cannot be proven are kept, and prune reports their count, total size and parent path. Project grammar checks keep the two most recent Baselines; `MOTIF_GRAMMAR_BASELINE_MAX_ROWS`
sets another positive limit. Trial grammar checks keep the newest five rows per Proposal;
`MOTIF_GRAMMAR_CHECK_MAX_ROWS` sets another positive limit. Responses are compressed.

The optional `MOTIF_TRACE=1` JSONL trace rotates at 10 MiB, keeping the current file and one previous file
with a `.1` suffix. `MOTIF_TRACE_MAX_BYTES` selects another positive limit for each file; an individual line larger
than the limit is dropped. `MOTIF_TRACE_FILE` selects the path; otherwise the trace uses a per-user file in the
temporary folder. Multiple processes coordinate writes to the same path.

With `MOTIF_DEVELOPER_COMMANDS=1` set, start the app with `--text-editor-preview` to open a developer-only window that shows the SIL.TextEditing field with sample text.

Motif stages the parser pinned in [`pangloss-release.json`](../pangloss-release.json). Set `MOTIF_PANGLOSS_EXE` to use a different executable. Evaluation data is pinned separately; fetch it with `pwsh ./evals/Get-TestGrammars.ps1` or set `MOTIF_TEST_GRAMMARS` to a checkout at the pin.

The documentation site consumes the Help export. With Node 22.12 or later, export it with `motif help --all --json`, set `MOTIF_HELP_EXPORT` to the export, then run `npm ci`, `npm test` and `npm run build` in `site/`.

## Project-declared Autonomous Apply gate

`<project>.motif.toml` beside the project declares the word sets that must be completely measured by a Trial before Autonomous Apply:

```toml
[gate]
word-sets = ["gold"]

[[review-word-set]]
id = "gold"
name = "Gold words"
role = "regression"
selection = "Gold words"
```

Save the named Selection first. A review word-set JSONL manifest is an alternative: replace `selection` with `manifest = "gold.jsonl"`, resolved relative to the TOML file. A gate needs at least one distinct declared set. No declaration means `apply.gate-undeclared`, with the exact TOML to add in the refusal. Required sets cannot be omitted with `--review-set none`.

This reuses the existing `[gain-gate] mode` (`off`, `advisory`, `hold`) and `min-attested-per-object` (default 1.5), plus `[parsimony] block-on` (default empty). These policies are frozen alongside the declaration and required sets' membership, expectation and source digests. A held gain, opted-in Parsimony hold, new second reading on an attested word, missing required evidence, or unexpected loss prevents green. A fresh Trial after independent approval resolves a second-reading hold; `--expect-loss` only excuses named intended losses. Ordinary Readiness and a person's Apply retain their separate contracts (ADR 0021 and ADR 0022).

Autonomous Apply checks the current declaration and source digests, the newest Trial's revision and Baseline, and the loaded live semantic project state and writing-system file contents under the Apply lease. Changed external state refuses with `apply.trial-gate`; capture a new Baseline and Trial before retrying. Reports without retained compact gate facts are unmeasured; `proposal trial --refresh-report` rebuilds from exact retained inputs when available, otherwise rerun the Trial.
