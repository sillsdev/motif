# Unreleased

## Breaking for MCP and CLI clients

The Grammar Design Records list is now the Grammar Guide, and its commands are renamed with no aliases. Update scripts, agent prompts and MCP profiles.

- CLI verbs: `motif gdr list`, `gdr show` and `gdr check` are now `motif guide list`, `guide show` and `guide check`. `motif gdr ...` is an unknown command.
- MCP tool: `motif_gdr_show` is now `motif_guide_show`. A custom profile that names the old tool fails to load.
- `hypothesis link` options `--gdr`, `--gdr-section` and `--gdr-none` are now `--guide`, `--guide-section` and `--guide-none`. In MCP and JSON the fields `GdrId`, `GdrSection` and `GdrNone` are now `GuideId`, `GuideSection` and `GuideNone`.
- Refusal and finding codes `gdr.*` are now `guide.*` (for example `gdr.not-found` is `guide.not-found`, `gdr.no-records` is `guide.no-records`).
- Motif reads the possibility list named "Grammar Guide". A list still named "Grammar Design Records" is read too, with a `guide.list-old-name` warning from `guide check` asking you to rename it in FieldWorks. Both names present is refused as ambiguous.
- Record ids stay `GDR-n`, and the Motif store keeps its existing hypothesis columns; nothing is migrated.
- `MOTIF_DEVELOPER_COMMANDS=1` no longer opens the Autonomous commands, which now refuse with `command.autonomous-required`. Set `MOTIF_AUTONOMOUS=1` or pass the global `--autonomous` flag instead; there is no alias. Moved commands: `discard-draft`, `discard-empty-drafts`, `reopen`, `duplicate`, `split`, `defer`, `reject`, `supersede`, `promote-gloss`, `preflight`, `add-delete-lexeme-form`, `project create`, `guide list` and `guide check`. They are never MCP tools. The same switch also makes `apply` an Autonomous-mode Apply, which needs a green Gate and `--message`; `motif help autonomous` explains it.

## Stores

- An older project store is upgraded when the project opens. Authored work is carried and derived data (Trials, Assessments, Baselines, Jobs, indexes) is dropped. Before it upgrades, Motif takes a verified `motif store backup` (under `<project>.motif-backups`) and tells you where it is and which derived results were dropped, in the CLI, MCP and the app; if the backup fails, nothing changes. The original is also kept beside the store as `.schemaN.bak`, and `motif store restore` brings the backup back. `motif store reset` backs up first too.
- A store made by a newer Motif is refused with the `storeNewer` fact. The window and `motif store delete-refused` no longer offer or perform a reset for it: install the newer Motif, or restore a backup made before the upgrade.

## Window

- The window and its Help call the list the Grammar Guide, and its entries Guide sections, instead of Grammar Design Records. A project whose list is still named "Grammar Design Records" opens the Guide read-only with a sentence saying to rename it in FieldWorks (Lists). Both names present shows a sentence saying to remove one in FieldWorks.

## Agent plugin

- The Motif plugin now ships the `grammar` router skill and the `grammar-discovery-loop` and `rule-hypothesis-test` skills, so agents building a grammar are pointed at writing and testing a rule before listing word forms. The `motif hypothesis` verbs the skills use (`add`, `test`, `decide`, `link`, `grade`, `answer`, `neighbours`, `report`) and `motif list hypotheses` are released CLI commands: they need no switch and are never MCP tools.
