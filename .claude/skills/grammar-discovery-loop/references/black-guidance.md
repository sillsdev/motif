# Andy Black's guidance, for grammar and GDR decisions

Read before writing or changing a GDR and before choosing a mechanism. The ladder is in
[mechanism-ladder.md](mechanism-ladder.md). Line numbers (P:) refer to Black's conceptual introduction to FLEx
grammar modelling; cite them, not this page. Repository analysis of the same material:
`docs/research/grammar-authoring/own-guidance-review.md`.

- **Fix an incorrect parse with the tightest principled mechanism that works** (P:2734-2740, 3971). Black gives
  no decision rule between rule and list, no numeric cap on ad hoc rules or exception-feature holders, and
  says nothing on nonword testing; `rule-hypothesis-test` and the size delta fill that gap.
- **Allomorph environments** use slash and underscore notation, are ORed per allomorph, and allomorph order is
  disjunctive (P:3108-3205). Unique natural-class abbreviations (P:3097). Narrowing a rule's left context alone
  did not stop over-generation in CEB 10-N1; put the context on the allomorph.
- **Inflection classes and features** when the variation is lexical, not phonological (P:1108-1226).
  Stem names when stem allomorphy is conditioned by inflection features (P:3635-3753).
- **Exception features** are "not true features" and make productivity restrictions (P:2246-2248): an affix is
  allowed only when the stem carries its feature (P:2208-2213). Describe each as meaningfully as you can.
- **Clitics** (P:2626-2734): set in Attaches To Categories; only one clitic in an orthographic word may carry
  the clitic type, others are proclitic or enclitic in order.
- **Ad hoc rules** are a last resort, tightest first (adjacent, somewhere, anywhere; P:3944-3975); a group of
  them hints at a missing principled mechanism (P:2820-2840).
- **Derivational versus inflectional and the kind of reduplication** are the analyst's call, not hard and fast
  (P:2106, 3295).
- **Null allomorphs** can slow the parser (P:4078); avoid them without need.

Sources (author's machine, provenance only; this page is the working copy):
`black-flex-conceptual-intro-fulltext.txt` via `black-digest.md` (CEB review, 2026-11) and
`ceb-black-advice-20261110.md` (CEB enclitic-g advice). Sections 2.2.1-2.2.5 and 3.4 were not read in full.
