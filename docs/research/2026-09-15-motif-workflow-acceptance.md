# Motif workflow against the merged parser

The existing Motif commands can capture a saved project, compare a real parsed reading with its approved analysis, show statistics, and create a local AI Handoff. This check exercises the command-line workflow; it does not establish that the desktop window displays every recorded detail.

## Inputs and isolation

The run used Motif `feat/ai-handoff` at `b8b4bbc` and `G:\cargo-build-cache\integration-xample\pg-test-opt\pangloss.exe`, built from the integration source merged into PanGloss main. A saved disposable affix fixture was copied into `.tmp/motif-acceptance/affix-project`; no user language project was edited. A copied lock marker remained present, so the Baseline reported the project as held. This was not a live FieldWorks lock test.

The first retained native fixture inspected was only an empty project skeleton and Baseline capture refused it. The affix fixture supplied the valid saved project used for the successful checks below.

## Results

The actual CLI returned success for Baseline capture, Assessment, statistics, and Handoff. These results come from the compiled executable and the real parser, rather than a fake parser.

| Check | Result |
| --- | --- |
| Baseline | Captured the saved affix project successfully. |
| Assessment | `dkat`, `kat`, and `tak` all completed their searches. `dkat` matched its one approved reading; the other two returned no analysis and had no approved expectations. |
| Statistics | `stats --json -- --group word` read the recorded statistics successfully. |
| Handoff | Published 15 files: grammar, selection, instructions, reader, recipes, three references, summary, and six statistics groups. This fixture has no Text export. |
| Artifact checks | Returned file list exactly matched the directory; all JSON parsed and all 22 JSONL records parsed. The bundled Python validator was not run successfully because the local Python launchers could not find their runtime. |
| Source preservation | Before and after SHA-256: `BD89A390CA0CD16F8A78E11C425EDBFB92E65E321E15162EBD37B208361FD205`. |

Handoff made its own Assessment using the project's wordforms, selecting only `dkat`. It did not reuse the preceding pasted-word Selection of three words. The returned Handoff Selection and its files describe that new run.

Local output is retained under `.tmp/motif-acceptance`: `baseline.json`, `assessment.json`, `statistics.json`, `handoff.json`, source hashes, the copied project, and the `handoff` directory. These disposable artifacts are not committed or sent to an external service.

## Desktop result inspection

The desktop now presents the morphology evidence returned by the Assessment. Each word has expandable ordered readings with selectable source references and guessed text. Evidence status distinguishes unavailable, invalid, partial, and empty results. Grammar warnings travel from the shared invocation into the response and appear once for the grammar as a whole, without invented per-word attribution.

The primary agent performed the command checks above and corrected the README's launch and managed parser-build instructions while Luna was unavailable. After the owner chose to wait, a later Luna audit completed and a Luna implementation task began the bounded desktop inspection change. Its final validation is recorded separately below.

## Validation of the inspection change

The full repository gate passes with the real merged parser selected. The new tests establish retained reading order, shared grammar warnings, and the headless result panel's handling of partial and unavailable evidence.

`./test.ps1` with `MOTIF_PANGLOSS_EXE=G:\cargo-build-cache\integration-xample\pg-test-opt\pangloss.exe` passed comment hygiene, compilation, and **1,668 tests**, with **19 skips and zero failures**. The initial Luna gate failed the new headless materialization assertion; the final root gate passed after the test opened the expanders, applied templates, and drained dispatcher jobs before reading the rendered controls. The final log is `.tmp/motif-acceptance/inspection-test-gate.txt`.

No new native screenshot, display-scaling, or clipping verification was performed. The earlier native evidence remains separate; a headless test does not replace it.

Independent Luna review returned GO with no concrete semantic or UI defects in the bounded diff. The primary agent reviewed the changes and ran the final gate above.
