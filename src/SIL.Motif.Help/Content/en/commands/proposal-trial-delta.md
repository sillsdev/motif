# Read a Trial's Reading changes

Run `motif proposal trial delta --project <fwdata> --job <jobId> --json`, or `--proposal <id>` for the newest completed Trial with retained evidence. It reads the evidence the Trial already retained, so it measures nothing and needs no second gate run. The MCP tool `motif_proposal_trial_delta` takes `project`, `jobId`, `proposalId`, `classes`, `offset` and `limit`.

The counts cover every measured form. Each class shows a page of rows ordered by spelling, with `--limit` (1 to 200) and `--offset` or `--page`:

- `lost`: parsed before the change and has no Reading after it.
- `gained`: had no Reading before and has one after.
- `changed`: parsed on both sides and the two sides share no Reading.
- `doubled`: keeps every old Reading and has more copies after.
- `stopped-negative`: a known non-word that parsed before and no longer does. It is an improvement, never lost. Ask for it with `--classes`.

Each row keeps `caseKey`, the id the report and regression rows use, and lists the retired and added Readings by signature id with a copy count and the signature as a manifest row writes it. Only lexicon-backed Readings count; guessed Readings do not.

`--write-manifests <dir>` writes `expect-retired.jsonl` and `expect-added.jsonl` there, one row per word and Reading: `{"word": ..., "signature": [...], "count": n}`. The format matches what the grammar lanes write, so `--expect-retired` and `--expect-added` accept the files as they are. Together they enumerate the whole delta: a lost word's Readings are in the retired file, and a known non-word that stopped parsing has no row. Review them before use; nothing is accepted automatically. The command refuses when any form was not measured on both sides or has a Reading without a complete identity, because a manifest must not leave part of the delta out. The summary reports Reading and word counts per entry, MSA and allomorph so a reviewer can check the swap names the expected objects.
