# Parser capture measurements

Motif keeps complete parser results and the existing 10 GiB child memory containment. The capture correction removes redundant reads without adding arbitrary output quotas.

## Measured integration output

A small real PanGloss integration shows what was actually emitted. It cannot predict the size of large projects or establish a maximum.

The combined repository gate used hash-verified PanGloss v0.5.1 and the two Motif-seeded stems in `EveryMorphologyIdentityNamesAnObjectInTheParsedProject`. That passing test recorded:

| Artifact | Bytes |
| --- | ---: |
| Batch TSV | 78 |
| Morphology JSONL | 616 |
| Diagnostic stderr | 217 |

The source project supplied every emitted morphology identity. The retained artifacts and completed payloads remained bound by SHA-256 checks. No observation here establishes runaway logging.

## Materialization inventory

Different commands emit different kinds of output. Keeping the full structured result remains necessary at the current consumer seam.

Batch writes TSV and optional morphology JSONL artifacts; Trace, Stats and Grammar Health return structured stdout; Import writes its grammar artifact. Diagnostic stderr is drained concurrently with stdout. Retained Batch capture creates one full TSV string and, when requested, one morphology string; Assessor parsing creates full result models. Stderr remains a full string.

File hashes now use streams instead of full byte arrays. Retained snapshots hash the same bytes read into their strings and are rechecked before publication. Assessor independently binds consumed strings to retained evidence with chunked UTF-8 hashing, avoiding another whole-file string comparison. Progress tracks a byte offset, using an 8 KiB read buffer and an incomplete-row buffer rather than rereading the entire growing TSV.

This is not a constant-memory pipeline. No stdout, stderr, artifact or progress-row quota is introduced. Existing concurrent draining, cancellation, admission reuse, input-race checks and complete result semantics remain. Regression coverage includes altered valid payloads, BOMs, Unicode boundaries, partial progress rows and duplicate suppression.

## Validation limits

The current Windows run proves the integrated behavior that executes on this platform. Unix capture permissions and platform-specific containment require their Linux/macOS checks.

The combined ./test.ps1 run passed 3,680 tests, failed none and skipped 22. Capture-specific changes also passed the worker's isolated full suite. The separate request-capability and private Unix capture work remains in progress.