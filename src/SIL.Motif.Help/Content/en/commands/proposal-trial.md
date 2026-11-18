# Read a Trial report

`proposal trial` reads the saved report of a Proposal's Trial: observed red flags, incomplete cases, counts and stopped words. It reads stored evidence, does not rerun the parser, and needs no developer setting.

```powershell
motif proposal trial --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" <proposalId> --json
```

Use `--job` or `--report` to pick a particular Trial or saved report, and `--detail failures` or `--detail full` for more of its cases. [Trial a Proposal](cmd:trial) describes every selector.

A Proposal with no Trial returns a "no Trial" result that names the command to start one.

Every stopped word is retained in `report.stoppedWords` with its before or after side, reason codes and specific cause. The same cause appears on the word’s full review page when detail is available. An invalid shape means the parser could not segment the spelling with the phoneme inventory; inspect the recorded explanation before changing the grammar.

The stored result keeps its `gate` and `report.losses`: `newNoParseWords` and `newApprovedLossWords` name every affected word, and `newlyLostReadings` names each word and frozen reading identity. These lists survive when the optional detail index exceeds the store budget. Expected losses are frozen when the Trial is queued; later reads keep that Trial’s verdict.

When a word had a Baseline analysis but its spelling cannot be segmented by the candidate phoneme inventory, the parser may mark it `skipped`. This is a named loss, including any confirmed readings it lost, rather than a pass or an unknown search result. Malformed evidence cannot establish that loss. A listed intended inventory loss is covered by the Trial's saved expected losses.

A new second reading on an attested word is named in `report.heldWords` and keeps the gate unmeasured until independent approval and a fresh Trial. The held names survive the detail budget too. Observed stops with no morphology packet retain their producer outcome; undispatched cases receive no invented stop.

Older reports without compact gate facts are explicitly unmeasured. Use `--refresh-report` to rebuild from exact retained evidence, or rerun the Trial if those inputs are unavailable. Autonomous Apply requires a [declared gate](cmd:config%20show), its current required scope and policy, and a green newest Trial for the live project state.
