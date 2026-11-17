# 0002. Grammar data never belongs in Motif

**Status:** Accepted, 2026-11-14.

## Context

The repository contains the program and format contracts. Language and project content belongs to its owners;
external test inputs must be explicit dependencies.

## Decisions

### D1. No grammar or FieldWorks project payload enters the repository

No such payload may be added, staged or committed anywhere in Motif. The ban covers real, synthetic,
self-authored, self-generated, agent-generated, converted, sampled, benchmark, conformance and test-fixture
inputs. Provenance, licence, size, author, generation method and regression need do not exempt payloads.

Payload includes grammar XML, JSON, YAML, CSV and other exports; word lists, lexica, corpora and language
examples; FieldWorks project files, SQLite stores, snapshots and backups; archives; and content embedded,
compressed or encoded in source, resources or goldens. A representation change does not make payload code.
Existing tracked payloads are not grandfathered.

### D2. Code and metadata may describe external inputs

Schemas, contracts, generic executable fixture builders, lock references and retrieval tools may be committed
when they contain no serialized grammar content. Tests may use approved pinned external datasets or private
owner data supplied externally. Runtime project outputs belong only in ignored or private temporary roots
and must never be staged. Private owner data must not be copied to a public external repository.

Conformance inputs are provisioned with `evals/Get-ConformanceFixture.ps1`; runtime interface validation uses
`tools/SampleProjects` to create disposable probes. Removing known payload paths does not establish that an
exhaustive semantic scan has found every possible payload.

### D3. Placeholder format fixtures are not language payload

`textediting/tests/conformance/` may contain record-file and Markdown shapes with placeholder values such as
`x`, testing the format rather than representing a language. Only the checker's `populated-grammar-structure`
record-shape rule is lifted there. All its other rules still apply; real language data remains prohibited.
Schematic patterns in methodology text, including the starter interview, are not language data. This does
not exempt real language examples or serialized grammar content.
The shared field's sealed boundary is defined in
[ADR 0006](0006-rich-text-is-fieldworks-markdown-in-the-shared-textediting-field.md).

### D4. Black's conceptual introduction is a named third-party document exception

Keep `docs/parser-help/workflow/sources/black-flex-conceptual-intro-fulltext.txt`. It is freely available and
made to ship with FieldWorks, separately licensed, and **not covered by Motif's MIT licence**. This exception
is for that third-party document, including its explanatory examples, and grants no general permission to
commit language data. Its separate licensing must be stated in the sources README and
`THIRD-PARTY-NOTICES.md`. The Parser Workshop L02 full-text file is not retained under this exception.

## Consequences

Project data, including Guide content and hypothesis rows, stays outside the program repository. Inputs are
retrieved or constructed at runtime without concealing payloads in code. Placeholder format tests and the
named, separately licensed document have precise boundaries rather than a general exemption.
