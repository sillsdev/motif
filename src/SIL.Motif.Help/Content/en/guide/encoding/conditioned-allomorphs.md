# Phonologically conditioned allomorphs

A linguist sees one morpheme with different spoken or written forms in different sound contexts. FieldWorks can keep those forms together and condition each one on its surrounding sounds.

## Encode the pattern

- Keep the variants under one lexical entry when the evidence supports one morpheme.
- Give each variant the sound environment where it occurs. Use a natural class when several sounds share the relevant property.
- Put the narrowest conditioned forms before a less restricted or elsewhere form.
- Do not duplicate the same unrestricted form for every stem. Do not use an environment broader than the evidence, such as “after any consonant” when only one class is supported.
- Check whether the forms are truly one morpheme before treating their difference as phonological conditioning.

## Motif support

`motif_lexicon` with detailed results can read existing allomorphs and their environments. `motif_grammar` can read natural classes and environments. `motif_add_phoneme`, `motif_add_natural_class` and `motif_add_environment` create the sound inventory and context. `motif_add_lexeme_form` changes an existing lexeme form. `motif_add_alternate_form` creates a stem or affix alternate under an existing entry. It accepts optional sound `environments` and, for affix forms, optional `inflectionClasses`. When alternates already exist, supply an exact `placement` gap. `motif_add_affix_process_allomorph` adds a pattern-based form (an affix process with its own input and output) to an affix entry whose other forms may be plain allomorphs or processes; it takes no environments and applies to all of the entry's grammatical info. `motif_add_alternate_form` likewise adds a plain affix allomorph beside process forms. Both take `inflectionClasses` for inflectional grammatical info; the parser ignores them for a derivational analysis (the stem features an allomorph requires, `MsEnvFeatures` in LibLCM, are read for a derivational affix too, but Motif has no composer for them yet), so a mixed entry can use them for its inflectional analysis while restricting the derivational analysis through the `motif_edit_derivational_affix_restriction` MCP tool or `compose-edit-derivational-affix-restriction` CLI verb (guide suffix-prefix-slots). When the entry has no inflectional grammatical info, Motif refuses `inflectionClasses` and points to that composer. `motif_edit_allomorph_condition`, when selected by the profile, can link an environment to an existing allomorph.

## What an allomorph can and cannot carry

FieldWorks holds the meaning of a morpheme on the entry, not on its forms. So one form cannot have its own tense, gloss or slot.

| An allomorph can carry | An allomorph cannot carry |
|---|---|
| Sound environments the form occurs in (`environments`) | Its own gloss, tense or other label |
| Inflection classes of the stems it attaches to, for an inflectional analysis (`inflectionClasses`) | Its own grammatical info or slot |
| For an affix, the stem features and category it needs (conditions on the stem it attaches to) | A different category or meaning from the entry's other forms |
| For a stem, a stem name tied to inflection features | A rule to apply or skip (use exception features on the analysis) |
| Its place in the order of the entry's forms | Anything that changes which senses it serves |
| Being abstract (a form no word shows) | |

When the form needs a different label, as a past form beside a future one, make it its own entry with its own grammatical info, slot and sense. Two analyses on one entry do not help: the parser gives every form of the entry to every analysis, so both labels would apply to both forms.

## Two forms that must not block each other (free variation)

An earlier form with an environment blocks every later form of the same entry wherever it matches, the lexeme form (always last) included. HermitCrab's one exception is free fluctuation: neighbouring forms whose environment sets are exactly equal are not disjunctive, so both parse and neither blocks the other. For an affix this is the only encoding; an affix variant entry is not read by the parser. A stem can also be a variant entry with no sense of its own, which the parser loads as a separate stem, but that is a different morpheme, not a form of this one.

- To let the plain suffix `an` and a variant that starts with a hyphen character, `-an`, both parse after any sound, give the entry two forms with no environment. `motif_add_alternate_form` with no `environments` does this; leave the new form last, beside the unrestricted lexeme form. A real-parser test (`AuthorFreeVariationParserTests`) shows two spellings (`an` and `ang`) both parse after `o` and after `a`.
- Two alternates that carry the same environments are free variation with each other (the same test file shows both parse where the environment matches, and the plain lexeme form is blocked there). A form whose environments differ from its neighbour's is not free variation. An alternate restricted to "after a consonant" blocks the lexeme form after every consonant, so the plain spelling stops parsing there. The parser has no way to say "this form where the environment matches, and the plain form everywhere as well".
- Best workaround when the variant should occur only in one context but the plain form must stay possible there too: make the variant unrestricted (free variation) and accept that it also licenses the variant in other contexts, such as a hyphen after a vowel; measure that over-generation with a Trial.

## A hyphenated spelling of an affix process

An affix process cannot carry a written alternate form: it has no spelling of its own, only an input and an output. When the text writes a hyphen between the affix and the stem (Cebuano `l`-forms and `X-X` reduplication), make the hyphen part of the output. Add a second process to the same entry with `compose-author-affix-process-allomorph` (`motif_add_affix_process_allomorph`) whose output inserts the hyphen phoneme where the text writes it, for example `{"insert":["<i>","<k>","<a>","<hyphen>"]},{"copy":"rest"}` beside the process that inserts the same sounds without the hyphen. The hyphen must be a phoneme (`compose-author-phoneme`). LibLCM holds the entry's processes as ordered alternate forms of one entry, so both spellings share its gloss and grammatical info and the real parser reads either; for full reduplication a single process that copies the stem, inserts the hyphen and copies it again already gives the hyphenated spelling, so no second process is needed. A plain written alternate (`compose-author-alternate-form`) is refused on an entry that has a process.

Do not add a shape-restricted solid form to the entry of an unrestricted hyphen form. A form earlier in the order blocks every later form of the entry for the stems it matches, so a solid form restricted to, say, three-sound stems stops the hyphen spelling of exactly those stems (`RealParserReduplicationBlockingTests` shows `pul-pul` lost while `kaun-kaun` still parses). That is the loss an earlier round saw when it put shape forms on the hyphen entry. Give the restricted solid form its own entry: both spellings then parse, each by its own entry.

## One form blocked after one form of another affix

Sometimes one form of an affix is wrong only after one particular form of an earlier affix: a stative `-lik` fits after an applicative `-o` but not after `-e`, while `-k` fits after both. Create an allomorph prohibition with the CLI verb `compose-author-adhoc-prohibition`, which MCP offers as `motif_author_adhoc_prohibition` (kind `allomorph`; the later form as `first`, the earlier form as `rest`, adjacency `adjacent-to-right`). The parser drops only that pair of forms. Use it between forms of different entries: in an inflectional template an entry fills one slot, so a rule between two forms of the same entry would never fire there. To choose between forms of one entry, give each its own environment, or put the narrowest first and the general form last.

## Every sound except some, and a zero form chosen by class

A natural class is a fixed list of sounds (or of feature values), and FieldWorks has no complement or negated class. A project file can hold a negated feature value (LibLCM stores one), but FieldWorks' parser loader cannot read it and FieldWorks offers no way to make one, so do not rely on it; the grammar check warns (`grammar.negated-feature-value`) when a project holds one. For "every consonant except `<sound>`", list the sounds wanted (author the class with `motif_add_natural_class`) and add a sound to it when the inventory grows. A zero affix that should depend on the stem's class, not on the sounds around it, takes `inflectionClasses` (`motif_add_lexical_entry`, or `motif_add_alternate_form` for a form of an existing entry) and no environment; a stem of any other class gets no reading from it.

## Circumfix parts

Each part of a circumfix keeps its own environment on the real parser (a real-parser test, `RealParserCircumfixEnvironmentTests`, authors `ma-` with `-ang` after a consonant and `-x` after a vowel as two circumfix entries, each part with its own environment, and the parser chooses by the stem's last sound; the prefix part's environment is tested at the stem start and the suffix part's at the stem end). A part whose environment names the other part's side can never hold, so give each part a context on its own side of the stem. Run a Trial with the words each way: a missing half, the wrong linker and a stem the prefix environment excludes all stay unparsed.

## Check with a Trial

After a composer can express the change, include attested words from each sound context and words just outside those contexts. Confirm each variant appears where expected and that the unrestricted form does not win before a narrower variant. Include words that should remain unchanged.

## Ask the linguist

Ask when the evidence does not show whether the variants share a morpheme or what sound defines the environment. Use real project forms: “I found `<attested form A>` before `<observed sound class A>` and `<attested form B>` before `<observed sound class B>`. Does `<variant>` also occur before `<attested sound C>`, or is that a separate morpheme?” Replace bracketed items with forms read from this project; label any unobserved form as a prediction.

## Common wrong choices

- A separate entry for each variant of one morpheme.
- An environment broader than the evidence, or an unrestricted form placed before narrower ones.
- Listing variants for a regular sound change across many forms; a phonological rule fits that better.
- A new entry for each pattern-based form of one affix, or a plain alternate form for a pattern change; use `motif_add_affix_process_allomorph`.
