# Captured PanGloss diagnostics

These fixtures preserve what the parser actually reported, so Motif can display recorded facts without inventing missing reasons. The synthetic grammars belong to PanGloss; Motif uses their captured output to test its readers and displays.

The committed `trace-details-v3-*.json` files were captured with the published PanGloss v0.6.0 Linux executable. The published release commit is `944b97e7`. Initial captures used the existing managed build at PanGloss commit `0d9c844003954a2968536f8a3776d1aa625ec6dd`. The source checkout and pinned v0.5.2 binary directory were read-only. Local executables and recaptures live under `/tmp`. The release recaptures differ from initial captures only in timing counters and the parser version stamp; the health report is identical.

To reproduce, use the released executable and a PanGloss v0.6.0 checkout:

```sh
pangloss parse "$PANGLOSS_SOURCE/<source>" <word> \
  --trace=<output.json> --trace-format=json --trace-details
```

| Word / fixture | PanGloss source |
|---|---|
| kumata | `docs/formats/examples/trace-details-v2-sample.snapshot.json` |
| matinlu | `conformance-staging/filter-passes/exact-span/grammar.xml` |
| zodut-synthetic (word zodut) | `rust/crates/pg-cli/tests/data/trace-family.xml` |
| sipu | `conformance-staging/edge-cases/optional-template-composite/grammar.xml` |
| numobel | `conformance-staging/edge-cases/compounding-non-recursive/grammar.xml` |
| tarona-required (word tarona) | `conformance-staging/filter-passes/co-occurrence/grammar.xml`, with its co-occurrence rule changed from `type="exclude" primaryMorpheme="mrPast" otherMorphemes="mrFut"` to `type="require" primaryMorpheme="mrEmph" otherMorphemes="mrPast"` in a temporary copy |
| kapita | `conformance-staging/filter-passes/allomorph-compatibility/grammar.xml` |

`GrammarHealth/schema-v4-producer.json` comes from the sample snapshot above, with the first root's stem MSA `part_of_speech` set to null and `11111111-1111-1111-1111-111111111111` appended to the template's suffix slots. Run `pangloss grammar-health <modified.snapshot.json> --fw-project Synthetic`; exit code 1 accompanies the valid error report. Its missing slot subject deliberately has no live navigation. `schema-v4-error.json` is a hand-authored error-level diagnostic test fixture, not a producer capture.

`trace-motifa.golden.json` is Motif's normalized CLI response over its seeded project and fake parser. It pins consumer projection, independently of the real producer captures.

The installed release executable at `~/work/pangloss/v0.6.0/pangloss-linux-x64` has SHA-256 `0b2643d7d5bd8b63849772d442829e8e00e6fc7015c12db32e5c6e154ffe6794`. Recapturing the original six traces with it preserves the committed non-timing evidence; its health report matches exactly. Use this released executable as `MOTIF_PANGLOSS_EXE` for local real-parser validation.

`trace-details-v3-tarona-required.json` is a released-parser capture of the modified synthetic XML grammar above. Its required co-occurrence rule has no FieldWorks equivalent; it tests consumer preservation of the producer's `require: true` polarity, owner and ordered operands. It does not claim a FieldWorks grammar can author that constraint.
