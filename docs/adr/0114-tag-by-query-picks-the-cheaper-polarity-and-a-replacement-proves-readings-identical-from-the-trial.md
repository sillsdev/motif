# 0114. Tag by query picks the cheaper polarity; a replacement proves identical readings from the Trial

**In plain terms:** Motif can give one exception feature to every stem that matches a question, and says up front how
many that is. Where FieldWorks lets a rule either require or exclude the feature, Motif tags whichever side is smaller.
Where FieldWorks only lets an affix require it, Motif says so rather than inventing a way around it. When a rule
replaces a list of alternates, the review shows, from the ordinary Trial, how many readings of the affected words
were lost or gained.

**Status:** accepted, 2026-11-13. Builds on [ADR 0057](0057-explicit-allomorph-retirement.md) (replacing listed forms with a
rule), [ADR 0112](0112-motif-writes-only-what-fieldworks-can-create.md) (producible only) and [ADR 0009](0009-layered-api-primitives-and-composers.md).

## Context

A rule gated by an exception feature is tagged stem by stem, so a rule that most stems follow still carries a tag on
every one of them (motif#175). And replacing a set of alternates with one rule had a combined intent (ADR 0057) but
its review followed only the frozen human readings, so a reading the rule newly allowed, or a non-approved reading it
removed, was invisible (motif#176).

## Decision

1. **Tag by query is a composer** (`compose-tag-stems-by-query`). It evaluates a query (category, final letters, an
   existing feature, absence of a feature, gloss text) over stems in one pass, lowers to the existing
   `MoStemMsa.ProdRestrict` add or remove operations, and puts a summary first in its related objects: stems matched,
   stems tagged, polarity.
2. **Polarity follows what FieldWorks can express** (ADR 0112). A phonological rule has both required and excluded
   features, so with `gate: rule` the smaller of the matching stems and the other stems in scope is tagged (ties tag
   the matching stems and the rule requires the feature). An affix has only From Exception Features that a stem must
   carry, so with `gate: affix` (the default) the matching stems are tagged even when the others are fewer, and the
   summary records the FieldWorks limitation. No negative mechanism is invented.
3. **Untag is the same query with `action: untag`.** A project-level default for new stems is not added: it has no
   FieldWorks path, and where it would live (feature, category or rule) is not known.
4. **Identical readings come from the Trial, not from a new parse.** `ReadingIdentityProof` takes the Trial's before
   and after word rows, translates the retired forms to their survivors with the retirement's own role mappings,
   and set-diffs the readings of the affected words. It reports words compared, readings lost and gained, and lists
   each difference. It is a finding, never a refusal, because a rule can add a real, correct ambiguity. A word
   that is absent, stopped or unfinished on either side makes the result Incomplete, never Identical. The retirement review
   carries it beside the human-reading verification.

## Consequences

- Cost: tag by query is O(stems) (O(senses) more with a gloss condition); the proof is O(affected words x readings),
  reading rows the Trial already keeps and starting no parser.
- The proof compares morph identities (form, grammatical info, inflection type, guessed text). A rule that changes
  which allomorph the parser reports for an unmapped role shows as lost and new, which is the honest answer.
- A reading's rule attribution still needs PanGloss derivation evidence; the proof does not claim it.
