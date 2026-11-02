# Compose many intents

Stages every intent in a JSON file in one Draft with one project load, through the same checks as each composing command alone. If any intent is refused, nothing is added. Only a person can Apply.

Composing operations does not set the Draft's review text. Before Finalize, add a short label with `motif label` and a Rationale with [`proposal rationale`](cmd:proposal%20rationale).

The file names each intent's composing command and carries the same closed intent that command's `--intent` takes.
Add an optional `citation` beside `intent`; Motif records it as operation provenance without changing the intent or its digest.

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

Any entry may add an `as` label, and a later entry may write `@label` as a whole value anywhere in its intent in place of an ID that the labelled entry created. A phoneme entry labelled `ph`, for instance, lets a later natural class list `"members": ["@ph"]`. Without a kind `@label` means the one object the entry created that nothing else it created sits under (the new entry of an entry composer, the new phoneme); if the entry created several unrelated objects, or you want a dependent one, name the kind: `@label:msa` (or `@label:morphosyntaxanalyses`), `@label:senses`, `@label:codes`, the last word of the operation kind without `create`. `@label:msa#2` takes the second of that kind in creation order. A reference that matches nothing, more than one object, a label declared later, or an entry that created nothing is refused, and the refusal names the objects the entry did create.

Labels must match `[A-Za-z][A-Za-z0-9_-]{0,20}` (1–21 ASCII characters). They are case-sensitive and unique within the file. Motif resolves them to canonical IDs before composing and declaring dependencies. They are not saved as intent data. A string that is not a declared label, such as the text `@home`, is left as written; names, citations, and IDs in other fields keep their normal meaning.

Intents run in file order, and each one sees the changes of the intents before it, so the Draft ends as it would after the same commands run one at a time. A refusal names the failing intent's position and command.

There is no limit by intent or operation count.
