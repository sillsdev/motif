# 0102. Grammar classes are Motif-registered Harmony types that ride LexBox opaquely

**In plain terms:** FW Lite has no grammar yet. Motif adds the grammar to the same history FW Lite keeps. FW Lite
and the LexBox server carry those additions along unchanged without needing to understand them.

**Status:** proposed, 2026-11-10. Depends on [ADR 0101](0101-a-project-source-is-fwdata-or-a-harmony-log-everything-else-is-built.md).

## Decision

1. **Motif registers its own object and change types, generated per LibLCM class** by `src/SIL.Motif.Generator` (which already
   generates about 84% of the operation kinds from MasterLCModel.xml), never hand-collapsed. Intents, composers and GDR
   markdown are the abstract, connected layer above them: a view that is never stored (owner, grill Q4, 2026-11-10).
   They cover the grammar FW Lite does not model, and are registered in a host configuration alongside LcmCrdt's (`ConfigureCrdt`: ObjectTypeListBuilder, ChangeTypeListBuilder and a new
   ModelVersion). Examples: allomorphs and their environments, MSAs, phonemes, natural classes, features,
   phonological rules, templates and slots, inflection classes and exception features. The scope is the 230
   grammar rows of the coverage manifest (`manifest/liblcm-inventory.tsv`), each with the class and GUID LibLCM uses.
2. **Lexical objects stay FW Lite's.** Entries, senses, POS and morph types are LcmCrdt's types. Motif's grammar
   objects reference them by GUID and never duplicate them.
3. **FW Lite and LexBox carry Motif's commits opaquely.** This is verified in code: the LexBox server stores
   `ServerJsonChange{Type, ExtensionData}` without a type check, and LcmCrdt sets
   `UnknownChangeHandling.Fallback`, so FW Lite keeps an unknown change as an `OpaqueChange`, skips it during
   replay and writes it back unchanged.
4. **Each Motif type decides what a deleted referent means** (`RemoveReference`). An allomorph whose entry is
   deleted is deleted. A rule whose natural class is deleted keeps a marked hole, so the Build reports it (ADR 0105).
5. **No unique indexes on Motif types.** A remote commit that violates a unique index fails every sync on every
   client, so uniqueness is a build error, not a database constraint.

## Consequences

- No upstream change is needed to start. When MiniLcm gains allomorph and MSA models (#2558/#2559), Motif's types
  for those classes migrate to them through a ModelVersion replay.
- A known change against an entity whose create FW Lite saw as opaque throws during replay (`EditChange` calls
  `NewEntity`). Motif must never emit an LcmCrdt change type against a Motif-typed entity. **Not yet tested.**
- The FwHeadless sync writes grammar-adjacent objects as side effects: it creates a lexeme-form allomorph per new
  entry and an MSA when a sense's POS is set. Those writes target the team's .fwdata, not the source. While a
  project still syncs .fwdata through FwHeadless, the .fwdata has two writers. Open (ADR 0101).

## Alternatives

- **Contribute grammar to MiniLcm first:** blocked on another team's schedule.
- **Grammar outside Harmony** (Motif's own store, linked by GUID): two logs, two sync stories, and no FW Lite path.
