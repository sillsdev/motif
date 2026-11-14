# Query forms

`query forms` reads forms and their bases from the latest available Parsimony evidence for a Trial. It does not change the FieldWorks project. Run a Trial and wait for its evidence job. If the project changes or a newer Trial completes, the result marks the older evidence as stale.

Use repeated `--where` selectors to combine conditions. The operators are `=`, `in`, `not`, `has`, `~`, `<` and `>`; regular expressions use the non-backtracking engine. For example:

```powershell
motif query forms --project "project.fwdata" --where kind=alternate --where 'change=d>r'
motif query forms --project "project.fwdata" --where kind=alternate --group letters.dropped --json
```

`--group` accepts comma-separated columns, `--min-count` filters groups, and `--examples` selects examples ordered by form. `--ids` includes ids for use by other commands. `--trial` selects the Trial whose evidence is available; only one evidence bundle has query rows. Token frequency is not available in this phase. Forms use the project's graphemes; `--raw-letters` uses individual characters instead.
