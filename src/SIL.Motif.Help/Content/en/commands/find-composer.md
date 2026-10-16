# Find a composer

Finds the composing commands that fit a goal in a linguist's words, or lists every composer that serves a Subject. Reads only; nothing is staged and no project is opened.

Give exactly one of a goal or a Subject.

```sh
motif find-composer --goal "add a plural suffix to nouns"
motif find-composer --subject affix-slots-and-templates --detail detailed
```

`--goal` takes a few words and returns at most five composers, best first. Words are matched against each composer's example goals, its title and its description, and against the titles of the Subjects it serves. `--subject` takes a Subject id and returns every composer that serves it, ordered by command name.

Each result carries the command name, a short title, when to reach for it, the Subjects it serves, and its intent schema. A composer with a declared shape carries that schema as JSON Schema (`schemaSource` is `shape`). Any other composer has no schema in the result: `schemaSource` is `help`, and the intent it takes is described on its own Help page.

`--detail detailed` adds the first worked example from the composer's Help page, when it has one, and the opening paragraph of the Subject page that fits the search. Concise is the default. Add `--json` for the structured result.

A search that names both a goal and a Subject, or neither, is refused with `composer.goal-or-subject`. An unknown Subject is refused with `composer.unknown-subject`, and a detail level other than concise or detailed with `composer.invalid-detail`.

This command is available when Advanced AI mode is on. The MCP tool is `motif_find_composer`, with `goal`, `subject` and `detail` arguments, and is off unless a profile selects it.
