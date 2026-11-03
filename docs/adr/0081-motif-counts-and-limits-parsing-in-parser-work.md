# ADR 0081 — Motif counts and limits parsing in parser work

**In plain terms:** Motif now says "this grammar takes less work to parse the words". The measure it reports for a word, a rule or a whole run is **parser work**, and the limit a person sets for one word is a **parser work limit**. The old words "work steps" and "step limit" are gone from the window, the command line, Help and the saved data. The limit's default is 20,000,000 parser work, which is what the old default of 200,000 analysis attempts meant to the parser, so no parse changes. Nothing a person saved is lost: an old limit is read as a hundred times its value.

**Status:** accepted, 2026-11-02. Provisional and reversible: the owner may reverse any numbered decision.

## Context

The owner's words: "Pangloss 0.13.0 landed - it has better work-steps. Make sure to pull out all step stuff. We should likely rebrand the limits / caps to match the work_step, maybe called 'parser work'. 'this grammar takes less work to parse the words'. PanGloss 0.13.1 is coming with Parsimony speedups; when it drops 'steps' is not known."

PanGloss 0.13.0 reports `work_steps` for every word (every unit of work the word reserved, the quantity its `--work-cap` bounds) and adds `batch --cost-column`, a fixed-weight `cost` that adds bytes allocated and FST registers copied to the work. Cost is the better estimate of instructions (direction right 17 of 17 times at a 2% change, against 16 of 17 for work alone) but it cannot come with `--stats`, exists only in the batch rows, and has no per-rule split, so Motif's per-rule shares and every cap would lose their number. Motif counted a word's work in work steps but limited it in analysis attempts, two different quantities under one "step" word.

## Decision

### 1. One number: parser work

Motif reads PanGloss's `work_steps` and calls it parser work everywhere a person or an agent can see it. It does not read `cost`; that is recorded as read and unused until PanGloss's next shapes are known. The vocabulary is "parser work" for the measure ("1,234 parser work") and "parser work limit" for the bound. Unrelated steps (workflow steps, migration steps, trace steps) keep their name.

### 2. The limit is a parser work limit

The per-word limit counts parser work. Its default is 20,000,000. Motif passes `--work-cap` with that number. PanGloss 0.14.0 dropped `--step-cap` and analysis attempts per word, so Motif no longer derives or sends a step cap and the work limit is the only cap. The time estimate keeps its per-attempt rate: a limit of 100 parser work allows one attempt, so estimates do not change.

### 3. Names follow, and PanGloss's keys stay at the reading points

Contract members, JSON keys, command-line flags (`--parser-work-limit` replaces Motif's `--step-cap`; PanGloss's own flags keep their names), resource strings and store columns take the new name. PanGloss's wire names (`work_steps` in the batch row and statistics cache, `workSteps` in trace details, the resident parser's answer and phonology examples, `--step-cap`) stay where Motif reads or passes them, each with a comment, so that PanGloss removing them is a change in those places only.

### 4. Saved values convert on reading

A project file's `per-word-step-limit`, a stored scope's `perWordStepLimit` and `{"steps":N}` read as a parser work limit of 100 times N. Named Selections stored an attempt count in a column, so the store takes schema 60, which keeps Proposals and every other authored table as [ADR 0077](0077-project-store-upgrades-carry-authored-data.md) says and stores each Named Selection's limit as 100 times its old value; computed results are dropped and recomputed.

### 5. What breaks

The Contract, the MCP argument names (`perWordParserWorkLimit`), the CLI flag, the project file key and the JSON keys `workSteps` (now `parserWork`) change. An agent or script that sends or reads the old names must change. Old saved data still opens.
