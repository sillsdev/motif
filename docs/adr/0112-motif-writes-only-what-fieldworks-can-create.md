# 0112. Motif writes only what FieldWorks can create

**In plain terms:** every change Motif makes to a project is a change a person could have made in FieldWorks, or one
that FieldWorks's own code makes. A project Motif has edited never holds a shape FieldWorks would not accept or
could not have produced, and when Motif refuses something "as FieldWorks does", FieldWorks really does refuse it.

**Status:** accepted, 2026-11-12 (owner rule). Builds on [ADR 0038](0038-expectations-are-fieldworks-approved-analyses.md), where
FieldWorks-approved analyses are the ground truth, and on the conformance suite's producible-only rule: a fixture
stays in the shared suite only when FieldWorks can produce it.

## Context

Motif refused to delete the last sense of an entry "as FieldWorks does". That claim was false for variants: LibLCM's
`CreateVariantEntryAndBackRef` (`OverridesLing_Lex.cs:3196-3225`) makes a variant entry with a lexeme form and a variant
reference and no sense, and a real project holds thousands of them. The refusal blocked a valid clean-up
(motif#114) and rested on a wrong reason. The opposite error, writing a shape FieldWorks cannot create, would
hand the next FieldWorks session or the parser data it may mishandle.

## Decision

1. **Producible only.** Every composer and every operation produces only shapes the FieldWorks UI or LibLCM's own API
   paths produce.
2. **Each composer cites its path.** A composer's definition names the LibLCM method or the FLEx user action it
   reproduces, as a code reference or as the UI steps. A composer with no such path is not added.
3. **Refusals tell the truth.** A refusal or message that says "as FieldWorks does" is made only where FieldWorks (or LibLCM)
   really refuses the same thing. Where Motif refuses something FieldWorks allows, the message gives Motif's own reason.
4. **Judgement stays free** ([ADR 0079](0079-linguistic-decisions-follow-black-and-judgement-calls-are-the-linguists.md)):
   this rule limits the shape of the data, not which analysis a linguist prefers.

## Consequences

- The last-sense refusal narrows to what is true: an entry may lose its last sense only when it stays a variant of a
  component that has a stem analysis.
- The variant composers (`compose-strip-variant-senses`, `compose-author-variant-entry`) cite
  `CreateVariantEntryAndBackRef` and FLEx's Create Variant dialog.
- Review of a new composer checks the cited path.
