# Measure word type coverage

`coverage` answers whether a lane is Done. It reads a lane ledger TSV, its sidecar JSON and the pinned frequency list, joins them by word type, and reports where every type stands, what is still Open, and the three Done tests. It reads and never writes. The MCP tool `motif_coverage` takes the same fields in camelCase.

This is not `report --kind coverage`, which is the share of a scope's words the parser analysed, measured from one stored Assessment. `coverage` counts word types against a ledger and asks whether each one is accounted for: parsed and expected, set aside with a disposition, or Open.

## Example

```text
motif coverage --project <fwdata> --ledger ledger.tsv --sidecar ledger.meta.json --frequency accept-dev.tsv --json
```

- `--ledger`, `--sidecar` and `--frequency` are required. `--frequency-format tsv|tsv-header|json` names the list's shape; a `.json` file is read as a word-to-count object and anything else as two tab-separated columns.
- The project's Motif store, when it exists, adds its saved checks and its word type dispositions. A type with a current check at the ledger's grammar revision is proved; the rest carry the ledger's state and are counted as stale.
- `--audit-dir` and `--protected` name the frozen audit sample and protected set that the Correct and Stable tests read; without them those tests are unmeasured.
- `--limit N` caps `open.ranked`, 50 by default and at most 200. `--tokens` adds `tokenShare`, the share of eligible tokens carried by Open singletons; it is absent otherwise.

## What it prints

`done.verdict` is `fail` when any of `accounted`, `correct` and `stable` fails, `pass` when all pass, and `unmeasured` otherwise; each test lists its `reasons` and `counts`, such as `recurring-open-current`. `byState` gives types and tokens per state, split into `current` and `stale`. `open.byMechanism` groups Open types by the ledger row's class, keeping the 20 heaviest by type count; `open.mechanismTotal` counts every mechanism and `open.otherMechanisms` sums the ones cut (absent when none were), and `open.ranked` lists the heaviest recurring Open types. A malformed ledger, sidecar or frequency list is refused with a `coverage.*` code that names the file and a next step and, for a ledger row, its line number. An `--audit-dir` or `--protected` path that does not exist is refused with `coverage.file-unreadable`; leave the flag out to leave that check unmeasured.

## Related commands

- [Read an Assessment report](cmd:report) with `--kind coverage` is the share of words the parser analysed.
- [Give word types a disposition](cmd:wordform%20dispose) records the dispositions this command counts as accounted for.
