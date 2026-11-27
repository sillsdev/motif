# Convert ids

`id convert` spells each id you give it in both forms, so you can move between a project file's guids and the canonical ids a Proposal names without writing a script.

## When to use it

The `.fwdata` file stores an object's id as a hyphenated guid (`3f2a8c10-5b7e-4d21-9a63-0c1e2f3a4b5c`). A Proposal's intent names the same object by its canonical id, an optional prefix and 22 base64url characters. Use this command to turn one into the other. It reads no project, so it works anywhere.

Every command that takes an object id (`lexical-entry`, `grammar read --id`, the `--target` of `add-set-gloss`, `add-delete-lexeme-form` and `promote-gloss`, and the text ids of `progress`, `word read-state`, `remove-analysis` and `accept-new-set`) accepts either form, so converting first is only needed for a Proposal's intent, which stays canonical.

## Example

```powershell
motif id convert 3f2a8c10-5b7e-4d21-9a63-0c1e2f3a4b5c
```

## What it prints

One line per id with its `guid` and its `canonical` spelling. Add `--json` for an array of `{input, guid, canonical}` objects in argument order.

An argument that is neither form is not stopped on: its line (or its JSON object, which then carries `code` `id.invalid` and a `message` in place of `guid` and `canonical`) reports the problem, every other argument still prints, and the command exits with the invalid-argument code.

## Related commands

- [Read a lexical entry](cmd:lexical-entry) reads an entry or sense by either id form.
- [Find lexicon entries](cmd:lexicon%20find) spells its result ids in either form with `--id-format`.
