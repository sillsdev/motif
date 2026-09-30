# Parser admission follow-up

Motif must check whether PanGloss supports a requested operation without refusing valid syntax. Independent Sol review identified compatibility and test-isolation problems before this change was accepted.

## Findings awaiting correction

The pinned PanGloss release declares `parse --trace` with `takes_value=true`, while a bare flag requests stdout and does not consume the next argument. Applying a required-value rule to that flag consumes `--trace-format` and miscounts its value as a positional argument, refusing the typed Trace request. Changing the fake description to `false` misrepresents the real contract. This is P1 and requires faithful description fixtures plus a behavioral regression against the supported syntax.

Exact flag lookup also refuses valid inline forwarded Stats forms such as `--group=word` and `--format=jsonl`. This is P2: validate the flag name while preserving the original argv and check whether the declared flag accepts a value.

The new Unix permission test scans all temporary parser captures and reads them to identify its tokens. This is P2: concurrent suites can expose it to another invocation's complete, unbounded output. A private invocation capture path or bounded internal test seam must identify only this test's streams.

## Accepted aspects and verification limits

Read-only review found no additional issue in cancellation, capability caching or atomic Unix user-read/write creation. All three findings were returned to the owning Luna worker. Corrective commits, independent re-review, fresh combined real-parser verification and native Unix execution remain pending. No parent admission change is integrated on the strength of a parserless run alone.