# Show project settings

`config show` reads the named Assessment scopes and related settings stored beside one FieldWorks project.

## When to use it

Use it before measuring when you want to confirm which Assessor, Assessment kinds, or resource limits a named scope selects. A scope provides context for an Assessment; it does not gate whether two measurements can be compared. The Default Selection's parsing limits are saved separately; use [selection show](cmd:selection%20show) to read them.

## Example

```powershell
motif config show --project "C:\FieldWorks\Projects\Koro\Koro.fwdata"
```

## What it prints

The command prints the saved project settings, or their structured form when `--json` is supplied. It reads configuration and does not run PanGloss.

Each Assessment scope's `timing-detail-words` limits the number of costliest words whose individual object timings are stored. The default is 2,000 and the allowed range is 0–10,000. Set it in `<project>.motif.toml` beside the project, for example:

```toml
[[scope]]
name = "default"
timing-detail-words = 1000
```

Motif keeps the newest 20 standalone Assessments per kind, as well as Assessments needed by current or cited evidence. To change the count, add this project setting:

```toml
[retention]
standalone-assessments-per-kind = 20
```

## Set a parser work limit for the project

A word that needs more parser work than the limit stops, and a Trial reports it as capped, so the gate cannot measure it. To give every parse the same higher limit, add this project setting:

```toml
[parser]
work-limit = 60000000
```

The value is a positive whole number or `unbounded`. Trial, Assess and the Trial an Apply runs all use it. The order is the same everywhere: an explicit `--parser-work-limit` flag, then the limit a saved Default Selection or the scope itself carries (a scope without its own limit takes the project's), then the project's `[parser] work-limit`, then the default of 20,000,000 work units. A project setting never overrides a Selection or scope limit that was set explicitly. `parse --trace`, `trace` and `why-lost` pass the limit to PanGloss as `--work-cap`: the frozen limit of the Trial they explain when there is one, otherwise the limit a Trial would freeze now, and they say which. A value that is not a positive whole number or `unbounded` is refused with `config.invalid`, naming the line. `config show --json` reports it as `parserWorkLimit`, and a Trial and its evidence export report the limit that measured their words.

## Declare an Autonomous Apply gate

Add this to `<project>.motif.toml`, beside the project, to require the saved Selection named Gold words:

```toml
[gate]
word-sets = ["gold"]

[[review-word-set]]
id = "gold"
name = "Gold words"
role = "regression"
selection = "Gold words"
```

Save that Selection before running a Trial. To use a review word-set JSONL manifest instead, replace `selection` with `manifest = "gold.jsonl"`; relative paths resolve beside the TOML file. The list must be nonempty and name distinct declared review sets. An absent `[gate]` refuses Autonomous Apply with `apply.gate-undeclared`; it does not imply a default gate. `config show --json` reports the required ids as `gateWordSets` when declared and omits that field otherwise.

Required sets are measured even with `trial --review-set none`. The Trial freezes their source, membership, expectation and policy digests. Autonomous Apply requires complete measurement and the current declaration; changing a Selection, manifest or policy requires a fresh Trial.

The gate reuses these existing settings:

```toml
[gain-gate]
mode = "advisory"
min-attested-per-object = 1.5

[parsimony]
block-on = []
```

Gain modes are `off`, `advisory` (default) and `hold`. `hold` keeps a declared gate from being green below the threshold. Added phonological rules and affix processes are exempt. Parsimony blocks only opted-in measures: `R-word-disapproved-produced`, `R-word-negative-accepted`, and `P-variant-same-shape`. Missing required measurement remains unmeasured. A person's Apply keeps its existing Readiness checks.

## Related commands

- [Measure a Selection](cmd:assess) uses the configured scope when one is named.
- [Show the Default Selection](cmd:selection%20show) reads the project's usual word list.
- [Set Selection limits](cmd:selection%20set-limits) changes its parser work limit or time policy.
