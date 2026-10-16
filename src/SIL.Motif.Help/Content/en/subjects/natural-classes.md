# Natural classes

A natural class is a named group of sounds that behave alike, such as the vowels or the voiceless stops. Environments and phonological rules refer to the class instead of listing every sound.

## What the work needs

- The member phonemes must exist first. Members may be phonemes already in the project or phonemes created earlier in the same Draft.
- A class should be as narrow as the evidence. A class of all consonants where only the nasals behave alike will let a rule apply to words it should not touch.
- Give each class a unique abbreviation; environments name classes by abbreviation (Black §3.1).
- When a class is wrong, correct its members rather than adding a second class for the same sounds.

## Composers, in order

1. `compose-author-natural-class` creates a class with its members.
2. `compose-edit-natural-class` adds or removes members of an existing class.
3. `compose-relink-natural-class` moves the environments and rules that use one class onto another, when a replacement class is the cleaner fix.

## Pitfalls

- Editing a class changes every environment and rule that uses it. The edit reports the affected users; read them before finalizing.
- Do not rename or duplicate a class to avoid reading what depends on it.
- Check the change with words that should keep their analyses and words in the affected sound contexts.
