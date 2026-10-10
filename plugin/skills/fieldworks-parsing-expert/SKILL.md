---
name: fieldworks-parsing-expert
description: Diagnose FLEx HermitCrab or XAmple parses that are wrong, missing, ambiguous or slow, and explain Grammar-area setup, affix templates, allomorphs, features, phonological rules and parser traces. Use for parser behavior and configuration; route general screen lookup to fieldworks-expert and linguistic judgments to linguistic-consultant.
---

# FieldWorks parsing expert

Explain parser behavior in plain language, then give the smallest discriminating check. The UI
baseline is **FieldWorks 9.3.11 Windows/WinForms**; engine gotchas have their own provenance and
reach labels. A feature existing in HermitCrab does not establish that FLEx can configure it.

HermitCrab analyzes possible underlying structures then synthesizes them to confirm the surface
and constraints. XAmple uses dictionary/allomorph analyses plus constraints and a PC-PATR word
grammar; it does not execute HermitCrab phonological rules. Use the chosen engine's evidence.

For a new grammar, follow Black's build order: phonemes and spelling, one small paradigm, a short
text, lexical entries and grammatical information, slots/templates, choose parser, Try a Word,
read the analysis/status and iterate. Start with [setup](references/workflow/setup.md) and
[concept map](references/concept-map.md). Do not populate every rule before exercising one.

For a reported failure, record the FLEx/parser version, selected engine, exact surface, expected
complete analysis (named stem and affixes), and an observed trace or result. Missing evidence
leaves a hypothesis. [Diagnosis](references/diagnosis.md) routes by symptom:

- Missing: first check loaded segments/forms, then category/class/slot licensing, then confirmation.
- Wrong: identify the exact unwanted path; inspect gates and final confirmation, not only unapplication.
- Too many: separate distinct legitimate readings from duplicate paths; inspect optionality and partial affixes.
- Slow: separate load, reverse search and confirmation; retain correctness while measuring cost.

Read one matching topic from the [correctness corpus](references/broken/index.md) or
[performance corpus](references/speed/index.md), then follow its linked workflow. For malformed or
silently dropped project objects use [loader traps](references/loader-traps.md). For menu actions,
traces and human approval use [parser controls](references/workflow/parser-controls.md).
For XAmple use [its appendix](references/xample.md), which explicitly has no equivalent gotcha corpus.
For measured experiments use [bounded evidence](references/workflow/measuring.md).
Sources and licensing are copied into [sources](references/sources.md); the
[terminology crosswalk](references/crosswalk.md) is local to this package.

State engine reach, loader reach and UI reach separately. Confirm a proposed repair on a positive
example and a near-negative contrast, including every required reading of an ambiguous word.
A timeout or truncated search is incomplete evidence. Neither a proposed analysis nor a green
parser indication is human approval or a linguistic verdict.

Do not answer general FLEx location/visibility here: load `motif:fieldworks-expert`. Do not decide
which linguistic analysis is justified here: load `motif:linguistic-consultant`. Project changes go
through `motif:motif-workflow`, which owns Motif tool use. If unavailable, explain how Advanced AI
mode supplies the connection; do not substitute direct project/XML editing. This skill supplies
knowledge and a diagnostic procedure, not an alternative project writer.
