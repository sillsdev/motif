# Archiphonemes and the glottal stop

An archiphoneme is a sound that an underlying form carries and a rule changes. The nasal of a prefix such as `paN-` is one example: it is `m`, `n` or `ng` by the sound after it, and never `N`. LibLCM has no archiphoneme flag, and neither HermitCrab nor PanGloss treats one specially. It is a plain phoneme, so Motif authors it with [Author a phoneme](cmd:compose-author-phoneme) like any other.

## Encode the pattern

1. Choose a letter or symbol that the written language never uses for another sound, and author it as a phoneme (`compose-author-phoneme`). If the symbol is not a letter of the writing system, make it a word-forming character of the vernacular writing system first, or the form that holds it cannot be split into phonemes.
2. Give the phonemes of the surface sounds their features: define the features with `compose-author-phonological-feature`, then set them with `compose-edit-phoneme-features`. A natural class by features (`compose-author-natural-class`, `compose-edit-natural-class-features`) then picks out the conditioning sounds.
3. Write the underlying form of the affix or stem with the archiphoneme (`compose-author-lexical-entry`, `compose-author-alternate-form`), and add the rule that changes it (`compose-author-phonological-rule`).
4. Run a Trial with a word for each surface sound and with words that must not parse.

A rule that only changes the archiphoneme near some sounds leaves it in place elsewhere. If it must always change, add a rule for the elsewhere case.

## What Motif checks

Motif reports, with code `grammar.phoneme-never-used` at level information, a phoneme that no form, wordform, rule, natural class or environment uses. An archiphoneme without an underlying form, or one whose rule was never written, is reported until it is used. An inventory authored ahead of the lexicon is also reported; nothing needs changing then.

## The glottal stop

The glottal stop is an ordinary phoneme too. Two choices decide how it shows in a word:

- The orthography writes it, with a hyphen or an apostrophe or a letter. Author the phoneme with that representation. A hyphen or an apostrophe is a word-forming character only if the writing system says so.
- The orthography leaves it out where it is predictable, for example at the end of a word. Author the phoneme with a symbol that never appears in text, and let a rule delete or realise it, as for an archiphoneme.

Decide from the attested spellings, and ask the linguist when they do not decide it (guide ask-the-linguist).

## Pitfalls

- Do not author a second phoneme for a surface variant that a rule can derive.
- Do not remove an unused archiphoneme while a rule that will use it is still planned.
- Segment classes in a rewrite rule behave the same in a featureless project and in one with features (guide nasal-assimilation).
