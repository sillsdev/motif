# Writing systems come from libpalaso

The field reads writing-system facts from libpalaso's `SIL.WritingSystems` (`WritingSystemDefinition`): font,
direction, font features and keyboard. FieldWorks and Motif already use the same definitions, so a separate
`IWritingSystemInfo` would only copy them.

**Considered options:** our own small interface, filled in by each host. Rejected by the owner: "Let's not reinvent
the wheel."

**Consequences:** the package depends on libpalaso, and through it on ICU. Its versions must match what FieldWorks and
Motif pin, and the net10.0 build must stay trimmable and AOT-compatible.

- The minimum version is FieldWorks' pin, 18.0.0-beta0042; hosts may float up. From the move into Motif (ADR 0034), the
  version comes from Motif's `SilVersions.props` instead.
- A libpalaso trim or AOT warning is fixed upstream. libpalaso takes pull requests from us, through its normal review.
- Until that fix is released, the warning is suppressed by its ID, with a comment that links the libpalaso pull request.
- The suppression is removed when the minimum version is raised to the fixed release.

Owner decision, 2026-11-03.

Amended 2026-11-04: the Markdown converter does not reference libpalaso. It identifies writing systems by language tag
and takes each one's language, direction and font from the file's front matter (ADR 0028).

Amended by ADR 0034 (2026-11-06): from the move into Motif, SIL package versions follow Motif's `SilVersions.props`, not
FieldWorks' pins. The netstandard2.0 / Avalonia 11.3 line is the compatibility promise to FieldWorks.
