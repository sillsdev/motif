# Grammar health diagnostic evidence

Warnings carry PanGloss's explanation and advice so someone can read the same finding in Motif, an exported report, or the parser's documentation. Motif preserves the reported subject's state rather than resolving a missing reference against a later project.

Motif accepts only grammar-health `schema_version: 5`, documented by the PanGloss v0.19.4 source tag named in `pangloss-release.json`. Earlier versions and unknown future versions are refused with an instruction to update PanGloss and Motif together.

PanGloss reports an unreadable phonological environment as a warning. Roots ignore that restriction but keep valid ones; ordinary affixes add an unrestricted pass beside valid passes; an infix with no valid position is skipped. Missing references and unrelated fatal conversion issues still refuse.

Each finding retains its producer `title`, `description`, nullable `explanation` and `guidance`, report `locale`, `scope`, nullable `help_path` and CommonMark `help_body`, and structured `fieldworks_places` (`tool`, `field`). Motif displays these fields without deriving advice or destinations from prose. Its reference link uses the interface source tag and producer help path.

Each subject retains `status`, `field`, `source_class`, captured identity, labels and navigation availability. `object` subjects may carry live navigation; `unresolved_reference` subjects remain unavailable even if their GUID later exists; `project_settings` subjects stay settings rather than objects. Unresolved subjects and settings never trigger Baseline object lookup. A reported scope must agree with the subjects: object takes precedence over unresolved reference, which takes precedence over settings.

The stored response includes this evidence. A database from before this shape is refused under Motif's pre-1.0 store policy; there is no migration or reader for an older diagnostic shape.

Schema 5 preserves the producer's `blocks_fst` and `fst_reason` on each diagnostic and summary row.
`fst_checked` and `fst_check_error` distinguish optional FST capability inspection from the ordinary
grammar check: an unrequested or incomplete inspection is never a clean FST result. The optional
FST-health format is schema 10, whose retained-construct finding is `PGF0018` (replacing `PGF0016`).
Motif reads the labels from grammar-health and does not invoke the separate FST-health command.

Within one run, grammar-health can produce the snapshot and light facts used by Parsimony through
`--snapshot-out`, `--facts-out`, `--facts-context` and `--facts-light`. Facts remain schema 9 and bind
their `source_sha256` to the emitted snapshot bytes. A Trial passes that snapshot to its Assessment
batch, retaining the original project as its source evidence. These files belong to the current
run; no parser engine or imported model is retained for another run.
