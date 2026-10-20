# ADR 0074 — Tokens map onto FwAvaloniaTheme purposes and never override Semi keys app-wide

**In plain terms:** Motif keeps its own names for colours and sizes, but each shared purpose (such as the selected-row
colour) is mapped to the matching FieldWorks value. Motif also stops changing the underlying Semi toolkit's own sizes
for the whole application, so that a Motif view placed inside FieldWorks does not resize FieldWorks' dialogs.

Status: accepted (2026-10-20). Amends ADR 0066 (see its 2026-10-20 amendment).

## Context

FieldWorks' ADR 0001 aliases Semi's layer-2 roles, and its ADR 0002 has a zero-tolerance token hygiene gate. Motif's
Intent layer already aliases Semi's roles and has the same kind of gate, so the architecture matches. FieldWorks'
token names are flat and follow no written rule; Motif's are layered (Primitive, Intent, Component). Motif's primitives
override Semi's own control-height keys to 26 and 28 for the whole application, which in a shared shell would resize
every FieldWorks dialog (its minimum control height is Semi's 24).

## Decision

1. **Keep the layered token names.** Renaming gains nothing, because FieldWorks' names are not a convention. A
   mapping table, `docs/design/fw-token-map.md`, to be written with the token work, will list each Motif Intent or Component key beside its FieldWorks key and
   value, with a reason wherever they differ, and a test will pin the shared light values.
2. **Adopt FieldWorks' light values** for the purposes that match (selected row, label ink, field border, rules),
   wherever Motif's contrast tests pass. A value that cannot pass is registered.
3. **Never override a Semi key app-wide.** Control heights come from Motif-owned control themes or a resource scoped
   to the Motif root, so the visuals stay the same and a host keeps Semi's defaults.
4. **The framework stays ready for theme variants.** Fuller dark and high-contrast work follows FieldWorks' net10
   migration and is not designed here. When hosted, Motif follows the host's theme.
5. **Interface font:** Motif keeps its bundled Andika at 13 DIP (owner decision, 2026-10-20). FieldWorks sets no UI family today (platform default, 11 px surface text), and a FieldWorks pull request is being prepared to bundle Andika as its standard Avalonia UI font, so this is expected to match.

## Consequences

Removing the override must show no layout-invariant or screenshot difference. Contrast tests, the size matrix and the
reachability tests are candidates to offer FieldWorks, since its ADR 0003 has no colour or contrast check.
