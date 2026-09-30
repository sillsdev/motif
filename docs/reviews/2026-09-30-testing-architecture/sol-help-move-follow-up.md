# Shared documentation follow-up

The CLI, window and website should present the same authored guidance. Keeping source pages beside the Help code makes ownership explicit, while generated preview fixtures and published pages remain derived outputs.

## Reviewed corrections

The near-code move `4fd75868` preserves embedded resource identities and uses catalog exports as the website body source. Independent Sol review initially found that the default preview fixture omitted Guide entries; `cdc2bd76` regenerates it from the CLI with 42 real Guide pages, including ten Learn pages and all seven homepage features. Bodies match the Help-owned source. A sample no longer links to a Turkish lesson absent from the actual catalog, and advertised lesson links are checked for resolution. No physical-body rendering fallback was added.

Cancellation test correction `12f357f4` confines the held fake behavior to Batch, leaving Refresh Grammar Health free to finish. It still proves child exit and no stored invocation after cancellation, awaits the actual refreshed command task and evidence publication, and requires exactly one rerun invocation with the expected words and baseline semantic identity plus bundle digest. A different capture timestamp is legitimate when the byte-identical bundle is reused. Independent Sol found no actionable findings in either correction.

## Verification limits

The owner completed the full wrapper: 3,654 passed, zero failed, 61 skipped, 710.6 seconds, with build, comment, token and offline-restore gates passing. This isolated run is parserless. Website tests against its freshly built real CLI are running; combined pinned-parser and release-media verification remain pending. These commits have not yet been integrated into the parent branch.
