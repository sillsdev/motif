# Add an affix process allomorph

Use this command to give an affix that already realizes itself as an affix process another pattern-based form, the way Lexicon Edit > Insert Allomorph > Affix Process does in FieldWorks. One call stages the new allomorph on an entry that exists, or that an earlier command added to the same Draft. Its `input` and `output` mean what they mean for `compose-author-affix-process`.

This example adds a form of an existing nasal-substitution prefix that turns a stem-initial `t`, `d` or `s` into `n`:

```sh
motif compose-author-affix-process-allomorph --project language.fwdata --draft prefix-forms \
  --intent '{"entry":"<entry-id>","input":[{"name":"first","naturalClass":"<t-d-s-class-id>"},{"name":"rest","remaining":true}],"output":[{"insert":["<p-id>","<a-id>","<n-id>"]},{"copy":"rest"}],"placement":{"after":"<earlier-form-id>"}}'
```

The intent has `entry`, `input`, `output`, optional `inflectionClasses` and optional `placement`. There is no gloss, grammatical info, slot or morph type: the form copies the entry's morph type and applies to every grammatical info the entry has, so a change meant for one meaning of a multi-meaning affix needs a different entry. `input` and `output` follow the same rules and limits as an affix process: at most one remaining-stem part, last; at least one input copied; and a realization that inserts, omits or repeats something. There are no environments, because an affix process carries none.

The entry must be an affix whose lexeme form is an affix process, as `compose-author-affix-process` creates. A stem, an entry with no lexeme form, an entry without affix grammatical info, and an entry that mixes plain allomorphs with processes are all refused, naming the entry. So is a pattern and output the entry already has.

Alternate forms have declared precedence. When the entry already has alternate forms, `placement` is required and names the neighbours of the insertion gap, as `{"after":"<form-id>"}`, `{"before":"<form-id>"}` or both, and they must be adjacent now. The first alternate form takes no placement. `inflectionClasses` lists existing inflection classes of the category the entry's grammatical info names or a category above it; the form then attaches only to stems of those classes.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces. The command stages operations in a Draft and does not change the saved FieldWorks project. Dry Run and Trial the Proposal on affected words, including words just outside each pattern, then Finalize it for a person to review and Apply.

## When not to use this

- For a spelling variant, or a form conditioned by an environment, use `compose-author-alternate-form`.
- For a new morpheme, create a new entry with `compose-author-affix-process` or `compose-author-lexical-entry`.
- For a regular sound change across many forms, use a phonological rule with `compose-author-phonological-rule`.
