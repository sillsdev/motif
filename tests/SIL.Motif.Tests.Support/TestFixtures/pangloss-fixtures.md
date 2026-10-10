# Captured PanGloss diagnostics

These fixtures preserve what the parser reported, so Motif can display recorded facts without inventing missing reasons. The synthetic grammars belong to PanGloss; Motif uses their captured output to test its readers and displays.

## Trace details v3

The seven parser-produced `trace-details-v3-*.json` captures use the published PanGloss 0.6.2 Linux x64 executable, SHA-256 `ee955edee049e8710dce4748c0609a011002d5abeec20eb23d37edc033f75c2b`. PanGloss 0.6.2 keeps `--step-cap` as an analysis-attempt limit and derives an inner search-work limit at 100 times that value. The fixture inputs are from PanGloss tag `v0.6.1`, commit `8d7055b2ba95c90a9b0e05f527caa53abad92da8`; 0.6.2 adds the work cap without changing these fixture grammars or their advice and reason catalogs. XML grammar hashes use source bytes, so the commands below archive the tagged files to preserve their committed line endings.

`trace-details-v3-hawajafika.json` is a synthetic UI fixture with scaffolding, not parser output. The other seven files are raw parser captures. Their analyses, grammar hashes, and trace events are unchanged from the prior pin; 0.6.2 adds `search.workSteps` alongside the parser version and refreshed timing counters.

Run these commands from a shell with Git, Python 3, `tar`, and the PanGloss release executable:

```sh
set -euo pipefail
PANGLOSS_SOURCE=/path/to/PanGloss
PANGLOSS_EXE=/path/to/pangloss-linux-x64
PANGLOSS_CAPTURE_SOURCE="$(mktemp -d)"
MOTIF_FIXTURE_DIR=/path/to/Motif/tests/SIL.Motif.Tests.Support/TestFixtures

git -C "$PANGLOSS_SOURCE" fetch origin refs/tags/v0.6.1:refs/tags/v0.6.1
git -C "$PANGLOSS_SOURCE" archive v0.6.1 \
  docs/formats/examples/trace-details-v2-sample.snapshot.json \
  conformance-staging/filter-passes/exact-span/grammar.xml \
  rust/crates/pg-cli/tests/data/trace-family.xml \
  conformance-staging/edge-cases/optional-template-composite/grammar.xml \
  conformance-staging/edge-cases/compounding-non-recursive/grammar.xml \
  conformance-staging/filter-passes/co-occurrence/grammar.xml \
  conformance-staging/filter-passes/allomorph-compatibility/grammar.xml \
  | tar -x -C "$PANGLOSS_CAPTURE_SOURCE"

python3 - "$PANGLOSS_CAPTURE_SOURCE/conformance-staging/filter-passes/co-occurrence/grammar.xml" \
  "$PANGLOSS_CAPTURE_SOURCE/tarona-required.xml" <<'PY'
import sys
from pathlib import Path
source = Path(sys.argv[1])
text = source.read_text()
old = 'type="exclude" primaryMorpheme="mrPast" otherMorphemes="mrFut"'
new = 'type="require" primaryMorpheme="mrEmph" otherMorphemes="mrPast"'
assert text.count(old) == 1
Path(sys.argv[2]).write_text(text.replace(old, new))
PY

"$PANGLOSS_EXE" parse "$PANGLOSS_CAPTURE_SOURCE/docs/formats/examples/trace-details-v2-sample.snapshot.json" kumata \
  --trace="$MOTIF_FIXTURE_DIR/trace-details-v3-kumata.json" --trace-format=json --trace-details
"$PANGLOSS_EXE" parse "$PANGLOSS_CAPTURE_SOURCE/conformance-staging/filter-passes/exact-span/grammar.xml" matinlu \
  --trace="$MOTIF_FIXTURE_DIR/trace-details-v3-matinlu.json" --trace-format=json --trace-details
"$PANGLOSS_EXE" parse "$PANGLOSS_CAPTURE_SOURCE/rust/crates/pg-cli/tests/data/trace-family.xml" zodut \
  --trace="$MOTIF_FIXTURE_DIR/trace-details-v3-zodut-synthetic.json" --trace-format=json --trace-details
"$PANGLOSS_EXE" parse "$PANGLOSS_CAPTURE_SOURCE/conformance-staging/edge-cases/optional-template-composite/grammar.xml" sipu \
  --trace="$MOTIF_FIXTURE_DIR/trace-details-v3-sipu.json" --trace-format=json --trace-details
"$PANGLOSS_EXE" parse "$PANGLOSS_CAPTURE_SOURCE/conformance-staging/edge-cases/compounding-non-recursive/grammar.xml" numobel \
  --trace="$MOTIF_FIXTURE_DIR/trace-details-v3-numobel.json" --trace-format=json --trace-details
"$PANGLOSS_EXE" parse "$PANGLOSS_CAPTURE_SOURCE/tarona-required.xml" tarona \
  --trace="$MOTIF_FIXTURE_DIR/trace-details-v3-tarona-required.json" --trace-format=json --trace-details
"$PANGLOSS_EXE" parse "$PANGLOSS_CAPTURE_SOURCE/conformance-staging/filter-passes/allomorph-compatibility/grammar.xml" kapita \
  --trace="$MOTIF_FIXTURE_DIR/trace-details-v3-kapita.json" --trace-format=json --trace-details
```

`trace-motifa.golden.json` is Motif's normalized CLI response over its seeded project and fake parser. It pins consumer projection, independently of these real parser captures.

## Grammar-health fixtures

`GrammarHealth/pangloss-v0.6.2-seeded.json` is the unmodified report captured by `PinnedGrammarAdviceTests.PinnedParserSuppliesAdviceForEverySeededFinding`. To recapture it, set the pinned parser and run the one fact through the repository test script, then copy its raw output:

```sh
MOTIF_PANGLOSS_EXE="$PANGLOSS_EXE" MOTIF_TEST_SLOTS=6 MOTIF_TEST_SLOT_DIR=/tmp/motif-test-slots \
  pwsh ./test.ps1 -Configuration Release -Project SIL.Motif.Tests.Commands \
  -Filter FullyQualifiedName~PinnedGrammarAdviceTests
cp bin/Release/tests/seeded-advice-raw.json \
  "$MOTIF_FIXTURE_DIR/GrammarHealth/pangloss-v0.6.2-seeded.json"
```

`GrammarHealth/catalog-advice-v0.6.2.json` is a source-derived fixture, not parser output. Its five entries are the title, explanation, and guidance returned by `import_diagnostic_advice` in PanGloss tag `v0.6.1`, commit `8d7055b2ba95c90a9b0e05f527caa53abad92da8`, `rust/crates/pg-snapshot/src/warning_metadata.rs`. PanGloss 0.6.2 adds the inner work cap without changing this advice. Keep its `{subject}` placeholder; the screenshot fixture expands it just as the producer does.

`GrammarHealth/schema-v4-producer.json` comes from the sample snapshot above, with the first root's stem MSA `part_of_speech` set to null and `11111111-1111-1111-1111-111111111111` appended to the template's suffix slots. Run `pangloss grammar-health <modified.snapshot.json> --fw-project Synthetic`; exit code 1 accompanies the valid error report. Its missing slot subject deliberately has no live navigation. `schema-v4-error.json` is a hand-authored error-level diagnostic test fixture, not a producer capture.

`GrammarHealth/schema-v4-stored-analysis-no-longer-parses.json` is a synthetic schema-4 information finding based on the PanGloss 0.8.2 metadata. It keeps the wordform, stored analysis, morph, MSA and rule subjects together so the Warnings test covers the finding's full subject list.

`pangloss-0.8.2-trace-reasons.json` records the reason codes in `rust/crates/pg-rules/src/trace.rs` at the pinned release.
