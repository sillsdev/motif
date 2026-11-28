# Edit a Grammar Guide section

`guide edit --project <fwdata> <GDR-n> --from-file <file.md | -> [--draft <name>]` stages an edit of one existing Grammar Guide section as FieldWorks Markdown. It adds the same operations the window's Markdown view adds, so a person's edit and an agent's edit are applied, checked for Drift and reviewed alike. Nothing reaches the FieldWorks project until a person applies the Draft.

Take the starting text from `guide show <GDR-n> --format fw-md`, or, when the Draft already holds an edit of the section, from `guide show <GDR-n> --as-of <draft> --format fw-md`. A file that would put a staged field back to the Baseline text is refused (`guide.markdown-invalid`) so no staged work is lost silently. The file is either that whole record with its front matter and one fenced div per field (title, abstract, Discussion), or, with no front matter, the Discussion body alone. The record identity, parent and position are read-only. Only fields that differ from the section are staged; a file identical to the section stages nothing and says so.

```text
motif guide show --project Koro.fwdata GDR-4 --format fw-md > gdr-4.md
motif guide edit --project Koro.fwdata GDR-4 --from-file gdr-4.md --draft plural-rule
motif guide show --project Koro.fwdata GDR-4 --as-of plural-rule
```

Without `--draft` the edit joins the pending changes, as an edit in the window does. A named Draft must already exist (`motif new`); stage the Guide edit in the same Draft as the rule it explains, and the section ships with the rule. `--from-file -` reads standard input.

An unchanged paragraph keeps its identity; a changed one is replaced in place, and a section edited in FieldWorks in the meantime is refused at Apply (`text.field-stale`), as for any text edit. An inline code span in backticks is accepted as the `Code` character style, staged in the same Draft when the project lacks it (see `guide add`). A pipe table likewise stages `styles/style/create` for `Table Header` and `Table Row` when the project lacks them; the result names every such style in `stylesCreated`, so a gate that allows only Guide operations can allow exactly those names, and `operationCount` includes them. Markdown the reader cannot parse is refused as `guide.markdown-invalid` with every finding, and nothing is staged.

A Proposal whose operations are all Guide-section edits or text-segment Notes has no word to lose: Autonomous Apply does not ask for a green Trial gate for it (see `motif help autonomous`). Dry Run still shows every field changed.

Fenced code blocks, including `motif-query` and `motif-show` blocks, cannot be added or changed with this command: a new block is added in FieldWorks, and a file that adds one is refused with nothing staged.

An interlinear example is a fenced div, not a code block, so this command stages it: `::: {.interlinear}`, then the lines `word:`, `morphemes:`, `gloss:` and optionally `translation:`, then `:::`. Write the word and morphemes lines as `[text]{.v}` so they are in the vernacular writing system; the gloss and translation are analysis text. The project keeps the lines as plain paragraphs, and `guide show` folds them back into the block. `guide check` reports morphemes the lexicon lacks and lines whose tokens do not pair up.

A query block's saved table (the caption and table right after the block) is not authored text: this command keeps each block's stored table whatever the Markdown holds, warns when you changed it, and removes it only with its block; `guide refresh-tables` writes it.

This command edits a section that exists in the project's Grammar Guide list; a new section or part is added with `guide add`, and an unknown section is refused as `guide.not-found`.

Availability: `guide edit` needs Advanced AI mode (`motif settings advanced-ai on`); `guide show` is a read and needs no mode; `guide decide` needs `--autonomous`. An agent running the whole loop needs both.

In Advanced AI mode, the generated `motif_guide_edit` MCP tool takes `id`, `project`, `markdown` and an optional `draft`.
