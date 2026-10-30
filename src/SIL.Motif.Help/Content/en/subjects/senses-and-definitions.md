# Senses and definitions

A sense is one meaning of an entry, with its gloss and definition. A word with several meanings has one entry and several senses.

## What the work needs

- Read the entry's existing senses first, so a meaning that is already recorded is not added again.
- State the gloss briefly and the definition in a full sentence, in the writing systems the project uses for each.
- Keep one sense for one meaning. Split a sense only when the evidence shows the meanings behave differently.
- Unrelated meanings that share a form are separate entries (homographs), not senses.

## Composers, in order

1. `compose-author-lexical-entry` creates a new entry together with its first sense, gloss and definition.
2. `compose-delete-empty-sense` deletes an empty sense and the analysis only it uses; a part-of-speech-only analysis requires a linked variant and a sense with content is refused.
3. `compose-move-sense-to-entry` moves a sense, with its sub-senses and examples, into a new entry of its own; an entry's only sense is refused.
4. `compose-delete-sense` deletes a sense or sub-sense that carries content, with its sub-senses and examples; an entry's only sense and a sense something refers to are refused.

## Pitfalls

- No composer here edits the content of the sense of an entry that already exists. Say so instead of approximating an edit with unrelated changes.
- Do not copy a gloss from another language's expectations. Use the evidence from the project's texts and the linguist.
- A definition in the wrong writing system will not show where the linguist looks for it.
