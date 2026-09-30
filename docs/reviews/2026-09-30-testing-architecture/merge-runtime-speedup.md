# Runtime and package build improvement

The Worker implementation is reusable without pulling its executable packaging files into the front ends. Its package test now reuses common compilation inside one private run instead of rebuilding those dependencies three times.

## Merge boundary

Candidate code 45d255f5 was tested on main 9189dc36. Integration also preserves subsequent main 0a640e91, whose package-smoke script and release-plan changes are disjoint from this candidate. It contains only the independently reviewed Runtime extraction, architecture overview, Unix package staging/process lookup, solution build ordering and private publish-cache reuse. Admission, usage, Help content relocation and new authored walkthrough work remain on the larger review branch.

All publishes retain fresh private GUID output/intermediate roots, project-specific intermediates, sequential App/CLI/Worker ordering, separate final Worker output, shared Worker byte-preservation checks, executable asset exclusions and actual packaged CLI-to-Worker SQLite completion. No tests are removed or deadlines relaxed.

## Verification

Fresh ./test.ps1 passed 3,704 tests, failed none and skipped 22. Its test phase took 558.3 seconds; this excludes the preceding build and offline restore stages. Comment and token hygiene, compilation and offline restore passed; compilation reported zero warnings and errors. All five release-required real PanGloss integrations passed against the pinned v0.5.1 artifact. Skips comprise one parser capability, sixteen platform and five opt-in artifact checks.

The owner experiment passed 3,647 tests, failed none and skipped 61 on its parserless base. The complete package case took 128.299 seconds; its three publishes took 122.251 seconds total. The candidate's pinned run recorded a 209.327-second package case and 201.330-second publish total. Different concurrent machine activity prevents treating these as an isolated before/after benchmark. Both are lower than the prior retained 470.916-second case, but the two-minute complete test target remains unresolved.

Website CLI parity and site build passed all eight checks. Independent Sol review accepted all five candidate commits and verified their patch IDs against the reviewed originals; independent TRX inspection confirmed the counts and package timings above. The subsequent main merge was clean and introduced no changes to the tested Runtime or test implementation. Windows execution is established; native Unix package execution remains CI evidence.
