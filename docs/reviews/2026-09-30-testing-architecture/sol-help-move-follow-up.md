# Shared documentation follow-up

The CLI, window and website should present the same authored guidance. Keeping source pages beside the Help code makes ownership explicit, while generated preview fixtures and published pages remain derived outputs.

## Reviewed corrections

The near-code move `4fd75868` preserves embedded resource identities and uses catalog exports as the website body source. Independent Sol review initially found that the default preview fixture omitted Guide entries; `cdc2bd76` regenerates it from the CLI with 42 real Guide pages, including ten Learn pages and all seven homepage features. Bodies match the Help-owned source. A sample no longer links to a Turkish lesson absent from the actual catalog, and advertised lesson links are checked for resolution. No physical-body rendering fallback was added.

Cancellation test correction `12f357f4` confines the held fake behavior to Batch, leaving Refresh Grammar Health free to finish. It still proves child exit and no stored invocation after cancellation, awaits the actual refreshed command task and evidence publication, and requires exactly one rerun invocation with the expected words and baseline semantic identity plus bundle digest. A different capture timestamp is legitimate when the byte-identical bundle is reused. Independent Sol found no actionable findings in either correction.

## Verification limits

The owner completed the full wrapper: 3,654 passed, zero failed, 61 skipped, 710.6 seconds, with build, comment, token and offline-restore gates passing. This isolated run is parserless. Its fresh real-CLI website suite passed nine tests with no failures or skips. The changes are integrated in the parent as fac6b6f6, 3df11761 and 40abbde2, with release helper path correction b3e386be. Parent merge preserves the Motif-owned SeededProject fixture. Combined pinned-parser and release-media verification remain pending.


## Parent website correction

Final route review exposed an encoded separator escape and a merged fixture retaining links to removed Learn entries. Commit f7d8cc96 rejects decoded slash and backslash components; a consumer regression preserves a sentinel outside the content directory and failed before the guard. The merged fixture removes its links when it removes those entries.

Independent Sol accepted this correction. The parent complete website suite passes 20 tests with no failures or skips, including actual CLI export parity and Astro build. This establishes the reader and route checks, not the pending release media pipeline.
