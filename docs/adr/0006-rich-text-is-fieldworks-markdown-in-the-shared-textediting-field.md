# 0006. Rich text is FieldWorks Markdown in the shared TextEditing field

**Status:** Accepted, 2026-11-14.

## Context

People and assistants need one rich-text round trip for Guide text, other FieldWorks prose, Proposal
Rationales and Comments. A shared field and a documented Markdown profile prevent formatting loss and
incompatible private dialects while preserving each text's actual store and write boundary.

## Decisions

### D1. FieldWorks Markdown is a human-editable Pandoc profile

It is a strict documented profile of Pandoc Markdown. Every file is valid Pandoc Markdown and produces the
same structure in Pandoc with the profile's format string as in the .NET Markdig reader/writer; CI checks
agreement with Pandoc. Front matter `fwmd: 1` marks the file and names default writing systems and each
writing system's language tag, direction and font. Filenames end in `*.fw.md`. Spans, fenced divs, link
attributes, paragraph alignment and `custom-style` belong to this common format; finding IDs are `fwmd.*`.

People may edit it, and the shared rich field offers a source mode using the same checker and importer.
FieldWorks remains the store of project prose; Markdown is its view and clipboard form, not a second
canonical store. The Guide's outline, template and export are in [ADR 0005](0005-the-grammar-guide.md), not
a separate format. Typesetting and VS Code support are outside the rich-text 1.0 scope.

### D2. Project paths carry identity explicitly

A link contains the object's GUID; a project path such as `/lexicon/<name>` is a readable locator refreshed
on export after rename or move. `silfw://`, `motif://` and a GUID alias are other spellings of the same link.
The host supplies the path resolver and checks stored silfw links against a real project. Tables use the
generic **Table Header**, **Table Row** and **Table Cell Separator** styles in Guide text, Rationales and
Comments; pipe tables are ordinary FieldWorks Markdown, not findings.

### D3. Two generic field operations carry project rich-text edits

`text/setParagraphs` addresses any editable StText-valued field by owner object and field. `after` carries
paragraph order (existing keys or null for new paragraphs), changed/new paragraph styles and runs, removed
keys, and `expected`, the starting text of every changed or removed paragraph keyed by paragraph identity.
Runs retain text, writing system, character style and other allowed properties. Apply creates a missing
StText, preserves existing paragraph identities and creates new ones.

`text/setRichString` addresses any editable String or MultiString field by owner, field and writing system;
`after` carries runs and the expected starting text. The field inventory determines eligibility, not a
per-field operation kind. These kinds are additive; generated plain-text kinds remain for agents until
rich text replaces them, with no generator-wide change. Templates add no kinds. SIL.TextEditing's paragraph
keys and LibLCM load-back rules govern the round trip.

### D4. Editing sessions retain the original guard; drift holds the whole Apply

An editing session updates one operation. Later sessions on the same target and field replace that
operation while preserving `expected` from the first session's start. If live text differs at Apply, refuse
the whole Apply with a stale-field result naming the field; keep the Draft whole. The person resolves the
field through the shared comparison viewer, restages it from live text and retries. The pending-changes
path and an agent's Proposal use the same Draft, Dry Run, Preflight and Apply contracts.

### D5. Rationale and Comments are FieldWorks Markdown in Motif's store

Unmarked text uses the default analysis writing system. Existing text is read as FieldWorks Markdown;
unreadable text appears as plain text and is never lost or rewritten. A person edits with **Edit**, then
Save or Cancel in the shared rich field. Save writes directly to Motif's store, with no Draft or project
Apply. An agent may add its own Comment but may not edit/delete one; people may edit any Comment. Everything
freezes when the Proposal is decided, under
[ADR 0007](0007-a-proposal-has-one-lifecycle-a-conversation-citations-and-one-apply-path.md). Pictures remain
attachments. A concurrent Rationale change offers **Compare…**, with the other side labelled by its author.

### D6. Text findings describe what a reader would see as wrong

Find unreadable markup, missing project-object links, unknown writing systems or styles, and cases where
reading changes a list, including a marker inside a paragraph or a renumbered ordered list. Store text as
written. Save may only tidy line endings, whitespace at the end and fill a link's `guid=`, and only if
rereading proves meaning unchanged. Canonical spelling is not permission to change meaning.

### D7. Finalize requires a clean Rationale; Comments never block

During Draft, save the newest Rationale even with findings and return those findings; preserve restorable
history. Refuse Finalize while findings remain, and require a Rationale. Once Ready, refuse a new revision
with findings and keep the last clean Rationale. Already-Ready text is left as written; this rule governs
new revisions. Comments save with findings and never block Finalize or Apply.

### D8. Backticks are plain analysis text

A code span in a Rationale or Comment never creates a vernacular finding. Language forms use `[form]{.v}`
or an explicit `ws=` span, so they carry the right writing system. Commands and identifiers remain readable
without false findings.

### D9. textediting/ is sealed, with dependencies in one direction

Motif may reference projects in `textediting/`. Nothing there references outside it except
`Directory.Build.Common.props` and `SilVersions.props`; `textediting/tools/Check-Sealed.ps1` enforces this.
The folder owns its `AGENTS.md`, ADRs, `Directory.Build.props`, `Directory.Packages.props` and
`SIL.TextEditing.sln`. Motif projects do not enter that solution, and the folder's projects do not enter
`Motif.sln`. Shared FieldWorks/Motif code belongs there; Motif workflow code does not. A clone needs no
submodule checkout, and the folder remains independently extractable.

### D10. The shared field keeps both framework lines and its own gate

The folder builds netstandard2.0 with Avalonia 11.3 for FieldWorks' .NET Framework 4.8 and net10.0 with
Avalonia 12 for Motif. Nearest-directory settings keep Motif's shared output directory, runtime forcing,
implicit usings, dependency-injection package and Product name out. `Directory.Build.Common.props` contains
only shared language version, nullable, warnings-as-errors and deterministic-build policy.

`SilLibPalasoVersion` supplies SIL.WritingSystems; `SilLCModelPackageVersion` supplies SIL.LCModel.Core and
SIL.LCModel. `LOCAL_NUGET_REPO` works in both. Raising pins re-tests net48. The independent Windows
`.github/workflows/textediting.yml` gate watches the folder, its two shared imports and itself; it builds
both lines, tests net10.0/net48 and publishes a native AOT check.

Motif build/test scripts and their cross-platform matrix build only the referenced net10.0 libraries:
App's core and Avalonia fields, Host's Project/LCModel/Markdown/core for Guide reads, and Runner's LCModel
load-back support. They do not run the folder's independent suite. Project references select net10.0 with
`SetTargetFramework="TargetFramework=net10.0"`, never `AdditionalProperties`, to avoid concurrent builds
into one obj folder. Placeholder format fixtures follow [ADR 0002](0002-grammar-data-never-belongs-in-motif.md).

## Consequences

Project prose and Motif discussion share format, checker, importer and editor while retaining their own
stores. A formatting-preserving edit has one field guard rather than one kind per field. Snapshot and diff
views that flatten rich text cannot yet expose formatting-only effects; this remains a limitation to fix,
not permission to discard runs. Checker messages must make sense to people and agents, and CI supplies
format-conformance and framework-line proof.
