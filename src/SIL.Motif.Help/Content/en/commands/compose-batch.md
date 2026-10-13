# Compose many intents

Stages every intent in a JSON file in one Draft with one project load, through the same checks as each composing command alone. If any intent is refused, nothing is added. Only a person can Apply.

The file names each intent's composing command and carries the same intent that command's `--intent` takes:

```json
{"intents": [
  {"verb": "compose-author-phoneme", "intent": {"name": "i", "representations": ["i"]}},
  {"verb": "compose-author-natural-class", "intent": {"name": "front", "abbreviation": "F", "members": ["<phoneme id>"]}}
]}
```

Intents run in file order, and each one sees the changes of the intents before it, so the Draft ends as it would after the same commands run one at a time. An intent cannot name something an earlier intent in the same file creates, because that identity is made only when the intent runs: put such intents in a second file and use the identities the first one reports. A refusal names the failing intent's position and command.

There is no limit by intent or operation count. A subsequent Dry Run or Trial needs the Proposal's frozen job input to fit within 1 MiB of UTF-8 JSON; an oversized input is refused with `job.input-too-large` and advice to split the Proposal.
