# Read a Trial's Reading changes

Run `motif proposal trial delta --project <fwdata> --job <jobId> --json`, or `--proposal <id>` for the newest completed Trial with retained evidence. It reads the evidence the Trial already retained, so it measures nothing and needs no second gate run.

The counts cover every measured form. Each class shows a page of rows ordered by spelling, with `--limit` (1 to 200) and `--cursor` (the `nextCursor` of the previous page):

- `lost`: parsed before the change and has no Reading after it.
- `gained`: had no Reading before and has one after.
- `changed`: parsed on both sides and the two sides share no Reading.
- `doubled`: keeps every old Reading and has more copies after.
- `stopped-negative`: a known non-word that parsed before and no longer does. It is an improvement, never lost. Ask for it with `--classes`.

A form that resolved to one sense before the change and has several candidate senses after it (or the reverse, or one resolved sense swapped for another) changes its Reading id, so it shows as one retired and one added Reading. When the two Readings share entry, MSA, allomorph and features and differ only in sense resolution, they pair, and each pair is one change. `counts.senseChanged` is the number of pairs and `counts.readingChanges` is `retiredReadings + addedReadings - senseChanged`; `rows[].senseChanged` is the same count for one word. The Readings stay in `retiredReadings`, `addedReadings`, the rows and the manifests, which remain complete, and approvals are not affected. The classes do not change: such a word with no other change is still `changed`.

Each row keeps `caseKey`, the id the report and regression rows use, and lists the retired and added Readings by signature id with a copy count and the signature as a manifest row writes it. Only lexicon-backed Readings count; guessed Readings do not.

`--write-manifests <dir>` writes `expect-retired.jsonl` and `expect-added.jsonl` there, one row per word and Reading: `{"word": ..., "signature": [...], "count": n}`. The format is the one the grammar lanes write, with the gate's own spellings: words are lower-cased and composed (`--keep-case` keeps them as measured), and ids the Proposal creates are renamed `created:N` in order of first appearance, as the gate renames them (`--alias-created false` keeps real ids). Name the directory; a bare `--write-manifests` is refused. Only the CLI writes files. Together they enumerate the whole delta: a lost word's Readings are in the retired file, and a known non-word that stopped parsing has no row. Review them before use; nothing is accepted automatically. The command refuses when any form was not measured on both sides or has a Reading without a complete identity, because a manifest must not leave part of the delta out. The summary reports Reading and word counts per entry, MSA and allomorph so a reviewer can check the swap names the expected objects.

## Group the changes by cause

`--group-by entry` or `--group-by slot` replaces the per-word rows with one group per cause, so a lane reads which lexical entry or affix slot a change comes from without a script. Each group has `key`, `label`, `retired` and `added` Reading copies, `words` (distinct), `tokens` and up to five `sampleWords`, the most frequent first. Groups are ordered by tokens, most first, then by key, and `--limit` and `--cursor` page the groups. `--classes` chooses which words count; the class totals stay and their rows are left out. It also works with `--json`, `--write-manifests` and the expectation flags, which still see the whole delta.

- `entry`: the key is the entry's canonical id and the label its headword. A Reading counts toward every entry it names, once.
- `slot`: the key is the canonical id of an inflectional affix slot of an affix morpheme's analysis, the label its name. Three keys stand for no slot: `(no slot)` for an inflectional affix that names none, `(derivational)` for a derivational affix, and `(unknown msa)` for an analysis the Baseline copy does not hold. Stems are not counted. Slots come from the current Baseline's project copy; if it cannot be opened the command refuses with `trial.delta-group-unsupported`.
- `rule` is refused with `trial.delta-group-unsupported`: a Reading's identity carries entries, analyses, allomorphs and features but no phonological or compound rule, and the command does not guess one.
- `tokens` are the word's occurrences in the current Baseline's texts, case folded; spellings that differ only in case count as one word. When the Baseline holds none, `tokens` is 0 and `limitations` carries `tokens-unavailable`.

## Hold the delta to what you expected

`--expect-retired <file>` and `--expect-added <file>` name manifests in the format `--write-manifests` writes, and `--max-doubled <n>` and `--max-doubled-frac <0 to 1>` cap the doubled words. Any of them adds `expectations` to the result, the grammar lanes' gate rules over the Trial that already ran:

- Each manifest must name the whole delta of every measured word. A word whose retired or added Readings differ from its rows, in Reading or in copy count, is a mismatch, whether the Trial shows more than the manifest or less. A word with no row expects no change. A known non-word that stopped parsing needs no row. A manifest word the Trial did not measure is ignored and counted in `manifestWordsNotMeasured`.
- The caps count words that parsed before, kept every old Reading and gained more. `--max-doubled-frac` is a fraction of the measured words that parsed before. Both apply even when the added Readings are declared. Without a cap, doubled words are only reported.
- `state` is `green`, `mismatch`, `doubled-over-cap` or `unmeasured` (some form was not measured on both sides, the Trial measured no form, or the Trial is not of the Proposal revision and Baseline as they stand now; `reason` names both revisions or both Baselines, so run a new Trial). `reason` says why, `mismatches` lists the first 50 words with `unexpected` and `missing` Readings, and `doubled`, `doubledSample` and `doubledOverCap` carry the cap evidence.

The command exits 0 only when the state is `green`. Any other state exits 6, as a Trial with a lost word does; read `expectations.state` to tell them apart. `--pick` projects the result on that path too. Manifests spell words lower-cased and ids the Proposal creates as `created:N`, so use `--alias-created false` and `--keep-case` when yours do not. `--write-manifests` cannot be combined with these flags.


## Write a receipt for Apply

`--write-receipt <file>` goes with at least one expectation flag. When the state is `green` it writes a receipt there and the result carries `receipt` (`path`, `receiptDigest`, `gateCodeDigest`); any other state removes an earlier file at that path and writes none. The receipt binds the Proposal's revision, the Baseline the Trial measured, that Trial and the build of the gate rules that judged it, and carries a digest of its own content. `motif apply <proposalId> --receipt <file>` and `motif apply --proposal <id> --receipt <file>` then stop with `apply.receipt-invalid` when the file is missing, edited or not green, and with `apply.receipt-stale` when the Proposal revision, the Baseline, the Trial or the gate build has changed since; run a new Trial and write a new receipt. Without `--receipt`, Apply asks for none. `apply --all-pending` takes no receipt.

## Siblings

A Trial queued with `--siblings` (see `motif trial`) measured corpus words that share a touched form. They are in the delta and judged by the expectations above like every other measured word. The result also carries `siblings`: `pattern`, `seed`, `max`, `touchedForms`, `referrerForms` and `referrers` (the edited grammar objects with the count of entries that use each; and `skippedReferrerForms` (form and reason, up to fifty); only when the sample drew on them), `corpusTypesSha256`, `candidates`, `parsedCandidates`, `sampled`, `measured`, `lost`, `lostSample`, `doubled` and `doubledSample` (up to twenty words each), and `warning` when the sample could not use something (for example the forms of existing entries when the project had no Baseline). It is omitted when the Trial sampled no siblings. A sampled sibling the Trial did not measure, or a sibling record that cannot be read, makes `expectations.state` `unmeasured` and adds the limitation `siblings-unmeasured` or `siblings-unreadable`; it never reads green.
