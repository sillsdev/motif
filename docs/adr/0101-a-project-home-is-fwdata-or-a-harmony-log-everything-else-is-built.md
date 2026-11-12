# 0101. A project's Home is a .fwdata project or a Harmony log; everything else is built from it

**In plain terms:** Motif works with a FieldWorks project, or with an FW Lite project whose history is its native
format. Proposals store the changes and are applied to whichever one the project uses. The files tools run on, such
as a parser grammar or a FieldWorks file built from an FW Lite project, are built from that Home when needed. People
almost never see this happen.

**Status:** proposed, 2026-11-10. The owner settled the shape in a grill on 2026-11-10 (Q1–Q3). Revisits
[ADR 0013](0013-harmony-is-the-change-mechanism.md), which Plan A demoted
([ADR 0014](0014-generate-the-crdt-layer-from-masterlcmodel.md)). Evidence:
[plan-a-harmony-rejection.md](../source-build/plan-a-harmony-rejection.md),
[harmony-edge-cases.md](../source-build/harmony-edge-cases.md), [three-paths.md](../source-build/three-paths.md).

## Decision

1. **Two kinds of Home.** A project's Home is either a **.fwdata project** (Apply writes it through LibLCM, as
   today) or a **Harmony log** (FW Lite's `{Code}.sqlite`, extended with grammar per ADR 0102; Apply commits to it).
   Motif works with both. On one computer, Motif works with FieldWorks or with FW Lite, not both at once.
2. **The Harmony log is the native format** and the direction of travel. FieldWorks itself may move to it.
3. **Export to the log.** A one-way export turns a .fwdata project into a Harmony log as one tip, with no history,
   like a shallow git clone. It keeps every GUID. This is the only "decompile"; there is no continuous two-way sync
   of grammar between the two kinds of Home.
4. **Build.** A Build turns the Home at one point into what a tool reads: a PanGloss project snapshot (parser and
   Paratext plugins), a .fwdata project from a Harmony log (FLEx classic, export, a Proposal snapshot), and later an
   FST. A Build either succeeds or reports build errors; it never repairs the Home (ADR 0105). The word is internal:
   people see "parser updated" or a problem to fix, not a Build.
5. **Instant vs reviewed, by convention.** Lexical edits (in FW Lite or FieldWorks), approving parses, and editing GDR
   text are instant, like editing documentation straight on main. Every grammar change goes through a Proposal, with
   Trial, Measure and review, like code needing full CI. FW Lite does not edit grammar, so the convention costs nothing today.
6. **LibLCM stays as the compiler** for a log Home: a Build may load the log into a memory-only LCM cache
   (`kMemoryOnly`) so LCM's rules and Motif's existing Lowering and checks keep working.
   *Amended 2026-11-12 (Q28, ADR 0103):* a Trial or Measure builds the PanGloss snapshot straight from the log, with no
   LibLCM. A .fwdata is built only on demand: for FLEx classic, an export or a Proposal snapshot.

## Why this answers Plan A's objections

| Plan A objection (2026-08-01) | Answer |
|---|---|
| FieldWorks (net48) cannot load Harmony (net10) | FieldWorks opens a .fwdata Home or a built .fwdata; it never loads the log. |
| Dual authority: LibLCM and the CRDT | Each project has one Home. The other form is a build output or a one-way export. |
| No effect model or dry run in Harmony | Dry Run and Trial build a copy of the Home plus a Proposal ([ADR 0016](0016-scratch-cache-copy-not-undo.md) in spirit). |
| No proposal layer | Proposals are Motif's, held back from the log (ADR 0103). |
| Ordered grammar cannot ride a last-writer-wins order | One list-valued property, and a broken order is a build error (ADR 0104). |

## Consequences

- A .fwdata built from a log is output: FLEx classic edits to it are not carried back. FLEx classic remains a full
  editor for .fwdata-Home projects.
- One single path is needed in each direction, with no duplicated data: .fwdata → log (export) and log → .fwdata
  (Build). Proposal changes must lower through the same mapping. See ADR 0102 and the open grill questions on GUIDs and MSAs.
- Phase 1 uses a .fwdata Home (ADR 0106).

## Alternatives

- **Log only:** this strands existing FieldWorks teams.
- **Continuous two-way sync of grammar** (extend FwHeadless): this keeps two authorities, which is Plan A's objection.
- **Fork** (three-paths.md): this is a .fwdata Home applied to a private copy. It is subsumed here.
