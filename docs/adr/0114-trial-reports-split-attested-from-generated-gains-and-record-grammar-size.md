# 0114. Trial reports split attested from generated gains and record grammar size

CEB packages reported probe forms generated from the grammar as "gained" words (one reported 924 gained and 95
attested), and 22 applied packages added 1,476 entries and variants and no phonological rule. A gain count that
generated forms can raise rewards a Proposal for adding objects, not for explaining the project's words.

**Status:** accepted. Follows the advisory principle of [ADR 0060](0060-parsimony-evidence-and-advisory-dispositions.md)
section 6: the gate is report-only and changes nothing that gates Apply.

## Decision

1. **Attested and generated gains.** A gain is a word that did not parse Before and parses After. It is attested when the word is a sampled token in the project's texts, or
   carries an eligible Approved analysis; otherwise it is generated (a paradigm cell, a reference form or a touched
   word with no other evidence). A word counts once however many sets hold it. The report shows both numbers,
   labelled so that generated gains read as exposure, not evidence.
2. **Grammar size delta.** Each report counts the objects its Proposal adds, from the Proposal's operations alone,
   by kind: entries, senses, MSAs, allomorphs, variants, affix processes, phonological rules, environments,
   exception features (a possibility created and then named by a host operation of the same Proposal) and other
   list items. Hosts are counted only for exception features the Proposal creates, and are reported beside the total, not in it. An entry the Proposal
   creates counts once; its own senses, MSAs and allomorph are not counted again. A created entry that is the
   target of a variant reference counts as a variant. Only objects FieldWorks can create are counted
   ([ADR 0112](0112-motif-writes-only-what-fieldworks-can-create.md)).
3. **Gain gate.** The report divides attested gains by objects added. Below the project's threshold the gate says
   under-threshold; with mode `hold` it also sets `hold`. Modes are `off`, `advisory` (the default) and `hold`;
   the default threshold is 1.5 attested types per object. A Proposal that adds a phonological rule or an affix
   process is exempt (rule-added), because one rule can explain many words and its attested gain says so. A report
   with incomplete cases, with no objects added, or with the gate off is never under-threshold.
4. **Configuration.** `<project>.motif.toml` takes a `[gain-gate]` table with `mode` and `min-attested-per-object`.
   The policy is frozen into the Trial's review inputs, so a stored report stays a function of retained inputs.
5. **Advisory only.** `hold` is a field of the report. Apply and Readiness do not read it
   (ADR 0060 section 6); a driver such as CEB's package loop reads
   it and records the column. Reports written before this decision have no `gains` object and read as before.

## Consequences

- Probe forms cannot raise the gate; only attested words do.
- The delta is a count of operations, so it needs no parser run and exists for a Proposal that has not been tried.
- Not decided here: a lexical-category exemption for names and loans, and a record of attempted rules.
