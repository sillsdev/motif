# Author an alternate form

Use this command to give an entry another form, such as a prefix that changes before a vowel. One call stages the form on an entry that already exists, or that an earlier command added to the same Draft, and links the conditions you name. It adds no entry, sense or grammatical info and leaves the lexeme form and every other form as they are.

```sh
motif compose-author-alternate-form --project language.fwdata --draft new-words \
  --intent '{"entry":"<entry-id>","form":"mw","formWs":"sw","morphType":"<prefix-morph-type-id>","environments":["<before-vowel-id>"],"inflectionClasses":["<class-1-2-id>"]}'
```

`form` is written in the vernacular writing system `formWs`, without the hyphens FieldWorks shows on affixes. `morphType` is the id of an existing morph type and must suit the entry: a stem or root type for a stem entry, an affix type for an affix entry. An entry whose lexeme form is an affix process is refused.

Each form states its own conditions, and none are copied. `environments` lists existing phonological environments the form occurs in. `inflectionClasses`, for an affix entry only, lists existing inflection classes, of the category the entry's grammatical info names or a category above it; the form then attaches only to stems of those classes. Leave either out for no condition. Empty lists and repeated ids are refused.

Alternate forms have declared precedence: an eligible earlier form can block a later one. When the entry already has alternate forms, `placement` is required and names the neighbours of the insertion gap, as `{"after":"<form-id>"}`, `{"before":"<form-id>"}` or both. Adjacent means the two are next to each other now.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.

The command returns the new form's operations and created entity id. Dry Run and Trial the Draft on affected words, revise it, then Finalize it for a person to review and Apply.
