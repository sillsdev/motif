# Inflection classes

Inflection classes are groups of words that inflect the same way. A stem belongs to a class, a category may have a default class, and an affix can be restricted to the classes it attaches to: an inflectional affix on its form, a derivational affix on its grammatical info.

## What the work needs

- Create the class under the category it belongs to before assigning anything to it.
- Assign a stem to a class only when the evidence shows it takes that class's affixes.
- Use a category default when most stems share one class, and assign the exceptions individually.

## Composers, in order

1. `compose-author-inflection-class` creates a class or subclass.
2. `compose-edit-stem-inflection-class` sets or clears one stem's class.
3. `compose-edit-part-of-speech-inflection-class-default` sets or clears a category's default class.
4. `compose-edit-affix-inflection-classes` restricts an inflectional affix to certain classes, or removes the restriction.
5. `compose-edit-derivational-affix-restriction` restricts a derivational affix to stems of one class (and optionally with certain exception features). A class on the form of a derivational affix is ignored by the parser.

## Pitfalls

- A new default changes the reading of every stem in the category that has no class of its own. Check a sample of them.
- Restricting an affix to classes can leave stems with no matching affix. Test words from every class.
- Do not add a class to record a single exception when an individual assignment will do.
