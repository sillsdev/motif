# ADR 0071 — Motif is ahead of FieldWorks on Avalonia 12 and net10, on the same path

**In plain terms:** Motif uses a newer version of the Avalonia UI toolkit than FieldWorks does today. That is not a
split in direction. FieldWorks is on the older version only because it still runs on .NET Framework 4.8, and it will
move to the newer one before the two codebases merge.

Status: accepted (2026-10-20). Supersedes nothing.

## Context

FieldWorks pins Avalonia 11.3.x, Semi.Avalonia 11.3.x and Ursa because its Avalonia code runs in-process on net48
beside WinForms; Avalonia 12 drops netstandard2.0. FieldWorks will retire WinForms and move to Avalonia 12 and net10
before the codebases merge (its "Phase 2"). Motif is on Avalonia 12.1.2, Semi 12.1.0 and net10.0. Downgrading Motif to
Avalonia 11 would not let it load in FieldWorks' net48 process, so it would buy nothing.

## Decision

1. Motif stays on Avalonia 12 and net10. This is not a deviation from the FieldWorks guide: it is the same path,
   reached earlier. The register notes it as "ahead", with the revisit trigger "FieldWorks completes its net10 move".
2. Until hosting, FieldWorks starts Motif as a separate process, so the two Avalonia versions never share a process.
3. Code meant to be shared later (tokens, .resx accessors, view models) avoids Avalonia-12-only APIs where that costs
   little. Motif does not take on Ursa until a FieldWorks control needs it.
4. Test framework (xUnit against FieldWorks' NUnit) is a recorded deviation, ended by a mechanical port at hosting.

## Consequences

No package downgrade is planned. The merge cost is the test-framework port and the project split, both deferred to
hosting.
