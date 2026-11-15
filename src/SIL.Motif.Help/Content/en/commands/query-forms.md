# Query forms

`query forms`, `query stems`, `query uses` and `query objects` read the sources below, and `query saved` runs the saved query of a Measure. `query forms` reads forms and their bases from the latest available Parsimony evidence for a Trial. It does not change the FieldWorks project. Run a Trial and wait for its evidence job. If the project changes or a newer Trial completes, the result marks the older evidence as stale.

Use repeated `--where` selectors to combine conditions. The operators are `=`, `in`, `not`, `has`, `~`, `<` and `>`; regular expressions use the non-backtracking engine. For example:

```powershell
motif query forms --project "project.fwdata" --where kind=alternate --where 'change=d>r'
motif query forms --project "project.fwdata" --where kind=alternate --group letters.dropped --json
```

`--group` accepts comma-separated columns, `--min-count` filters groups, and `--examples` selects examples ordered by form. `--ids` includes ids for use by other commands. `--trial` selects the Trial whose evidence is available; only one evidence bundle has query rows. Token frequency is not available in this phase. Forms use the project's graphemes; `--raw-letters` uses individual characters instead.

## Other sources

| Source | One row per | Columns |
|---|---|---|
| `stems` | stem MSA | `form`, `entry`, `category` (set, `none` when the MSA has no part-of-speech row), `features` (set), `gloss` (`none` when blank), `msa-count`, `alternates` |
| `uses` | reference to an environment, natural class, exception feature, category, slot or inflection class | `target-kind`, `target`, `user-kind`, `user`, `role`, `effect` |
| `objects` | environment, natural class, exception feature, category, slot or inflection class | `class`, `label`, `uses`, `applied`, `carriers`, `users`, `key` |

`stems` and `forms` also have `shape` (the consonant and vowel skeleton), `syllables`, `initial:n` and `final:n` (the first or last n segments) and `fold`. `<` and `>` compare `tokens`, `syllables`, `msa-count`, `alternates`, `uses`, `applied`, `carriers` and `users`. `fold` uses `--fold`, comma-separated from `nfc`, `case`, `iy~y`, `o~u`, `e~i`, `c~k`, a raw `from>to` and `project` (the project's `[parsimony] fold` rules); the default is `nfc,case`. `--distinct <column>` keeps only groups that hold two different values of that column.

```powershell
motif query stems --project "project.fwdata" --where 'final:2~[aiu]d' --group shape
motif query objects --project "project.fwdata" --where class=environment --where applied=0
```

## Saved queries

`motif query saved <measure-id>` runs the query that detects the same items as the Measure: `P-msa-no-category`, `P-statement-unused`, `P-env-duplicate`, `P-feature-few-carriers` and `P-lexeme-near-duplicate`. The Measure remains the command that records findings; the saved query lists the items and nothing else.
