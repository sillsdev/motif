# Move a Grammar Guide section

`guide move --project <fwdata> <GDR-n> [--parent <GDR-n>] [--after <GDR-n> | --before <GDR-n>] [--draft <name>]` stages the move of one Grammar Guide section, with all its sub-sections, under another parent or to another place among its siblings, as a Draft change. It is the same move a person makes by dragging an item in the Guide list in FieldWorks. Nothing reaches the FieldWorks project until a person applies the Draft.

The section keeps its `GDR-n` number and its id, and so do its sub-sections, so every link to them stays valid. A Guide can be restructured into an outline without renumbering.

- `--parent <GDR-n>` is the new parent. Without it the section goes to the top of the list (a Guide part).
- `--after <GDR-n>` puts it right after that sibling, `--before <GDR-n>` right before it. The sibling must be under the new parent. Without either the section goes last under its parent. Give at most one (`guide.move-position`).
- Moving a section under itself or one of its own descendants is refused (`guide.move-cycle`). A number or id no section has is refused (`guide.not-found`). Each refusal names the next step.
- A Draft that already moves the section is stale for a second move: it is refused (`guide.move-stale`). Preview the staged move with `guide show <GDR-n> --as-of <draft>`, or remove its operation and move again. A move applied after the section was moved in FieldWorks is refused at Apply.

```text
motif guide move --project Koro.fwdata GDR-7 --parent GDR-3 --after GDR-4
motif guide move --project Koro.fwdata GDR-9 --before GDR-2 --json
```

Without `--draft` the move joins the pending changes. A named Draft must already exist. The `--json` answer carries `draftName`, `id`, `abbreviation`, `parentId`, `previousParentId` and `operationCount`. A Guide move changes no word, so its Trial is exempt from the Autonomous gate. After Apply, run `baseline capture --project <fwdata>`: the Guide reads see only the Baseline.

Availability: `guide move` needs Advanced AI mode (`motif settings advanced-ai on`). In Advanced AI mode, the generated `motif_guide_move` MCP tool takes `project`, `id`, and the optional `parent`, `after`, `before` and `draft`.
