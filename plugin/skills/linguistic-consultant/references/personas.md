# Personas and scenarios

**Scope:** FieldWorks 9 family; FLEx 9.3 WinForms terminology baseline from the September design.
Help branch: `sillsdev/FwHelps:markdown-export`, no release tag verified here.
Model vocabulary: `SIL.LCModel 11.0.0-beta0182`. This reference promises no current screen layout.

All five personas are inferred from the September design and cited literature, not interviews or
population statistics. They guide an explanation; do not infer someone’s competence from identity.

| Persona | Need and constraints | UI/workflow review scenario | Grammar/report scenario |
|---|---|---|---|
| Mother-tongue translator | Recognizable words, practical literacy terms, usable community materials | Explain why an analysis color changed using a word and reading, not an engine class | Ask whether bare `lum` can mean plural before making plural marking optional |
| Graduate field linguist | Record provenance, test hypotheses, retain uncertainty during analysis | Evaluate whether an uncertain segmentation can remain explicit while texts are annotated | Distinguish a paradigm gap from a parser failure; request elicitation rather than invent a rule |
| Lexicography consultant | Stable entries and senses, review many records, prepare publication | Check that a gloss correction is visible in the intended sense and writing system | Separate a lexical correction from a grammatical restriction; preserve other senses |
| Morphologist | Explain paradigms, exceptions, grammatical features and allomorphy | Assess whether a report exposes which contrast a recommendation preserves | Decide whether lexical class or phonological environment predicts `-ta` versus `-na` |
| Computational evaluator | Reproducible identities, completion, evidence scope and resource limits | Check that summary counts link to completed evidence and distinguish missing results | Compare all approved morphologies with confirmed analyses, preserving incomplete searches |

Pick a leading persona and any conflicting needs. A computational evaluator’s reproducibility need
can accompany a translator’s readable explanation. Every scenario above is invented.
