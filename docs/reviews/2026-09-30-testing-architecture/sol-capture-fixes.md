# Capture second opinion

The capture correction must reduce unnecessary reads without weakening the retained evidence. An independent GPT-6.1-Sol xhigh review inspected the worker diff, including the incremental progress reader, before integration.

## Finding

Important/P2: the Assessor validated the retained morphology artifact's SHA but parsed independently mutable `Completed.MorphologyOutput`. A public invoker wrapper could alter valid morphology identities while preserving timing/outcome fields and the unchanged retained artifact. The original text comparison rejected that mismatch.

Restore payload-to-artifact binding without allocating another whole retained string, and add an outcome-only valid morphology mutation regression. The finding was returned to the worker for correction; no final acceptance is claimed here.

## Other checks

The invoker's streamed snapshot and rehash bind its consumed TSV and morphology to retained files. The problem is the independent public invoker/outcome seam, rather than the stream hashing itself.

No other blocking findings were reported. Existing 10 GiB containment remains, no output quotas are introduced, and the append reader buffers partial UTF-8 rows until newline. Complete results, retained evidence and input-race validation remain. This was a read-only review; the reviewer ran no tests.

## Resolution

The correction now binds both completed payloads to their retained artifact digests without rereading another full text copy. Independent re-review of worker commit `920c5ec1` accepted the fix with no new capture findings.

Both valid-payload corruption cases failed before the correction. The worker's final full gate passed 3,647 tests, failed none and skipped 61 on its parserless checkout. Integration commit `03220485` passed the combined real-release suite: 3,680 passed, none failed and 22 skipped. BOM handling and a surrogate pair across the 4,096-byte encoding boundary are covered. Remaining full result strings and models are acknowledged in [capture measurements](parser-capture-measurements.md).