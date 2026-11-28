# Compose many intents

Stages every intent in a JSON file in one Draft with one project load, through the same checks as each composing command alone. If any intent is refused, nothing is added. Only a person can Apply.

Before any intent runs, one read-only pass checks every intent's alias (`as`, 1 to 21 characters), citation, `@label` references, and each phonological or metathesis rule's `placement` (required once the grammar or an earlier entry has a rule; a rule id anchor must exist). It reports every problem at once, with each intent's position, command and field. A Draft that held no operations when the batch was refused is discarded, so `new --draft` with the same name works again.

Composing operations does not set the Draft's review text. Before Finalize, add a short label with `motif label` and a Rationale with [`proposal rationale`](cmd:proposal%20rationale).

The file names each intent's composing command and carries the same closed intent that command's `--intent` takes.
Add an optional `citation` beside `intent`; Motif records it as operation provenance without changing the intent or its digest. A citation's `source` is at most 2000 characters and its `locator` at most 300; a longer one refuses the whole batch with the intent's position, command, `citation` field and the length it measured, so keep detailed offsets in your own evidence record and give Motif a short, stable locator.

```json
{"intents": [
  {"verb": "compose-author-phoneme", "intent": {"name": "i", "representations": ["i"]},
   "citation": {"source": "Grammar 2026", "locator": "§ 3"}},
  {"verb": "compose-author-natural-class", "intent": {"name": "front", "abbreviation": "F", "members": ["<phoneme id>"]}}
]}
```

Phonological-rule entries may add an `as` label and a later rule may use `@label` in `placement.after` or `placement.before`:

```json
{"intents": [
  {"verb": "compose-author-phonological-rule", "as": "r1",
   "intent": {"name": "first rule", "direction": "left-to-right", "input": [{"phoneme": "<phoneme id>"}],
     "output": [], "left": [{"boundary": "word"}], "right": [{"boundary": "morpheme"}]}},
  {"verb": "compose-author-phonological-rule", "as": "r2",
   "intent": {"name": "second rule", "direction": "left-to-right", "input": [{"phoneme": "<phoneme id>"}],
     "output": [], "left": [{"boundary": "word"}], "right": [{"boundary": "morpheme"}],
     "placement": {"after": "@r1"}}}
]}
```

Any entry may add an `as` label, and a later entry may write `@label` as a whole value anywhere in its intent in place of an ID that the labelled entry created. A phoneme entry labelled `ph`, for instance, lets a later natural class list `"members": ["@ph"]`. Without a kind `@label` means the one object the entry created that nothing else it created sits under (the new entry of an entry composer, the new phoneme); if the entry created several unrelated objects, or you want a dependent one, name the kind: `@label:msa` (or `@label:morphosyntaxanalyses`), `@label:senses`, `@label:codes`, the last word of the operation kind without `create`. A step that creates alternate forms also names each one by its morph type, `@label:prefix`, `@label:suffix`, `@label:infix`, `@label:proclitic`, `@label:enclitic` or `@label:stem`, which tells the two parts of a circumfix apart. A reference that matches nothing, more than one object (the refusal names them), a label declared later, or an entry that created nothing is refused, and the refusal names the objects the entry did create.

Select one of several values created with a phonological feature by its exact value name in the default analysis writing system: `@identity:values:slash`. Names are case-sensitive and canonically equivalent Unicode spellings match. Bare `@identity:values` refuses when there is more than one value. The selector searches only values created by that labelled entry; a value added by another entry uses that entry's own label. Rule input, output and feature contexts accept the same references as other intents.

```json
{"intents": [
  {"verb": "compose-author-phonological-feature", "as": "identity",
   "intent": {"name": "identity", "values": [{"name": "letter"}, {"name": "slash"}]}},
  {"verb": "compose-author-phoneme", "as": "slash",
   "intent": {"name": "literal slash", "representations": ["/"],
     "features": [{"feature": "@identity", "value": "@identity:values:slash"}]}},
  {"verb": "compose-author-phoneme",
   "intent": {"name": "letter c", "representations": ["c", "C"],
     "features": [{"feature": "@identity", "value": "@identity:values:letter"}]}}
]}
```

For subsequent calls in the same Draft, or for saved features, use `lookup:phonological-feature:identity` and `lookup:phonological-feature-value:identity|slash`. Each feature or value matches an exact name or abbreviation in the default analysis writing system. Zero or several matches refuse; use a canonical ID to disambiguate. Lookup runs after earlier staged edits, and both lookup forms work in feature specs and rules.

Labels must match `[A-Za-z][A-Za-z0-9_-]{0,20}` (1–21 ASCII characters). They are case-sensitive and unique within the file. Motif resolves them to canonical IDs before composing and declaring dependencies. They are not saved as intent data. Only a whole value that starts with `@` and names a declared label is replaced, so the plain text `ph` is never touched; the text `@home`, naming no declared label, is left as written; names, citations, and IDs in other fields keep their normal meaning.

Intents run in file order, and each one sees the changes of the intents before it, so the Draft ends as it would after the same commands run one at a time. A refusal names the failing intent's position and command.

There is no limit by intent or operation count.

## One intent over many targets (`forEach`)

Use `forEach` only if each target is a listed exception that no grammar generalization covers; otherwise make the one change that covers them all. The same operation over many targets usually means the change belongs somewhere else (Black, *Parsing Conceptual Introduction*):

- the same environment, spelled with literal sounds, on many forms: one environment written with a natural class (sec. 3.1.2, 3.1.3), or, when the project parses with HermitCrab, one phonological rule (sec. 6.1.2); forms that already share one natural-class environment are the intended model (sec. 3.1);
- the same analysis added to many entries: a derivational affix, possibly zero (sec. 2.1.5);
- different affix forms with one meaning for a group of stems: an inflection class, defined at the highest category that shares it (sec. 2.1.2.6, 2.1.2.8);
- a stem form chosen by the word's inflection features, such as a past-tense stem: a stem name (sec. 3.7.1);
- an affix limited to an arbitrary list of stems: one exception feature, tagged with `compose-tag-stems-by-query` (sec. 2.1.6);
- whole words for a regular derivation: the derivational affix (sec. 2.1.5).

The batch report carries advisories when it sees these patterns; they never refuse. `compose.repeated-condition` names an analysis that five or more intents add, or environments spelled with literal sounds that five or more forms share (it names a phonological rule only when the project parses with HermitCrab or already has rules), `compose.whole-word-environment` an environment with a word boundary on both sides and only literal sounds between, `compose.one-stem-inflection-class` an inflection class the batch leaves with one stem, and `compose.shadowed-alternate` an intent that edits an allomorph (or adds an alternate to an entry) that PanGloss's shadowed-alternates report marks shadowed: an earlier alternate of the same entry or affix is eligible wherever it is, so no word selects it. The message names that earlier alternate and PanGloss's reason; a proven row is worded as a warning, a row PanGloss could not prove as a softer note. The report is the one stored with the Baseline's grammar check (run [`grammar-check`](cmd:grammar%20check) or a Trial first); with none stored there is no such advisory, and a stored report that cannot be read gets one advisory saying so, with the reason. Read each, and either change the model or keep the batch and say why in the Rationale.

An entry may carry `"forEach": {"targets": [...]}` or `"forEach": {"targetsFile": "<path>"}` instead of being copied N times. Each target is an id or a form. The file has one target per line (blank lines are skipped; a line that starts with `"` is read as a JSON string), and a relative path is read from the folder of the batch file. Beside it the entry keeps its `verb`, its `intent` and an optional `citation`; it cannot carry `as`.

In any string of `intent` or `citation`, `{target}` is the target as written. `{target.id}` is the object's canonical id: the target itself when it is an id found in the project, or the one entry whose lexeme form is the target. `{target.form}` is the lexeme form of the entry or allomorph that an id names, or the target itself when it is a form. These three are the only placeholders.

The entry becomes one intent per target, in target order, in the same Draft. `proposal show` lists each one with `expandedFrom` (the entry's position in your file) and `target`. Positions in refusals count the expanded intents. A target that does not resolve, or whose intent a composer refuses, gets its own row (`Intent 3 (verb, target 'x' of forEach intent 1): ...`) and the other targets are still checked; as everywhere in a batch, one refusal still adds nothing. Refused with their own codes: an empty target list (`draft.foreach-empty`), a placeholder `forEach` does not define (`draft.foreach-unknown-placeholder`), `{target}` in an entry with no `forEach` (`draft.foreach-placeholder-without-foreach`), a `forEach` inside a `forEach` entry (`draft.foreach-nested`) and a target that names no object (`draft.foreach-target-unresolved`).

Add a Verb analysis to many entries (a new category, then the same intent per entry; the glosses name each form):

```json
{"intents": [
  {"verb": "compose-author-part-of-speech", "as": "verb", "intent": {"name": "Verb", "abbreviation": "v", "ws": "en"}},
  {"verb": "compose-add-morphosyntax-analysis",
   "forEach": {"targets": ["<entry id>", "<entry id>", "<entry form>"]},
   "intent": {"entry": "{target.id}", "msa": {"kind": "stem", "category": "@verb"},
              "gloss": "verb use of {target.form}", "analysisWs": "en"},
   "citation": {"source": "Grammar 2026", "locator": "{target}"}}
]}
```

Put one environment on listed allomorphs that no rule predicts (one environment, then one alternate form per entry from `entries.txt`; five or more raise `compose.repeated-condition` only if the environment spells literal sounds):

```json
{"intents": [
  {"verb": "compose-author-environment", "as": "env",
   "intent": {"name": "after t", "left": [{"phoneme": "<phoneme id>"}], "right": []}},
  {"verb": "compose-author-alternate-form", "forEach": {"targetsFile": "entries.txt"},
   "intent": {"entry": "{target}", "form": "ai", "formWs": "ta", "morphType": "<suffix morph type id>",
              "environments": ["@env"]}}
]}
```

## Changing many stems at once

The per-stem composers take one target each, and a batch is how a whole clean-up becomes one Proposal. Put one intent per stem in the file, for example `compose-set-stem-part-of-speech` to retype hundreds of stems (each with its own `expectedPartOfSpeech`) and, to stop stems parsing, retype or delete them (the do-not-parse flag changes no parse). Mix verbs freely; `compose-edit-derivational-affix-restriction` takes `alsoMsas` and `compose-add-exception-features` takes `msas`, so those two need only one intent for a list. Give the Draft its own Rationale before Finalize.

## Output size

The report lists the first 25 operations across its intents, and at most 25 intents, whatever the batch added. Every listed intent keeps its index, verb and operation count; `intentTotal`, `operationsAdded` and `nextStep` say what was cut, and the text names it too. The Draft holds every operation: read them in pages with [`show`](cmd:show) (`--cursor`, `--limit`, or `--json` for every operation). `--json` returns the same bounded report. Do not rerun the batch to see the rest: it would add the operations again.
