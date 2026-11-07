# No Graphite support

The field shapes text only through Avalonia's HarfBuzz, with OpenType font features. It does not support Graphite,
even though legacy FieldWorks Views does. A font that depends on Graphite shows its OpenType behaviour, or plain
glyphs if it has none.

**Considered options:** a HarfBuzz native build with graphite2, packaged for net48 and net10. Rejected: it would add
a native build to maintain for a shrinking set of fonts.

Owner decision, 2026-11-03. A deliberate exception to ADR 0005.

Confirmed 2026-11-04: leaving Graphite out of the Avalonia field is intentional, not a gap to close later. It is not a
reason to pick a typesetting engine either: typesetting is out of scope for 1.0, and the GDR Markdown format only
carries each writing system's language, direction and font for whatever tool a user publishes with.

Renamed 2026-11-04: the general format is FieldWorks Markdown (ADR 0028); GDR Markdown is its GDR profile.
