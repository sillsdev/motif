# Grammar health diagnostic evidence

Warnings carry PanGloss's explanation and advice so someone can read the same finding in Motif, an exported report, or the parser's documentation. Motif preserves the reported subject's state rather than resolving a missing reference against a later project.

Motif accepts only grammar-health `schema_version: 4`, produced by the pinned PanGloss v0.6.0 release. Earlier versions and unknown future versions are refused with an instruction to update PanGloss and Motif together.

Each finding retains its producer `title`, `description`, nullable `explanation` and `guidance`, report `locale`, `scope`, nullable `help_path` and CommonMark `help_body`, and structured `fieldworks_places` (`tool`, `field`). Motif displays these fields without deriving advice or destinations from prose. Its reference link uses the pinned release tag and producer help path.

Each subject retains `status`, `field`, `source_class`, captured identity, labels and navigation availability. `object` subjects may carry live navigation; `unresolved_reference` subjects remain unavailable even if their GUID later exists; `project_settings` subjects stay settings rather than objects. Unresolved subjects and settings never trigger Baseline object lookup. A reported scope must agree with the subjects: object takes precedence over unresolved reference, which takes precedence over settings.

The stored response includes this evidence. A database from before this shape is refused under Motif's pre-1.0 store policy; there is no migration or reader for an older diagnostic shape.
