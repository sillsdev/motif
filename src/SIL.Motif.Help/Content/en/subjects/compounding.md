# Compounding

A compound joins two stems into one word. FieldWorks allows it through compound rules, and a rule it creates by default can accept any stem followed by any stem, so it can build words the language does not have.

## What the work needs

- Check whether the language really compounds the way the rule says. A rule that is too general makes many non-words; count a sample of the words it builds before keeping it.
- Turn off a rule that accepts more than the evidence supports, instead of deleting it, so it can be switched back on.
- If the language does compound, the rule's left and right members and its head decide what it accepts. Those are edited in FieldWorks today; Motif can turn the whole rule off or on.

## Composers, in order

1. `compose-edit-compound-rule` turns one compound rule off or on. Give the rule's id, the Disabled value you read, and the one you want.

## Pitfalls

- Turning a rule off removes every word it builds, including real compounds. Test known compounds before and after.
- A new Assessment is needed to see the effect on the words the parser accepts; compare it with the one taken before the change.
