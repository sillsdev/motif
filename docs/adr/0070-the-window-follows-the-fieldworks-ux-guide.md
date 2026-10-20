# ADR 0070 — The window follows the FieldWorks UX guide, and every deviation is registered

**In plain terms:** Motif's window should feel like FieldWorks, because Motif is expected to run inside FieldWorks later.
So where the FieldWorks UX guide has a rule, Motif follows it. Where Motif cannot, the difference and its reason are
written in one table, so nobody has to guess whether a difference is deliberate.

Status: accepted (2026-10-20). Complements ADR 0046 (pages, not stages), ADR 0063 (fixed grid) and ADR 0066 (compact
scale), which stand.

## Context

FieldWorks is publishing a UX guide in [FieldWorks UX guide](https://github.com/sillsdev/FieldWorks/pull/1191) (`Docs/ux/README.md`), still open as pull request 1191. The owner expects Motif to be hosted
inside FieldWorks' Phase-2 window (about 80% likely), and until then Motif is a separate process. A window that
follows the guide now needs no redesign at hosting. Motif already matches the guide on dialog Enter and Esc, Close-only
dialogs, F1 Help, opinion colours (ADR 0049), data-text fonts (ADR 0051), the token hygiene gate and stable
language-independent automation ids.

## Decision

1. **Follow the guide as closely as possible; deviate only when necessary.** A deviation is necessary only when
   FieldWorks' constraint cannot apply to Motif (runtime), when Motif's job differs (a reviewed batch of parser
   changes, not per-field editing), when the same content also feeds the CLI or the docs site, or when the guide is
   silent. Taste is not a reason.
2. **Every deviation goes in the register**, [docs/ux/deviations.md](../ux/deviations.md): topic, the FieldWorks rule
   with a link, what Motif does, why it is necessary, and when to revisit it. A difference not in the register is a
   defect.
3. **Decisions where the guide is silent** are recorded in the register as proposals to send upstream, not as
   private Motif style.
4. **Apply is one-way, and that is not a deviation.** Motif has no unapply. Proposals, Dry Run and Trials exist to
   test before Apply, and Undo exists only before Apply
   ([ADR 0016](0016-scratch-cache-copy-not-undo.md), [ADR 0005](0005-schema-operations-non-undoable-uow.md)).
   FieldWorks' own undo covers only a subset of its commands, and its Bulk Edit already works as Motif does: preview,
   clearly marked as not saved, a command that names what it does, then one step.
5. **Dark mode and high contrast:** the token framework stays ready for theme variants. Fuller work comes after
   FieldWorks' move to net10 and is not designed here.
6. **Interface font:** Motif keeps its bundled Andika at 13 px (owner decision, 2026-10-20). The guide sets no family, and a FieldWorks pull request to bundle Andika as its standard UI font is being prepared, so the register row is expected to become a match.
7. **Crowdin:** Motif has no project now. Its strings join FieldWorks' project when the codebases merge.

## Consequences

Every UX change in a pull request checks the register. Motif's own conventions that FieldWorks lacks are offered
upstream once the guide merges. The register is the place to look when Motif is hosted: each row names what must be
revisited.
