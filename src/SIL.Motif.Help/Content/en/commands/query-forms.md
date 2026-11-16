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

A Trial built on light facts has no `uses` rows for exception features, categories, slots or inflection classes, and no measured `uses` or `applied` for objects of those classes (their columns are empty, not 0). A `uses` query that selects such a target kind, or an `objects` query that compares `uses` or `applied` for such a class, is refused with `query.not-measured`; otherwise the result lists what was not measured under `NotMeasured`. The same applies to target kinds left out by the 250,000-row `uses` cap.

```powershell
motif query stems --project "project.fwdata" --where 'final:2~[aiu]d' --group shape
motif query objects --project "project.fwdata" --where class=environment --where applied=0
```

## Testing a hypothesised change

`motif query stems --change X>Y` tests a hypothesised change without changing the project. `--at initial|final|medial` and `--affixes a,b` say where X is dropped and which affixes can follow, and `--wordset <id>` names the project's `[[review-word-set]]` of role `sampled-tokens` that serves as the attested evidence. It adds the columns `predicted` (the form with X replaced by Y), `evidence` (`rule`, `base`, `both` or `none`: which of the predicted and the original form the word set attests) and `collides` (`entry`: another entry already has the predicted form; `parses`: the current parser run analyses it; `none`; or `not-measured` when there is no parser evidence).

`--score` takes `tolerance` (the exceptions e must stay below N/ln N), `reliability` (the lower confidence limit (h+0.5)/(n+1) with z = 0.674; its version is printed; it is a sort key, not a verdict) and `control`. `control` follows the rule-hypothesis control test: with `--discovery <id>` and `--heldout <id>` it counts held-out hits against control letters (`--controls`, `--control-letters`) and returns counts only. A held-out set cannot also be the `--wordset`. Scorers count one unit; a scorer that would mix stems and MSAs is refused with `query.mixed-units` (`--unit msa|stem`, `--exception` selectors). `--test-result` prints the control result as the JSON that `motif hypothesis test --file` accepts. Anything Motif did not measure is listed under `NotMeasured`, never shown as 0.

```powershell
motif query stems --project "project.fwdata" --change d>r --at final --affixes an,un,on,i,a --wordset union --score reliability,tolerance
```

## Saved queries

`motif query saved <measure-id>` runs the query that detects the same items as the Measure: `P-msa-no-category`, `P-statement-unused`, `P-env-duplicate`, `P-feature-few-carriers` and `P-lexeme-near-duplicate`. The Measure remains the command that records findings; the saved query lists the items and nothing else.
