# Capture evidence follow-up

This follow-up distinguishes demonstrated Motif allocation inefficiency from hypothetical noisy parser output. Herdr reviewer: GPT-6.1-Sol, high, challenge-contracts; report preserved verbatim below.

**The inspected evidence does not justify 64 MiB stdout or 8 MiB stderr limits.** Unbounded capture is proven; normal PanGloss exhausting memory or disk is hypothetical. No production output-size distribution or exhaustion incident was found in the inspected fixtures/reports.

HEAD remains `74d82f11`. This follow-up used source reads and file metadata only; no changes or execution.

- **Stdout carries valid structured results.** Stats returns JSONL or text through [PanGlossRequest.cs:136](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/PanGloss/PanGlossRequest.cs:136); Trace returns JSON at line 171; GrammarHealth returns its JSON report at line 204; `--describe` supplies handshake JSON. A blanket stdout limit could reject valid results.
- **Batch’s structured results are files, rather than stdout:** TSV and optional morphology JSONL are read at [PanGlossRequest.cs:99](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/PanGloss/PanGlossRequest.cs:99). Import likewise produces a grammar file. Their successful outcomes do not consume captured stdout.
- **Stderr carries diagnostics.** Failures retain it in the typed refusal at [PanGlossInvoker.cs:239](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/PanGloss/PanGlossInvoker.cs:239). Batch retains the full text as `stderr.txt` at line 268, and extracts `warning:`/`capability:` lines at [PanGlossAssessor.cs:140](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/Assess/PanGlossAssessor.cs:140). It is not necessarily disposable noise.

**Size evidence is limited.** Existing fixture files measure:

| Fixture | Bytes |
|---|---:|
| [trace-details-v2-matinlu.json](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Support/TestFixtures/trace-details-v2-matinlu.json:1) | 24,979 |
| [schema-v3-error.json](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Support/TestFixtures/GrammarHealth/schema-v3-error.json:1) | 611 |
| [batch-mixed-outcomes.tsv](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.LibLcm/Parser/Fixtures/batch-mixed-outcomes.tsv:1) | 493 |

These are individual fixtures, not realistic upper bounds. [PanGlossInvokerTests.cs:208](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.Commands/PanGloss/PanGlossInvokerTests.cs:208) injects 1 MiB of synthetic stderr to test deadlock avoidance. The inspected real-parser tests assert semantics—three-word morphology at [RealParserBatchTests.cs:51](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.LibLcm/Parser/RealParserBatchTests.cs:51), one-word Trace at [RealParserTraceTests.cs:73](/C:/Users/johnm/Documents/repos/motif/tests/SIL.Motif.Tests.LibLcm/Parser/RealParserTraceTests.cs:73)—without recording byte sizes.

**Repeated materialization exists, but distinguish stdout from Batch artifacts.** Windows materializes each captured stream once through [WindowsCpuJob.cs:225](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/PanGloss/WindowsCpuJob.cs:225). Unix first captures to disk, then materializes strings after exit at [UnixPanGlossJob.cs:324](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/PanGloss/UnixPanGlossJob.cs:324). Stats JSONL subsequently becomes per-line strings and cloned JSON elements at [StatsCommand.cs:184](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Commands/Assess/StatsCommand.cs:184); text mode reuses the captured string. That is additional representation, not a second full stdout capture.

Batch artifacts have clearer duplication: `Finish` reads TSV/morphology into strings; retained publication rereads both as byte arrays and decodes them again at [PanGlossInvoker.cs:264](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/PanGloss/PanGlossInvoker.cs:264). The Assessor reads morphology again for verification at [PanGlossAssessor.cs:109](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/Assess/PanGlossAssessor.cs:109). Progress also rereads the entire growing TSV every 100 ms at [PanGlossInvoker.cs:333](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/PanGloss/PanGlossInvoker.cs:333).

**Minimal improvement:** address those demonstrated copies first. Stream artifact hashing using existing [DigestFile:46](/C:/Users/johnm/Documents/repos/motif/src/SIL.Motif.Host/PanGloss/BatchInvocationEvidence.cs:46), retain consistency checks without redundant full-text comparisons, and read progress incrementally while buffering incomplete trailing lines. This preserves all valid results and diagnostics, adds no arbitrary rejection threshold, and reduces allocation and repeated I/O. Broader file-backed stdout capture with reader-based consumers remains an optional next step, not an evidence-backed requirement today.
