# Read a Trial report

`proposal trial` reads the saved report of a Proposal's Trial: observed red flags, incomplete cases, counts and stopped words. It reads stored evidence, does not rerun the parser, and needs no developer setting.

```powershell
motif proposal trial --project "C:\FieldWorks\Projects\Koro\Koro.fwdata" <proposalId> --json
```

Use `--job` or `--report` to pick a particular Trial or saved report, and `--detail failures` or `--detail full` for more of its cases. [Trial a Proposal](cmd:trial) describes every selector.

With no selector, the command reads the newest Trial job for this Proposal, even if it failed or was cancelled. The Autonomous Apply gate uses the same job. Use `--latest` by itself to read the job ID, status and, when saved, `reportId` and `reportDigest` without expanding the report. This live identity response is bounded to 2 KiB in JSON, including the MCP success envelope. An identity above the limit is refused. A Proposal with no Trial returns a "no Trial" result that names the command to start one.

A report is current when it belongs to the newest completed Trial, its Proposal and Baseline identities match, and the active parser executable matches both checkpoint digests. A known parser mismatch marks it historical. A missing or unlaunchable parser, or a missing checkpoint pin, leaves freshness unknown unless another check establishes staleness. An exact report read keeps its saved ID, digest and gate verdict.

Every stopped word is retained in `report.stoppedWords` with its before or after side, reason codes and specific cause. The same cause appears on the word’s full review page when detail is available. An invalid shape means the parser could not segment the spelling with the phoneme inventory; inspect the recorded explanation before changing the grammar.

The stored result keeps its `gate` and `report.losses`: `newNoParseWords` and `newApprovedLossWords` name every affected word, and `newlyLostReadings` names each word and frozen reading identity. These lists survive when the optional detail index exceeds the store budget. For each word listed in `--expect-loss`, `newlyLostReadings` also carries one row per lost Reading with its canonical `signature` (ordered morphemes), the lost copy `count` and `remainingReadingCount` (lexicon-backed Readings still emitted); a word that lost every Reading lists them all with zero remaining. `losses.retirementAttestation` is bound to the report digest and its `complete` is true only when those rows cover the full retirement multiset of the waived words across every measured case; `limitations` says why not. The saved report keeps every retirement row; a read returns the rows filled to a 16 KB bound and `retirementAttestation.page`, `pageCount` and `rowCount` say how many exist, and `--page <N>` reads the others (`--report` pins the exact saved report). Nothing is refused for size. Rows with a `signature` (the retirement multiset) and rows without one (confirmed losses of frozen expectations) describe the same loss from two sides: a gate must read each kind on its own and must not sum across the two kinds. Expected losses are frozen when the Trial is queued; later reads keep that Trial’s verdict.

When a word had a Baseline analysis but its spelling cannot be segmented by the candidate phoneme inventory, the parser may mark it `skipped`. This is a named loss, including any confirmed readings it lost, rather than a pass or an unknown search result. Malformed evidence cannot establish that loss. A listed intended inventory loss is covered by the Trial's saved expected losses.

The summary names lost and regressed words in `report.regressions`: at most 10 per cause, each with its analyses before and after, then the count of the rest and the `nextStep` command that pages them all.

A new second reading on an attested word is named in `report.heldWords` and keeps the gate unmeasured until independent approval and a fresh Trial. The held names survive the detail budget too. Observed stops with no morphology packet retain their producer outcome; undispatched cases receive no invented stop.

Older reports without compact gate facts are explicitly unmeasured. Use `--refresh-report` to rebuild from exact retained evidence, or rerun the Trial if those inputs are unavailable. Autonomous Apply requires a [declared gate](cmd:config%20show), its current required scope and policy, and a green newest Trial for the live project state.
