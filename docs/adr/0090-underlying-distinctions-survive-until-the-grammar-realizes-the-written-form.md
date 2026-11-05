# ADR 0090 — Underlying distinctions survive until the grammar realizes the written form

**Status: Accepted.**

FieldWorks lexical forms may contain phonological distinctions that running spelling omits. Motif authors those distinctions as ordinary represented phonemes, phonological features and lexical/affix forms, and lets the grammar derive the written word. We retain them because subsequent morphology and phonology may depend on them; normalizing them away at input cannot express that dependency.

1. A phoneme used by the parser has a nonempty representation in the loaded default-vernacular phoneme table. Material absent from written output is realized or deleted by ordered FieldWorks rules. Underlying forms are not marked abstract merely because their symbols do not occur in text. Motif adds no hidden-phoneme flag, duplicate lexicon or independent orthography runtime.
2. A quantity, stress, tone or segmental distinction that conditions grammar remains distinguishable in the underlying inventory and forms. Aliases are appropriate only where they denote the same represented phoneme for the grammar's purpose. A final spelling realization may neutralize distinctions after their linguistic work. Its position and domain are explicit in rule order and, where required, effective parser strata.
3. Motif exposes the semantic authoring and read-back needed for the FieldWorks objects and effective parser configuration. It refuses malformed, unresolvable or unsupported edits, names engine limitations truthfully, and does not replace a legal linguistic analysis with a preferred one. Grammar Health and phonology examples advise; they do not prohibit a linguist's supported choice or silently become readiness gates.
4. Parsing Assessments take the actual written word and retain every supported lexical/morphological reading. Absence of an underlying segment from the written word is not a segmentation error. Unmarked spelling does not justify selecting a sense, folding approved expectations or discarding ambiguous analyses. Completion and unavailable identities remain separate facts.
5. A spelling-only check cannot prove a distinction erased before output. Claims about quantity/stress or another neutralized effect require producer-backed derivational evidence under an explicit source/context/checkpoint, with real completion facts. Motif stores that evidence; it does not reconstruct engine behavior.
6. Restore lexical information only from selected evidence with exact identity and source Citations. Keep unresolved legacy forms until their own evidence is available. Source spellings may remain in Citation Form; parser forms hold the chosen underlying representation. Reference-safe migration and retirement require the ordinary Proposal, Dry Run and Trial workflow.

**Consequences:** inverse deletion and neutralization may increase parser work and ambiguity across many words. Measure them, bound incomplete searches honestly and improve performance without deleting evidenced distinctions. There is no native syllable/mora claim merely because a segment has a stress feature. A richer domain unsupported by FieldWorks/HermitCrab needs an explicit capability proof and upstream work. Language-specific inventories, serialized fixtures and migration payloads remain outside Motif under ADR 0076.

**Alternatives considered:** enumerate source-supported surface allomorphs as a transitional bridge; erase distinctions with aliases or a pre-parser fold; implement a separate orthography layer. The bridge remains useful but cannot supply missing underlying conditioning. The latter two lose the authoritative derivation or duplicate it outside the FieldWorks grammar.

## Initial proof domain and engine limits

The first parser target is unmarked running text; marked dictionary citations remain in Citation Form.
Wolff's documented distinctions are the source baseline: acute denotes quantity, grave a following glottal
stop, and caron exceptional stress. These are separate facts rather than interchangeable accent aliases.

The bounded proof uses simultaneous regular rules (`multipleApplicationOrder="simultaneous"` in the
FieldWorks export shape), ordered on the Morphology stratum with empty Clitics and Surface strata.
`NotOnClitics=true`, empty `HC/Strata` and `HC/DelReapps=0` make this domain explicit; 0 is the
FieldWorks deletion-reapplication default, not a ban on inverse deletion. This does not establish domains
for attached clitics or multiple cycles; those need separately proved effective strata.

The reference-engine comparison agrees on the probe analysis sets apart from a rendering label, but hc.dll
hangs beyond 600 seconds on `pat-a` with the full simultaneous chain (also with its wrong-order control).
PanGloss 0.15.2 completes it. Keep this word in the default-engine proof and report the hc.dll limit;
do not change rule mode, split strata or drop the word to claim parity. That comparison used XML probes
and loader-source inspection, not an observed FieldWorks export of the combined grammar or a new Windows
FieldWorks gate.
