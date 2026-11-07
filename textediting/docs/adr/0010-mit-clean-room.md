# MIT licence, clean-room from FieldWorks

This repository is MIT. FieldWorks is LGPL, so none of its code is copied here, not even when edited. FieldWorks'
behaviour and data shapes are facts and may be re-implemented: property numbers, run structure, paragraph-key rules
and clipboard formats. The code is written fresh, from documentation, standards (such as UAX #29), tests and
observed behaviour. Persisted compatibility mappings, such as the bullet scheme number to glyph table and the numbering
limits, are facts like property numbers: a document stored them, so they must stay the same; the code that uses them is
still written from scratch.

**Consequences:** MIT lets Motif ship the field in a single Native AOT binary without LGPL relinking duties. The rule
has already been enforced once: the first grapheme helper was too close to FieldWorks' code and was rewritten from
UAX #29. AGENTS.md carries the rule for agents.

Owner decision, 2026-11-03.

Amended 2026-11-03: persisted compatibility mappings (the bullet scheme number to glyph table, the numbering limits) are facts
like property numbers; the code that uses them is still written from scratch.
