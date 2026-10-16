# Find a composer

Finds the composing commands that fit a goal in a linguist's words, or lists every composer that serves a Subject. Reads only; nothing is staged and no project is opened.

Give exactly one of a goal or a Subject.

```sh
motif find-composer --goal "add a plural suffix to nouns"
motif find-composer --subject affix-slots-and-templates --detail detailed
```

`--goal` takes a few words and returns at most five composers, best first. Words are matched against each composer's example goals, its title and its description, and against the titles of the Subjects it serves. `--subject` takes a Subject id and returns every composer that serves it, ordered by command name.

A goal that no composer covers is not answered with a weak list. A composer covers a goal only when its example goals and title together contain at least two distinct content words of the goal, or one when the goal has two content words or fewer; a word that appears only in a description or a Subject title does not count. When none does, the answer says `noMatch: true` with no results. It adds `nearestSubjects` (up to two Subjects with their titles), `nearestComposers` (up to three composers with their weak scores), `missing` (one sentence to tell the person, such as "Motif has no Composer for "export the dictionary" yet; the nearest Subject is Lexical entries and glosses.") and `next`, which says to tell the person what is missing rather than stage a nearby composer. The Motif MCP server keeps a local note of each miss, the goal and its nearest Subject, in its activity log; the command line records nothing, and nothing leaves the machine.

Each result carries the command name, a short title, when to reach for it, the Subjects it serves, its intent schema, and, for a goal search, its `score` and the `matchedWords` of the goal it matched. A composer with a declared shape carries that schema as JSON Schema (`schemaSource` is `shape`). Any other composer has no schema in the result: `schemaSource` is `help`, and the intent it takes is described on its own Help page.

`--detail detailed` adds the first worked example from the composer's Help page, when it has one, and the opening paragraph of the Subject page that fits the search. Concise is the default. Add `--json` for the structured result.

A search that names both a goal and a Subject, or neither, is refused with `composer.goal-or-subject`. An unknown Subject is refused with `composer.unknown-subject`, and a detail level other than concise or detailed with `composer.invalid-detail`.

This command is available when Advanced AI mode is on. The MCP tool is `motif_find_composer`, with `goal`, `subject` and `detail` arguments, and is off unless a profile selects it.
