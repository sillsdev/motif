# Check the Grammar Guide against the grammar

`guide check --project <fwdata> [--no-run | --build] [--timeout <seconds>] [--json]` reads the Grammar Guide of the current Baseline and reports what is out of step with the grammar: rules without a section, links to deleted objects, example words that no longer parse as their section says, and problems in the Guide's query blocks. Every finding is advisory; none blocks a Proposal, Apply or Trial.

Each fenced `motif-query` block in the Guide is run read-only, through the same evaluator the window uses, for both `motif query` lines and `motif query corpus forms` lines. A block that is refused, failed or cold is a finding, never a zero. The report lists one line per block: its id, its section (GDR-n and heading), its status, its row count and its time.

- `ok`: the query answered. The count is word types; it reads "at least N" when the parse behind it is incomplete. For a `group-by` block the rows are groups, so the line also gives the words and tokens beneath them.
- `refused`: the line was refused, and the finding carries the code (for example `guide-query.run-not-read-only` or `query.twin-needs-regex`).
- `not-built`: the query needs a parse index that is not built. The finding names the command that builds it (`motif guide check --build`); without `--build` the check builds nothing.
- `failed`: the query threw or ran past the per-block timeout.
- `skipped`: `--no-run` was given.

`--build` builds each corpus parse index a block needs and that is missing or stale (a grammar apply makes it stale), then checks the blocks. It runs the same build as the block's own `motif query corpus forms` line, under the same parser work limit and machine capacity rules, once per distinct index: blocks that share one reuse it. A cold build can take many minutes; `--timeout` bounds a query, not the build, and a build that fails is reported as `failed` for that block. `--build` cannot be combined with `--no-run`.

`--no-run` lists the blocks and checks the block text without running anything, which is the fast check. `--timeout <seconds>` is the longest one block may run before it is reported as failed (default 60). A `motif group` line is not run by the check. Nothing is stored: the results are computed for the report and dropped.

With `--json` the answer carries a `queryBlocks` array with `id`, `gdr`, `section`, `status`, `code`, `message`, `rows`, `complete`, `milliseconds` and, for a cold index, `buildCommand`; a group-by block also has `words`, and a block with a token total has `tokens`; `indexBuilt` is true when `--build` built its index.

```text
motif guide check --project Koro.fwdata --no-run
motif guide check --project Koro.fwdata --build
```
