# Compounding

A compound joins two stems into one word. FieldWorks allows it through compound rules, and a rule it creates by default can accept any stem followed by any stem, so it can build words the language does not have.

## What the work needs

- Check whether the language really compounds the way the rule says. A rule that is too general makes many non-words; count a sample of the words it builds before keeping it.
- Turn off a rule that accepts more than the evidence supports, instead of deleting it, so it can be switched back on.
- If the language does compound, the rule's left and right members and its head decide what it accepts. `compose-author-compound-rule` creates a rule from its two member categories, its head and its result category; Motif can also turn a rule off or on. Linker forms, member features and inflection classes, and a stem allomorph or grade chosen per member, are still edited in FieldWorks.

## Composers, in order

1. `compose-author-compound-rule` creates an endocentric rule (one member heads it, and the right member does unless you say left) or an exocentric rule (you name its result category). It joins any stem in the left category to any stem in the right, and subcategories match too.

2. `compose-edit-compound-rule` turns one compound rule off or on. Give the rule's id, the Disabled value you read, and the one you want.

3. `compose-set-no-default-compounding` turns the parser's built-in default compounding off or on for the whole project. Use it when the language has no compound rules and the parser still accepts words that join two stems.

`compose-author-compound-rule` is offered by default because a grammar cannot compound without it, while turning a rule off is an advanced switch.

## Pitfalls

- The parser compounds by default even when the project has no compound rules at all; turning that off removes every word built that way, so test known compounds before and after.

- Turning a rule off removes every word it builds, including real compounds. Test known compounds before and after.
- A new Assessment is needed to see the effect on the words the parser accepts; compare it with the one taken before the change.
