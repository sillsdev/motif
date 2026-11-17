# Unreleased

## Breaking for MCP and CLI clients

The Grammar Design Records list is now the Grammar Guide, and its commands are renamed with no aliases. Update scripts, agent prompts and MCP profiles.

- CLI verbs: `motif gdr list`, `gdr show` and `gdr check` are now `motif guide list`, `guide show` and `guide check`. `motif gdr ...` is an unknown command.
- MCP tool: `motif_gdr_show` is now `motif_guide_show`. A custom profile that names the old tool fails to load.
- `hypothesis link` options `--gdr`, `--gdr-section` and `--gdr-none` are now `--guide`, `--guide-section` and `--guide-none`. In MCP and JSON the fields `GdrId`, `GdrSection` and `GdrNone` are now `GuideId`, `GuideSection` and `GuideNone`.
- Refusal and finding codes `gdr.*` are now `guide.*` (for example `gdr.not-found` is `guide.not-found`, `gdr.no-records` is `guide.no-records`).
- Motif reads the possibility list named "Grammar Guide". A list still named "Grammar Design Records" is read too, with a `guide.list-old-name` warning from `guide check` asking you to rename it in FieldWorks. Both names present is refused as ambiguous.
- Record ids stay `GDR-n`, and the Motif store keeps its existing hypothesis columns; nothing is migrated.

## Window

- The window and its Help call the list the Grammar Guide, and its entries Guide sections, instead of Grammar Design Records. A project whose list is still named "Grammar Design Records" opens the Guide read-only with a sentence saying to rename it in FieldWorks (Lists). Both names present shows a sentence saying to remove one in FieldWorks.
