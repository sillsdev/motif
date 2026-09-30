# Parser admission follow-up

Motif must check whether PanGloss supports a requested operation without refusing valid syntax. Independent Sol review identified compatibility and test-isolation problems before this change was accepted.

## Findings and reviewed corrections

The pinned PanGloss release declares `parse --trace` with `takes_value=true`, while a bare flag requests stdout and does not consume the next argument. Applying a required-value rule to that flag consumes `--trace-format` and miscounts its value as a positional argument, refusing the typed Trace request. Changing the fake description to `false` misrepresents the real contract. This is P1 and requires faithful description fixtures plus a behavioral regression against the supported syntax.

Exact flag lookup also refuses valid inline forwarded Stats forms such as `--group=word` and `--format=jsonl`. This is P2: validate the flag name while preserving the original argv and check whether the declared flag accepts a value.

The new Unix permission test scans all temporary parser captures and reads them to identify its tokens. This is P2: concurrent suites can expose it to another invocation's complete, unbounded output. A private invocation capture path or bounded internal test seam must identify only this test's streams.

## Accepted aspects and verification limits

Read-only review found no additional issue in cancellation, capability caching or atomic Unix user-read/write creation. All three findings were returned to the owning Luna worker. Independent Sol re-review accepted the current corrective diff: the fake retains the real Trace declaration, inline Stats values preserve argv, and the Unix test examines only its injected private capture directory. Its fresh parserless suite passed 3,664 tests, failed none and skipped 59. The pinned-release gate is running and has exposed the separate known cancellation-walkthrough Refresh completion race; that correction belongs to the walkthrough owner. Corrective commits, fresh combined real-parser verification and native Unix execution remain pending. No parent admission change is integrated on the strength of a parserless run alone.

## Final admission review

Actual request validation and private captures are integrated as dcd685d6. Independent final review accepted owner commit bfed68c5 with no actionable findings; Commands shards passed 492 tests, failed none and skipped four on Windows.

The owner's entire pinned-parser gate had one separate Infix sample failure; it is not a green whole-gate claim. Unix permissions are source-reviewed and require native Unix execution. Parent integrated build and hygiene gates pass; combined behavioral verification remains pending.
