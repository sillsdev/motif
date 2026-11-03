# 0087. Phonological features are authored like any LibLCM feature, and an archiphoneme is a phoneme

An Andy Black-style rule morphophonology needs three things a linguist or a bot could not author through Motif:
the phonological features themselves, feature bundles on the phonemes, and the feature constraints of a natural
class. Motif could read a feature system and refer to its features, but only a person in FieldWorks could create
one. This ADR closes that gap and states what Motif does not add.

**Status:** accepted. Applies [ADR 0076](0076-grammar-data-never-belongs-in-motif.md) (generic only, no language
code) and the rule that if LibLCM can hold a construct, Motif allows it.

## Decision

1. **Features live in `LangProject.PhFeatureSystemOA`.** `compose-author-phonological-feature` creates a closed
   feature (name, optional abbreviation, one or more values) and `compose-add-phonological-feature-value` adds a
   value to one. Both reuse the generated `FsFeatureSystemFeatures` and `FsClosedFeatureValues` operations that the
   inflection-feature composers use on the morphosyntactic system. Names and abbreviations are unique among the
   system's features, and among one feature's values.
2. **Bundles are set, not replaced.** `compose-edit-phoneme-features` and `compose-edit-natural-class-features` take
   `set` (feature and value pairs; a feature already specified takes the new value in place) and `clear` (features to
   remove). A phoneme without a feature structure gets one. A feature-defined natural class must keep at least one
   constraint, because an empty feature class cannot condition an environment; a segment class has no constraints and
   is refused with a pointer to `compose-edit-natural-class`.
3. **There is no archiphoneme flag.** An archiphoneme is a plain phoneme in LibLCM, and in HermitCrab and PanGloss.
   Motif adds Help (guide `archiphonemes`) and one information finding, `grammar.phoneme-never-used`, for a phoneme
   that no form, wordform, rule, natural class, environment or insertion names. The finding is information because a
   sound inventory authored ahead of the lexicon is legitimate. The glottal stop is likewise an ordinary phoneme; the
   guide gives the two surface choices (a letter in the orthography, or a rule).
4. **Defining a feature changes how segment classes behave.** In a featureless grammar a rewrite rule that names a
   segment class is read differently from one in a featured grammar (PanGloss issue 120). The Help for the
   composers says so before a project goes from featureless to featured; the composers themselves do not refuse.
5. **Not in this slice.** Feature-change rule output and alpha variables, multi-segment rules, rule editing and
   metathesis stay in later slices of the same plan. MCP never applies: every composer stages operations in a Draft.

## Accepted cost

The `grammar.phoneme-never-used` check reads every form and wordform once as one string, so it adds a full pass over
the lexicon to Check. It matches a phoneme's spelling as a substring, so a short spelling inside a digraph counts as
used; the finding errs toward silence.
