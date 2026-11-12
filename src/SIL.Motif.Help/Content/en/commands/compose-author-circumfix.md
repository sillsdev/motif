# Author a circumfix

Use this command to stage one new circumfix: a single entry with a prefix part and a suffix part that attach to a stem together, as in Indonesian `ke-...-an`. FieldWorks holds it as one entry of the circumfix type whose lexeme form reads `ke- -an`, with a prefix form and a suffix form beside it. The parser pairs the two parts into one rule, so neither part alone attaches.

```sh
motif compose-author-circumfix --project language.fwdata --draft circumfix \
  --intent '{"prefix":"ke","suffix":"an","formWs":"id","gloss":"abstract noun","analysisWs":"en","msa":{"kind":"derivational","category":"<verb-category-id>","toCategory":"<noun-category-id>"}}'
```

The intent has `prefix` and `suffix`, written without hyphens, spaces or full stops (FieldWorks splits the lexeme form on a space or full stop), `formWs` for the writing system of both, `gloss`, `analysisWs`, optional `definition`, and `msa`. The MSA kind is `inflectional`, `derivational` or `unclassified`; a circumfix is an affix, so it is never a stem. `slots` belong to an inflectional circumfix, and `toCategory` to a derivational one. `prefixEnvironments` and `suffixEnvironments` each limit one part to existing environments; absent means any. `inflectionClasses` limits both parts to existing inflection classes of an inflectional or unclassified circumfix. References may name saved project objects or objects created earlier in the same Draft.

Each part of a circumfix keeps its own sound environment on PanGloss: the prefix part's environment is checked at the stem start and the suffix part's at the stem end (`RealParserCircumfixEnvironmentTests`). Separate circumfix entries with different suffix environments can choose a linker by the stem's last sound; see the [conditioned allomorphs guide](guide:encoding/conditioned-allomorphs). Run a Trial with the expected words, wrong linkers, missing halves and stems excluded by the prefix environment.

In a `compose-batch` file give the circumfix entry an `as` label. Its two parts have the distinct labels `@label:prefix` and `@label:suffix`, so a later `compose-author-alternate-form` can place a form `"placement":{"before":"@label:suffix"}` without copying an id from the output. `@label` alone is the entry, and `@label:msa` and `@label:senses` are as for any entry.

The command stages the entry, its lexeme form, the two parts, their conditions, the analysis and the sense in one Proposal. Dry Run and Trial it on words with and without the circumfix, then Finalize it for a person to review and Apply. `compose-edit-morph-type` and `compose-edit-affix-inflection-classes` still refuse a circumfix, because a single form cannot be edited into or out of one.

A derivational affix's composed summary lists a `Derivation: From -> To` line naming both categories, so a swapped pair shows at once, and its Dry Run warns when the project has no stem, subcategory stem or derivation that makes the From category, or when nothing takes the To category (or a subcategory) as input, which usually means the categories are the wrong way round.

## When not to use this

- Two markers that can each occur without the other are two entries in two slots; use `compose-author-lexical-entry` and a template (the [suffix and prefix slots guide](guide:encoding/suffix-prefix-slots)).
- A one-sided affix is a plain prefix or suffix from `compose-author-lexical-entry`.
- For a marker that copies or changes the stem itself, use `compose-author-affix-process`.
