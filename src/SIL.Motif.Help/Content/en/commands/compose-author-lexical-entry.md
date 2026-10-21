# Author a dictionary entry

Use this command to add a stem or an affix the project does not have yet. One call stages a complete entry: its lexeme form, one sense with a gloss and an optional definition, and the grammatical info the parser needs to use it.

```sh
motif compose-author-lexical-entry --project language.fwdata --draft new-words \
  --intent '{"form":"kitabu","formWs":"sw","morphType":"<stem-morph-type-id>","gloss":"book","analysisWs":"en","definition":"a bound set of written pages","msa":{"kind":"stem","category":"<noun-id>","inflectionClass":"<class-7-8-id>"}}'
```

`form` is the lexeme form in the vernacular writing system `formWs`, without the hyphens FieldWorks shows on affixes. `morphType` is the id of an existing morph type such as stem, root, prefix or suffix. `gloss` and the optional `definition` are in the analysis writing system `analysisWs`.

`msa` is the grammatical info (in FieldWorks, the morphosyntactic analysis). Its `kind` must suit the morph type: `stem` for a stem or root, and `inflectional`, `derivational` or `unclassified` for an affix. `category` is the part of speech; a derivational affix also needs `toCategory`, the part of speech of the word it makes. An inflectional affix can name existing `slots` of its category or a category above it, and a stem can name an `inflectionClass`, such as a noun class.

An affix can also be restricted. `inflectionClasses` lists existing inflection classes, of the affix's category or a category above it, and the form then attaches only to stems of those classes; leave it out for no restriction, and a stem entry cannot use it. `environments` lists existing phonological environments the form occurs in, for a stem or an affix. Empty lists, repeated ids and a stem's `inflectionClass` on an affix are refused.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.

Every id can belong to something an earlier command added to the same Draft, such as a new part of speech. The command returns the new entry's operations and created entity ids for later calls. Dry Run and Trial the Draft on affected words, revise it, then Finalize it for a person to review and Apply.

## When not to use this

- For an affix, prefer an inflectional MSA with its category and a slot over an unclassified one, which attaches without restriction.
- Do not create an entry per stem that takes the same affix; one affix entry serves them all.
- For another form of an existing entry, use `compose-author-alternate-form`.
