# ADR 0076 — Grammar data never belongs in Motif

**In plain terms:** Motif's repository contains the program and its format contracts, not language or FieldWorks project content. Keeping grammar inputs outside this repository protects owners' material and makes test inputs explicit external dependencies.

Status: accepted

## Decision

No grammar or FieldWorks project payload may be added, staged or committed anywhere in the Motif repository. The ban applies whether data is real, synthetic, self-authored, self-generated, agent-generated, converted, sampled, used for a benchmark, conformance check or test fixture. Provenance, licence, size, author, generation method and regression-test need are not exceptions.

Payload includes grammar exports and fixtures in XML, JSON, YAML, CSV or other formats; word lists, lexica, corpora and language examples; FieldWorks project files, SQLite stores, snapshots and backups; and copies packed in archives or embedded, compressed or encoded in source, resources or goldens. Changing a payload's representation does not make it code.

Motif may contain executable behavior and metadata that describe how to use external inputs: schemas, contracts, generic fixture-builder code, lock references and retrieval tooling. Such code must not embed serialized grammar content. Tests may consume approved pinned external datasets or private owner project data supplied from outside the repository. Tests may create disposable project outputs at runtime only in ignored or private temporary locations; those outputs must never be staged or committed. Private owner data must not be copied to a public external repository as a workaround.

No existing tracked payload is grandfathered. The previously tracked `tools/fixtures/pangloss-interface-smoke.xml` and files under `tests/SIL.Motif.Tests.Support/TestFixtures/Conformance/deep-optional-affix-nesting/` were removed from this source. Conformance tests provision pinned external inputs through `evals/Get-ConformanceFixture.ps1`; runtime interface validation uses `tools/SampleProjects` to generate disposable probe projects instead of a tracked XML input. These path-specific facts do not claim an exhaustive semantic scan or guarantee that every possible payload has been found.
