# Noun class and gender

Noun classes and genders sort nouns into groups that agreement and affixes may depend on. FieldWorks can model them two ways, and agreement systems such as Bantu noun classes use both: an inflection class says which class prefix a stem takes, and a feature carries the class to the words that agree with it (Black §2.1.2.7, Table 9).

## What the work needs

- Decide whether the grouping is grammatical agreement or an arbitrary lexical grouping. Agreement belongs in features; a purely lexical grouping is an inflection class.
- A noun's gender pair, such as a singular and plural class, goes on the stem's grammatical analysis, and its number on the class prefix's required features, so each reading shows the prefix's number.
- Create the category's classes before assigning stems or restricting affixes to them.

## Composers, in order

1. `compose-author-inflection-class` creates a class, or a subclass, under a category.
2. `compose-author-inflection-feature` declares the agreement feature when the grouping is grammatical.
   `compose-author-feature-structure` and `compose-author-feature-value` give noun stems the class feature.
3. `compose-edit-stem-inflection-class` assigns a stem to its class.
4. `compose-edit-affix-inflection-classes` restricts an affix to the classes it attaches to.

## Pitfalls

- Do not use an inflection class for a value that must participate in agreement.
- Do not invent a class for a single stem that follows the common pattern.
- Check that each class prefix appears only with the stems of its class, and that a stem with no class does not gain one.
