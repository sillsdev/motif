# Query forms

`query forms`, `query stems`, `query uses` and `query objects` read the sources below, and `query saved` runs the saved query of a Measure. `query forms` reads forms and their bases from the latest available Parsimony evidence for a Trial. It does not change the FieldWorks project. Run a Trial and wait for its evidence job. If the project changes or a newer Trial completes, the result marks the older evidence as stale.

Use repeated `--where` selectors to combine conditions. The operators are `=`, `in`, `not`, `has`, `~`, `<` and `>`; regular expressions use the non-backtracking engine. For example:

```powershell
motif query forms --project "project.fwdata" --where kind=alternate --where 'change=d>r'
motif query forms --project "project.fwdata" --where kind=alternate --group letters.dropped --json
```

A `--where` can also be a boolean expression: predicates `column op value` joined by `and`, `or` and `not`, with parentheses. `not` binds tighter than `and`, which binds tighter than `or`. The text ops are `is`, `not`, `in`, `not in`, `has`, `<`, `>`, `~` (unanchored regular expression), `matches` (a regular expression that must match the whole value), `exists` and `missing`. A value is a bare word or a double-quoted string, and `in`, `not in` and `has` take comma-separated values. A `--where` that starts with `(` or `not `, or that holds the words `and` or `or` and parses as an expression, is read as an expression; every other `--where` is read as a v1 selector first, so a v1 line keeps its meaning. Wrap a single predicate in parentheses to force the expression reading, for example `--where "(kind not in a,b)"`.

```powershell
motif query forms --project "project.fwdata" --where "kind is variant or (kind is alternate and tokens > 5)" --limit 20
```

`--limit` keeps at most that many rows, groups or ids. A `--as-of` run takes a conjunction of `is`, `not`, `in`, `has`, `<`, `>` and `~` selectors only.

## The JSON form: `--query-json` and `--schema`

`motif query --project "project.fwdata" --query-json <file|->` runs one `motif.query.v2` document, from a file or from standard input with `-`, and takes no other query flag. `motif query --schema` prints its JSON Schema, which is generated from the same records the engine reads; it lists every op, source and column with its type, and the reserved kinds. The MCP tool `motif_query` takes the same document as its `query` argument. Every response carries `schemaVersion`.

```json
{ "version": "motif.query.v2", "from": "forms",
  "where": { "and": [ { "field": "kind", "op": "in", "value": ["alternate", "variant"] },
                      { "not": { "field": "form", "op": "regex", "value": "sa.*" } } ] },
  "limit": 50 }
```

A predicate is `{field, op, value}` with op `eq`, `ne`, `in`, `nin`, `lt`, `gt`, `has`, `regex`, `contains` or `exists`. `regex` must match the whole value and `contains` matches anywhere; both use the non-backtracking engine and are limited to 256 characters. An op that does not fit its column (`lt` on text, `has` on a single value, `regex` on a number) is refused with `query.op-type-mismatch`. `morph`, `seq`, `phon`, `env` and `realizes` are reserved: a document that uses one is refused with `query.not-yet-available`. A malformed document is refused with `query.document-invalid`, naming the path.

`--group` accepts comma-separated columns, `--min-count` filters groups, and `--examples` selects examples ordered by form. `--ids` includes ids for use by other commands. `--trial` selects the Trial whose evidence is available; only one evidence bundle has query rows. Token frequency is not available in this phase. Forms use the project's graphemes; `--raw-letters` uses individual characters instead.

## Reading the Baseline or a Proposal: `--as-of` and `--sides both`

`motif query forms --as-of baseline` reads the lexicon's forms from the current Baseline and needs no Trial. `--as-of <proposalId>` reads that Proposal's latest completed Trial copy, which carries the Proposal's grammar. A Proposal with no completed Trial is not an error: the run succeeds with that side marked `pending` and no rows. `--sides both --as-of <proposalId>` returns the Baseline and the Proposal side by side. Nothing is saved; a run is always read afresh. These runs print JSON. They read only the forms source and refuse `--change`, `--score`, evidence, `--members`, `--context`, `--exception`, `--unit` and `--trial`. An unknown Proposal, no Baseline, a revision digest, or a Trial whose copy was cleared is refused with `query.as-of-unavailable`.

Each row carries its form's columns and, under `parse`, the state and analyses of the word of the Texts whose spelling is the form: `state` is `approved`, `parsed`, `unparsed`, `failed` (it has analyses and a person disapproved every one) or `absent` (the form is not a word of the Texts); `tokens` counts its occurrences in the Texts (the form's `tokens` column); `analyses` lists each analysis's `category`, `approved` flag and morph cut, each `morphs` entry having `form`, `gloss`, `category` and the lexicon `entry` id.

`--where` selectors work as above and also on `parse-state` (`=`, `in`, `not`) and `parse-tokens` (`=`, `not`, `<`, `>`). With `--sides both`, prefix a column with `baseline.` or `proposal.` to apply it to that side only, for example `--where proposal.parse-state=parsed`; a row stays only if it passes every prefixed selector, and an unprefixed selector must pass on at least one side read. A selector on a pending side is not applied. `--sort [baseline.|proposal.]<column>[:desc]` orders the rows by `form`, `base`, `kind`, `entry`, `id`, `morph-type`, `variant-type`, `environments`, `category`, `tokens`, `parse-state` or `parse-tokens`; with `--sides both` the side is required, rows lacking that side come last, and the default is `baseline.form`. A single side may also use `--group`; the groups then appear under `result` and `sides.rows` is empty.

```powershell
motif query forms --project "project.fwdata" --as-of baseline --where kind=alternate --sort form --json
motif query forms --project "project.fwdata" --as-of <proposalId> --sides both --where proposal.parse-state=parsed --sort proposal.form:desc --json
```

The JSON keeps the usual fields and adds `sides` (the usual `result.rows` repeats the rows without parse data for a single side, and is empty for `--sides both`; `trialId` is the proposal side's Trial, and `grammarHash` and `bundleId` are empty):

```json
{
  "sides": {
    "version": 1,
    "mode": "both",
    "states": [
      { "side": "baseline", "status": "ready", "source": "baseline" },
      { "side": "proposal", "status": "pending", "source": "<proposalId>", "reason": "no-trial" }
    ],
    "rows": [
      {
        "id": "<form id>",
        "baseline": { "form": { "id": "", "form": "kaon", "base": "kaon", "kind": "lexeme", "entry": "", "morphType": "stem", "variantType": "", "environments": [], "category": [], "tokens": 3 },
                      "parse": { "state": "approved", "tokens": 3, "analyses": [ { "category": "", "approved": true, "morphs": [ { "form": "kaon", "gloss": "eat", "category": "", "entry": "" } ] } ] } }
      }
    ]
  }
}
```

`version` is the shape version; it rises when a property changes meaning or is removed, and a consumer should tolerate unknown values in the closed sets below. `status` is `ready` or `pending`; `reason` (only when pending) is `no-trial`, `trial-not-completed`, `no-baseline` or `trial-cleared`; `parse.state` is `approved`, `parsed`, `unparsed`, `failed` or `absent`. A pending proposal side is not an absence of changes: read `states` first. `absent` means no data, not a failed parse: the form is matched to a word of the Texts by its exact spelling, so a lexeme stem that is not itself a word of the Texts is `absent` even when its inflected words are parsed. `parse.tokens` counts the word's occurrences in this side's own copy, and the row's `form.tokens` repeats it (omitted when 0), so it differs from the `tokens` column of a latest-Trial query; sorting or filtering on `tokens` or `parse-tokens` treats a missing count as 0. Each row's `id` is unique: a form's id, and for a variant form that varies two or more entries, the variant form's id, `@`, then the main entry's id, so one row per main entry.

A side that was not read, is pending, or lacks the form is omitted from its row. A ready Proposal state also gives `trialId`. `mode` is `baseline`, `proposal` or `both`.

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

`motif query stems --change X>Y` tests a hypothesised change without changing the project. `--at initial|final` and `--affixes a,b` say where X is dropped and which affixes can follow, and `--wordset <id>` names the project's `[[review-word-set]]` of role `sampled-tokens` that serves as the attested evidence. It adds the columns `predicted` (the form with X replaced by Y), `evidence` (`rule`, `base`, `both` or `none`: which of the predicted and the original form the word set attests) and `collides` (`entry`: another entry already has the predicted form; `parses`: the current parser run analyses it; `none`; or `not-measured` when there is no parser evidence).

`--score` takes `tolerance` (the exceptions e must stay below N/ln N), `reliability` (the lower confidence limit (h+0.5)/(n+1) with z = 0.674; its version is printed; it is a sort key, not a verdict) and `control`. `control` follows the rule-hypothesis control test: with `--discovery <id>` and `--heldout <id>` it counts held-out hits against control letters (`--controls`, `--control-letters`) and returns counts only. A held-out set cannot also be the `--wordset`. Scorers count one unit; a scorer that would mix stems and MSAs is refused with `query.mixed-units` (`--unit msa|stem`, `--exception` selectors). `--test-result` prints the control result as the JSON that `motif hypothesis test --file` accepts. Anything Motif did not measure is listed under `NotMeasured`, never shown as 0.

```powershell
motif query stems --project "project.fwdata" --change d>r --at final --affixes an,un,on,i,a --wordset union --score reliability,tolerance
```

## Splitting a pattern and weighing its size

`--score split` asks whether a grouping of rows is a rule. The deviants are the rows `--exception` selects, the ids in a `--members` file, or, under `--change` with a `--wordset`, the rows whose unchanged form alone is attested. In one pass it tallies about 30 context values (edges, shape, category, features, environments, the change itself) and keeps a context only if it covers the deviants, predicts them reliably, is smaller than listing them, can be stated in shapes FieldWorks itself can create, and holds on the held-out set. Outcome A is a sub-rule, B is predictable but not expressible as a rule, and C is a lexical list. Split, size, member and same-shape counts use distinct stem forms by default on the stems source (`--unit msa` counts MSAs instead), and form records on the forms source. Tag references use that declared unit. Missing parser measurements are excluded from context judging and their missing measurement counts are reported. Environment text remains `not-checked` because the query snapshot has no resolved, validated FieldWorks rule context. A context that covers fewer than 20 units is reported only as too few to tell. A second context is looked for once, among the rows the first leaves. Anything it could not decide reads `not-checked`; held-out words are never shown, only counts, and only from 20 words. `--score size` weighs the encodings in objects plus tag references, for the context a `--context <selector>` names, or against the opposite polarity when none is given.

## One screen of evidence

`motif query evidence stems|forms --where <selector>` (or `--members <file>`) prints the shared contexts, the counter-examples, the tolerance, reliability and control, the size, the same-shape count with ledger neighbours, the top examples and the words whose evidence is undecided as questions for a speaker. Scorers that need a change or word sets and did not get them are listed as not measured. Five or more units of one shape mean a rule attempt is required.

```powershell
motif query evidence stems --project "project.fwdata" --where "final:2=ad" --change d>r --at final --affixes an,un --wordset union
```

## Saved queries

`motif query saved <measure-id>` runs the query that detects the same items as the Measure: `P-msa-no-category`, `P-statement-unused`, `P-env-duplicate`, `P-feature-few-carriers` and `P-lexeme-near-duplicate`. The Measure remains the command that records findings; the saved query lists the items and nothing else.
