# ADR 0084 — Proposal discussion: Rationale, Comments, attribution, freezing and pictures

**In plain terms:** a Proposal gets a proper conversation. The Rationale can be revised by people and agents while the
Proposal is open, comments can pile up and say who wrote them, and pictures can be attached. Once the Proposal is decided
the discussion freezes and the working history is dropped, keeping only the final text. This replaces the old single note
that an agent could read but never write.

**Status:** accepted, 2026-11-02. Design: `docs/superpowers/specs/2026-11-02-grammar-design-records-design.md`. Changes the
**Rationale** entry in `CONTEXT.md` and adds **Comment**. Consistent with [ADR 0061](0061-proposal-mode-is-a-lens-over-one-set-of-pages.md),
which rules that a review records which changes were looked at and never who looked: that is about review marks, not
discussion, and is unchanged. [ADR 0039](0039-one-worker-baseline-and-live-host-authority.md) is untouched; Comments never
write the language project. Amends [ADR 0069](0069-the-agent-api.md) decision 6, where the note an agent never writes
becomes Comments an agent may add.

## Decision

1. Rationale keeps its name and remains the main description. People and agents may edit it while the Proposal is open.
   Its revisions are kept until the decision and then dropped; the latest text stays.
2. The Proposal's note becomes Comments, which can grow. Agents may add comments, attributed to them (for example
   "Claude (Opus 5.5) for John"), and may never edit or delete one. People may add, edit and delete any comment. This
   reverses the earlier rule that an agent never writes the note.
3. Rationale and Comments are frozen at the decision.
4. The latest Rationale, Comments and pictures are kept as long as the Proposal exists. Manual deletion of a Proposal that
   never applied stays and removes them with it.
5. Pictures are stored in `Project.motif.db`, scaled down to about 1 MB each (amended 2026-11-06 below). They count toward the 100 MB store
   budget of [ADR 0082](0082-stores-and-user-data-stay-inside-a-byte-budget.md). They are authored data, so a prune never
   removes them, and a store full of pictures reports `OverBudget` like any other.
6. Attribution on a Comment is a label for readers. It is not an approval, an identity check or an authorisation, and no
   Apply depends on it.
7. A choice made for an item that no longer fits is recorded as a Comment on the Proposal.

## Consequences

The store gains comment and picture tables, and a Rationale revision table that is cleared at decision. Rationale and
the person's note are today columns on the Proposal row, and the MCP notice `noteChanged` tells an agent a person has
written; that notice and its next-step text move to Comments with this decision.

## Amended 2026-11-06

Pictures change as follows; the rest of this ADR stands.

- Each picture has an optional one-line caption of at most 200 characters. It belongs to the picture, so it stays on the
  Proposal whether the picture is attached to the Proposal or to a Comment, and `proposal picture show` lists it.
  The store schema becomes 63 (a nullable `Caption` column); upgrades carry it by name and keep every Proposal.
- A Proposal holds at most 10 pictures, shown as "3 of 10". When it is full the window says "Remove one to add another".
  A Proposal that already holds more keeps them; only a new attach is refused.
- On attach the window honours the EXIF orientation, resizes so the long side is at most 1000 px (never enlarging) and
  stores only a WebP copy: lossless when the source is a PNG (or a GIF, BMP or lossless WebP), lossy at quality 80 when it
  is a JPEG. Re-drawing the picture drops all metadata (GPS, camera, orientation tag). The original is not kept.
  A build whose Skia cannot encode WebP falls back to PNG for a PNG source and JPEG at quality 80 for the rest, still
  resized and stripped.

## Amended 2026-11-07

- The CLI and the other commands scale pictures exactly as the window does. `proposal picture add` (and `AddPictureRequest`)
  accept a WebP, PNG, JPEG, GIF or BMP file of up to 64 MiB and about 100 megapixels (the declared size is checked before
  any decoding), and store the same WebP copy: upright, long side at most 1000 px, never enlarged, lossless for a PNG-like
  source and quality 80 for a JPEG or lossy WebP, metadata and colour profile dropped. The 1 MiB limit applies to that
  converted copy. A file that cannot be decoded, or is over the input limits, is refused with a message saying why.
- There is one implementation, `PictureScaler` in `SIL.Motif.Commands`, built on SkiaSharp; the window calls it. The
  earlier byte-level metadata stripping and the refusal of pictures over 1000 px are gone.
- Dependency: the owner approved a `SkiaSharp` package reference on the commands project on 2026-11-07, at the version
  Avalonia.Skia brings the window (3.119.4) so the build holds one Skia, and approved `SkiaSharp.NativeAssets.Linux` at
  the same version on 2026-11-07 so the native library is found on Linux (Windows and macOS assets come with SkiaSharp).
  The native libSkiaSharp therefore ships with both the CLI and the MCP server, which reference Commands.
