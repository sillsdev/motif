# Add a Grammar Guide section or part

`guide add --project <fwdata> --title <text> [--parent <GDR-n>] [--after <GDR-n>] [--from-file <file.md | ->] [--ws <tag>] [--draft <name>]` stages a new item in the project's Grammar Guide list as a Draft change. It stages only what FieldWorks can create, a new item in a possibility list. Nothing reaches the FieldWorks project until a person applies the Draft.

The new item takes the next permanent number: one more than the largest `GDR-n` in the list, so the first is `GDR-1`. A number is never reused, even after its item is deleted: the highest number given out is kept in the project's Grammar Guide list Description and in Motif's store. The number is final once applied; titles and bodies change later with `guide edit`.

- `--parent <GDR-n>` puts it under that part or section. Without it the item goes at the top of the list (a Guide part).
- `--after <GDR-n>` puts it right after that sibling. Without it the item goes last under its parent.
- `--ws <tag>` is the title's writing system, any writing system of the project, analysis or vernacular, for example `ta`; an unknown tag is refused (`guide.title-invalid`). The default is the project's default analysis writing system. The title is plain text and is stored as given.
- `--from-file <file.md>` (or `-` for standard input) is the body: FieldWorks Markdown in the ADR 0005 template, `### How it works`, `### Examples`, `### Grouping commands`, `### Exceptions`, `### Decisions`, `### Open questions`, `### In the grammar`. Give the body only, with no front matter. Fenced `motif-query` and `motif-show` blocks cannot be added with this command, as with `guide edit`. A body that cannot be read stages nothing (`guide.markdown-invalid`).

```text
motif guide add --project Koro.fwdata --title "Verb conjugation" --parent GDR-2 --after GDR-5 --from-file body.md
motif guide add --project Koro.fwdata --title "வினைச்சொல் இணைப்பு" --ws ta
```

A body that uses a FieldWorks standard style the project lacks (`Normal`, `Heading 1` to `Heading 3`, `Bulleted List`, `Numbered List`, `Block Quote`, `Hyperlink`, `Table Header`, `Table Row`), as a project generated and never opened in FieldWorks does, also stages creating that style in the same Draft with FieldWorks' default properties. The result lists them as `stylesCreated`. A style that is not a standard style is still refused (`text.style-unknown`).

A project with no Grammar Guide list gets the list created by the same Draft, so `guide list` reads `GDR-1` after Apply. A list still named "Grammar Design Records" is read but not written (`guide.list-not-writable`): rename it in FieldWorks. Two lists carrying either name are refused.

## First time on a project

1. `motif guide add --project <fwdata> --title <text>` creates the list and the first section in the pending changes; 2. apply the pending changes (`motif apply --all-pending --project <fwdata>`); 3. capture a Baseline, `motif baseline capture --project <fwdata>`, because `guide list`, `guide show` and `guide check` read the Baseline and say "No Baseline yet" or show no records until then; 4. run `motif guide check --project <fwdata>` (an Autonomous command: add `--autonomous`).

Without `--draft` the item joins the pending changes. A named Draft must already exist. Adding two sections in one Draft numbers them in order.

Availability: `guide add` needs Advanced AI mode (`motif settings advanced-ai on`). In Advanced AI mode, the generated `motif_guide_add` MCP tool takes `project`, `title`, and the optional `parent`, `after`, `markdown`, `writingSystem` and `draft`.
