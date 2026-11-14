# GDRs: the human interface, always in sync

People interact with the grammar through its Grammar Design Records. A GDR is the linguistic analysis as
linguists write it: prose, symbolic notation and worked examples ("this phonological rule does X; see these 5
examples"). Claude translates GDRs into FieldWorks rules. A GDR is a living page per topic with a permanent
`GDR-n` id, not an ADR; the why of each change stays in Proposals.

## Sync rule

The owner's decision (#178): a Proposal that changes a rule, affix process or the objects behind a GDR section
changes that section in the same Proposal. The product does not enforce this yet: the GDR spec has Finalize
warn on a missing section and does not require a Proposal to update a GDR, and the automated sync check is
future work (#178). Under this skill the agent enforces it as a self-check: a rule with no GDR section, or a
section whose examples no longer parse as stated, fails the completion criterion and the Proposal waits. Done when every rule and affix process touched maps to a GDR section and every example in it was
re-read from the grammar as it stands after the change (live examples, not remembered ones).

## Before writing or changing a GDR

Consult Andy Black's guidance and the other research each time: [black-guidance.md](black-guidance.md),
[mechanism-ladder.md](mechanism-ladder.md) and `docs/research/grammar-authoring/own-guidance-review.md`. State in the GDR which
mechanism was chosen and why a lower rung did not do. Read the GDR index at Proposal start and the section
bodies of the topics you touch.

## Shape of a GDR

Template spec: `docs/superpowers/specs/2026-11-02-grammar-design-records-design.md` (23 topics in 7 groups;
sound processes 8, rule ordering and exceptions 9, allomorph selection 18, exceptions and false parses 22 are
the ones rule hypotheses touch). Text is FieldWorks Markdown
(`docs/superpowers/specs/2026-11-03-gdr-markdown-design.md`; links such as `[[rule:Name]]`, `[[w:word]]`,
`{v}` for vernacular). Sections: Abstract, Analysis (prose and symbolic notation), Examples (a table of stem,
form, gloss, 5 or more rows, including an exception), Exceptions, Open questions, Sources.

```
GDR-18 Allomorph selection
Abstract: Nasal prefixes such as [mang-]{v} assimilate to the stem's first consonant, which often drops.
## Analysis
One underlying form; the nasal takes the place of a following consonant and voiceless stops delete.
Stated as one rule, [[rule:Nasal assimilation]], because it is regular across stems.
## Examples
| Stem | Prefixed | Gloss |
| [[w:palit]] | [[w:mamalit]] | 'buy' |
## Exceptions
Lexically marked stems keep their first consonant: [[exc:Keeps first consonant]].
```

Write a GDR in the shape of its hypothesis: the pattern, the rule in notation, the predicted forms that were
attested, the exceptions with polarity, the verdict from `rule-hypothesis-test`, and the speaker judgements
that settled open points, and any FieldWorks limitation that forced the majority to carry a tag. The speaker
never sees the GDR. Linguists comment on the Proposal, not on the GDR.

## Route

An agent writes a GDR only through a Proposal, as a Motif operation effective at Apply; people edit in
FieldWorks directly. A GDR edit remembers its base text; when it no longer fits, stop on that GDR and
amend, never overwrite.
