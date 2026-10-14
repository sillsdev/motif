# Compose many intents

Stages every intent in a JSON file in one Draft with one project load, through the same checks as each composing command alone. If any intent is refused, nothing is added. Only a person can Apply.

Composing operations does not set the Draft's review text. Before Finalize, add both a short label with `motif label` and a comment with `motif comment`.

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

Labels must match `[A-Za-z][A-Za-z0-9_-]{0,20}` (1–21 ASCII characters). They are case-sensitive, unique within the file, and supported only for phonological rules. Motif resolves them to the earlier rule's canonical ID before composing and declaring dependencies. They are not saved as intent data; names, citations, and IDs in other fields keep their normal meaning.

Intents run in file order, and each one sees the changes of the intents before it, so the Draft ends as it would after the same commands run one at a time. Other composers cannot refer to entities created earlier in the same file; put those intents in a second file and use the identities the first one reports. A refusal names the failing intent's position and command.

There is no limit by intent or operation count. A subsequent Dry Run or Trial needs the Proposal's frozen job input to fit within 1 MiB of UTF-8 JSON; an oversized input is refused with `job.input-too-large` and advice to split the Proposal.
