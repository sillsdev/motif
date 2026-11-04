# Author an alternate form

Use this command to give an entry another form, such as a prefix that changes before a vowel. One call stages the form on an entry that already exists, or that an earlier command added to the same Draft, and links the conditions you name. It adds no entry, sense or grammatical info and leaves the lexeme form and every other form as they are.

```sh
motif compose-author-alternate-form --project language.fwdata --draft new-words \
  --intent '{"entry":"<entry-id>","form":"mw","formWs":"sw","morphType":"<prefix-morph-type-id>","environments":["<before-vowel-id>"],"inflectionClasses":["<class-1-2-id>"]}'
```

`form` is written in the vernacular writing system `formWs`, exactly as typed: Motif stores the form as given and neither adds nor strips hyphens. `morphType` is the id of an existing morph type and must suit the entry: a stem or root type for a stem entry, an affix type for an affix entry. An affix entry whose lexeme form or other alternates are affix processes takes a plain affix form too; plain and process forms mix in one order.

Each form states its own conditions, and none are copied. `environments` lists existing phonological environments the form occurs in. `inflectionClasses` lists existing classes for an inflectional grammatical info the entry has, owned by that info's category or a category above it; the form then attaches only to stems of those classes for that inflectional analysis. FieldWorks ignores these form classes for a derivational analysis; use `compose-edit-derivational-affix-restriction` to set its From Inflection Class and From Exception Features. If the entry has no inflectional grammatical info, Motif refuses `inflectionClasses` and names that composer. Leave either field out for no condition. Empty lists and repeated ids are refused, and so is an environment whose pattern names a natural class that does not exist: write the class's abbreviation, such as `[V]`, not its name.

Alternate forms have declared precedence: an eligible earlier form can block a later one. `placement` names the neighbours of the insertion gap, as `{"after":"<form-id>"}`, `{"before":"<form-id>"}` or both. Adjacent means the two are next to each other now. Leave it out and Motif chooses the gap: a form with environments goes just before the entry's first form without environments, and any other form goes after the last alternate form. A form with no environment always wins over every later form, so an explicit placement of a restricted form after an unrestricted one, or of an unrestricted form before a restricted one, is refused and the refusal names where to place it instead. In a `compose-batch` file a neighbour can be a label such as `@circ:suffix`. The lexeme form is always the last candidate.

Add `--cite-source <text>` and optionally `--cite-locator <text>` to record the source on every operation this command produces.

The command returns the new form's operations and created entity id. Dry Run and Trial the Draft on affected words, revise it, then Finalize it for a person to review and Apply.

## Swapping the lexeme form with an alternate form

The lexeme form is always the last candidate and is never a placement neighbour, so no alternate form can be placed after it. To make an alternate the elsewhere form, or to swap the two, do it in two Drafts so each sees the other's result: first `compose-remove-alternate-form` on the alternate, and `compose-rewrite-lexeme-form` to give the lexeme form the alternate's text; then `compose-author-alternate-form` to add the old lexeme text as an alternate with its own `environments`, placed as the refusal text directs. An unrestricted alternate always sits ahead of the lexeme form, so it would hide a restricted lexeme form; that is why the composer refuses it and points here.

## When not to use this

- For a regular sound change across many forms, use `compose-author-phonological-rule` rather than listing a variant for each.
- Do not duplicate an unrestricted form for every stem; give each variant the narrowest environment that fits.
