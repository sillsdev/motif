# Oracle v1

This recipe makes a grammar task’s answer key reviewable: two strong models reason separately,
challenge the evidence once, and hand unresolved linguistic choices to a person. It also defines how
a fixed Judge pair measures agreement with those keys across every Arm.

**State: prepared, not activated.** No model rulings, human spot-checks, parser runs or live Judge
qualification were performed in this lane. The qualification examples are authored synthetic fixtures,
not accepted Oracle keys. They are a rehearsal packet, never live comparison evidence.

The normative rules are [ADR 0059](../../../docs/adr/0059-measuring-agents-fairly.md) and
[ADR 0058](../../../docs/adr/0058-the-agent-edit-loop.md). The [manifest](manifest.json) pins the
models, effort, prompts and content digests; [rules](rules.md) govern acceptance. Reference records
are in [the library](../../../docs/references/README.md); all industry records meet its date cutoff.
All roles load [linguistic-consultant](../../../plugin/skills/linguistic-consultant/SKILL.md) and its
[sixteen checks](../../../plugin/skills/linguistic-consultant/references/checklist.md).

## Contents

- [rules.md](rules.md): evidence packets, independent first rulings, rebuttal, attacks, parser facts,
  linguistic escalation and human sampling.
- [rulings.md](rulings.md): keys, Oracle rulings, Judge response shape and result records.
- [judge.md](judge.md): identical instructions for independent Opus/Sol judgments.
- [qualification/README.md](qualification/README.md): rehearsal and qualification protocol, denominator
  rules and proposed local acceptance thresholds.
- [qualification/cases.jsonl](qualification/cases.jsonl): task packets and candidate answers.
- [qualification/anchors.jsonl](qualification/anchors.jsonl): separate expected fixture labels.
- [qualification/fake-opus.jsonl](qualification/fake-opus.jsonl) and
  [qualification/fake-sol.jsonl](qualification/fake-sol.jsonl): frozen fake Judge outputs.
- [qualification/score.py](qualification/score.py): offline validation and agreement accounting.
- [prompts/](prompts/author.md): role instructions, including human review.

## Offline rehearsal

From the repository root, run:

```sh
python evals/oracle/v1/qualification/score.py   --cases evals/oracle/v1/qualification/cases.jsonl   --anchors evals/oracle/v1/qualification/anchors.jsonl   --opus evals/oracle/v1/qualification/fake-opus.jsonl   --sol evals/oracle/v1/qualification/fake-sol.jsonl
```

This reads fixtures only, makes no network or model call, and prints agreement plus an explicit
`fixture_only` status. It cannot activate the recipe or qualify a real model. Judges receive the
cases and frozen key criteria, never anchors; qualification targets remain control-side.

A future accepted version additionally freezes the actual starting grammar, parser outputs,
independent raw rulings, attacks/controls and human decisions. Those inputs are absent here by design.
The Oracle may include an agent model being measured, with that overlap disclosed. The Judge may
never include the model under test: if the tested model is Opus or Sol itself, this fixed pair is
ineligible and the comparison is a Hold pending a separately authorized qualified recipe.
