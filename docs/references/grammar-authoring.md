# Grammar authoring findings

FieldWorks authoring, its HermitCrab loader and PanGloss's separate project reader are different surfaces. A construct that FieldWorks can store is not automatically imported or executed by both parsers. The authoring recipes in the FieldWorks grammar-authoring skill describe the model and known loader boundaries; their source investigations are identified below.

## Model and loader

Create dependencies before references: writing systems and sound inventory, features and natural classes, environments and parts of speech, then entries, allomorphs, analyses, slots, templates and rules. Attach owned objects before setting properties that depend on their owner. The project owner supplies the cache and persists it.

- Register a created writing system with its manager and add it to the appropriate current list. The first phoneme set is the loader's input; phonemes need usable vernacular codes. One unloadable member can cause a natural class to disappear.
- Environments are strings and need validation. Invalid affix environments can widen rather than restrict applicability. Allomorph order matters: the loader examines alternate forms before the lexeme form and an earlier eligible form can block a later one.
- Entries own their MSAs; senses refer to them. An inflectional MSA with slots is reached through its template. Morphological and phonological feature systems have different owners; feature values, slot membership and inflection classes must be wired to the correct model objects.
- Write rule contexts to the rule branch that owns them. Ordered rule interaction, affix processes, co-prohibitions, exception features and compounds require their own parser checks. The model's stratum objects are not equivalent to the loader's parser-parameter strata text.
- Deleting a stratum can leave dangling references. Removing a reference to an owned collection member does not delete it. A `LexReference` has member and target-type constraints; do not assume every list has the same root shape.
- Custom-field schema changes cannot occur inside the data unit of work. Use NFD for plain text and resolve custom fields by owner and name. LibLCM computes homograph numbers.

Flexicon's inspected Python implementation is LGPL-2.1: these are independently expressed observations, not permission to copy its implementation. The 2026-09-28 harvest pinned its source and related tools in the original investigation. Grammar Health and Parsimony advise about an analysis; a rule proposal needs evidence that readings remain as intended. Observations of repeated alternations in one project identified 40 written pairs, 15 uniquely tokenized pairs and a 14-root group; that XML inspection did not establish that a rule preserves readings. No language forms are retained here.

Black's guidance distinguishes over-generation from coverage of attested words: an attested-word list alone cannot expose words the grammar wrongly permits. Evidence and comparison are needed before treating a suspected generalization as a decision. The guidance review also cautioned against asserting that an empty affix always costs more parser work; measure the actual grammar and engine.

Sources: `docs/flexicon-harvest.md@0697348c8`; `docs/research/grammar-authoring/liblcm-recipes.md@0697348c8`; `docs/research/grammar-authoring/flexicon-harvest-grammar.md@0697348c8`; `docs/research/grammar-authoring/own-guidance-review.md@0697348c8`; `docs/research/2026-08-08-grammar-breadth-black-2025-and-a-path.md@0697348c8`; `docs/research/sena-stem-retirement.md@0697348c8`.
