# Create a compound rule

Create an endocentric or exocentric compound rule, which FieldWorks holds as a compound rule with a left member, a right member and, depending on the kind, a head or a result. The parser then accepts a stem in the left category followed by a stem in the right category as one word.

```sh
motif compose-author-compound-rule --project language.fwdata --draft <name> \
  --intent '{"kind":"endocentric","left":"<noun-category-id>","right":"<noun-category-id>"}'
```

- **Endocentric:** one member is the head and gives the compound its category. `head` is `right` unless you say `left`, as in FieldWorks. `result` is optional and, if given, overrides the head's category.
- **Exocentric:** neither member is the head, so `result` names the compound's category and `head` is not allowed.
- `left`, `right` and `result` are category (part of speech) ids; a member also matches stems in a subcategory. `name` is optional.

The rule is created enabled and appended after the project's existing compound rules. The Dry Run states it plainly, for example "endocentric: noun + noun makes a noun (the head's category); the right member is the head", and warns when no entry has a stem analysis in a member's category. The command refuses an unknown category and a rule the project already holds, in the project or earlier in the Draft. Review the Dry Run and compare Trials with known compounds before a person Applies the Proposal; the CLI and MCP both stage a Draft, and MCP cannot Apply it.

This creates the rule only. It does not set a linker form, member features or inflection classes, and it does not choose which stem allomorph or grade a member uses.

## When not to use this

- To turn an existing rule off or on, use `compose-edit-compound-rule`.
- A rule joins any two stems of its categories, so a broad one makes non-words; ask the linguist and trial known compounds first ([guide](guide:encoding/ask-the-linguist)).
