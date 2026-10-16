# Phonology Example Set

A Phonology Example Set is a list you write down of what a form should become: an underlying form such as `n+baya`, the surface form you expect, such as `mbaya`, and the lexical entry that supplies the context. Each example can carry a citation to its source. Motif keeps each saved version unchanged, so a later edit is a new revision and the earlier one can still be read.

A set is advisory evidence. A [Phonology Check](cmd:phonology-examples%20results) can measure the current grammar against it and report, for every example, whether the expected surface form was produced (agree), the search finished without it (disagree), or the question could not be answered (unavailable). None of that changes Readiness, and Apply ignores it.

## Commands

- `phonology-examples save` records a new revision of a set. It is an advanced AI command, off until Advanced AI mode is on.
- [List phonology examples](cmd:phonology-examples%20list) shows the current revision of each saved set.
- [Show phonology examples](cmd:phonology-examples%20show) reads the examples of one saved revision, a page at a time.
- `phonology-examples check` queues a check of a saved revision against the current Baseline. It is an advanced AI command and needs a captured Baseline.
- [Read phonology check results](cmd:phonology-examples%20results) reads a finished check as summary pages or one complete example.
