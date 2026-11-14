# Mechanism ladder (Andy Black)

Fix an incorrect parse with the tightest principled mechanism that works. Climb only when the lower rung fails
its test, and record each rung tried. Source: Black's conceptual introduction to FLEx grammar modelling,
digest at `/home/johnm/work/ceb-review-opus/reports/black-digest.md` and the enclitic-g advice
`_briefs/harness-resume-2026-10-23/ceb-black-advice-20261110.md`; cite the paper's line, not this page.

1. Categories and co-occurrence (what attaches to what).
2. Allomorph environments: slash and underscore notation, ORed per allomorph; allomorph order is disjunctive;
   unique natural-class abbreviations. Put the context on the allomorph, because narrowing a rule's left context
   alone can leave over-generation untouched (10-N1).
3. Inflection classes and features, when the variation is lexical rather than phonological; stem names when
   conditioned by inflection features.
4. Exception features (productivity restrictions): "not true features", positive only (an affix is allowed
   when the stem carries the feature), so a minority cannot be tagged on affixes; costs host tagging.
5. Separate templates or entries, including a separate clitic entry (one clitic per orthographic word may carry
   the clitic type; others are proclitic or enclitic in order).
6. Ad hoc co-occurrence rules, tightest first: adjacent before or after, then somewhere, then anywhere.
   Last resort; a group of them hints at a missing principled mechanism.

Black sets no numeric cap and no decision rule between rule and list, and says nothing on nonword testing;
the loop's controls and size delta fill that gap. Avoid null allomorphs without need (parser speed).
Constructs a real FieldWorks project cannot create are out of scope (ADR 0112).
