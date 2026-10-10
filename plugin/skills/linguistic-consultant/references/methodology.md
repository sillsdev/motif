# Morphology methodology digest

**Scope:** FieldWorks 9 family; FLEx 9.3 WinForms terminology baseline from the September design.
Help branch: `sillsdev/FwHelps:markdown-export`, no release tag verified here.
Model vocabulary: `SIL.LCModel 11.0.0-beta0182`. This reference promises no current screen layout.

Start with a contrast the person recognizes and prove that the configured grammar accounts for it.
This is an authored digest of Black’s conceptual introduction and workshop; the parser expert owns
loader mechanics and diagnostics. Source summaries and citations are in the local bibliography.

1. Describe one paradigm in words. Record stems, affixes, grammatical readings, alternate forms and
   what is still unknown. Begin with affixes, their MSAs, slots and templates.
2. Establish which contrasts are grammatical, lexically conditioned or conditioned by sounds.
   Check inflection versus derivation by distribution and input/output properties, not by gloss alone.
3. Inspect the relevant category and sense-linked MSA. An affix’s visible spelling is not sufficient
   evidence that its grammatical information reaches the parser.
4. For inflection, inspect the template and the declared slot sequences. Required, optional and zero
   marking assert different analyses: a bare form’s interpretation needs linguistic evidence.
5. Fit allomorph conditioning to that evidence. A lexical class is not an agreement feature;
   a phonological environment needs an observed sound-based contrast.
6. Check importer warnings and confirmed analyses on positive contrasts and Reviewed negatives.
   Include a second stem or context that would defeat a tempting alternative explanation.
7. Cross-check against connected text and every approved morphology in scope. Preserve incomplete
   searches; state which contrasts were not exercised. Extend only after the first contrast is supported.

Invented example: `lum-ta` is approved as plural; `lum` is approved as singular. That alone does not
show that bare `lum` is also plural. Ask for that judgment before allowing the plural slot to be omitted.
If a supplied completed parser result already licenses bare plural and it is a Reviewed negative,
report the restriction problem and the evidence; do not claim that an observed spelling lacks a word entirely.

PanGloss’s FST proposes analyses and HermitCrab confirms them. The parser controls the executable fact
of whether a given confirmed reading is built, under a named mode and limit. It cannot establish the
speaker’s judgment. The internal `fix-a-grammar` skill concerns compiler strategies and proposer recall;
its engine “oracle” is separate from Motif’s versioned model-and-human Oracle recipe.
