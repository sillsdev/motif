# Machine conformance fixture provenance

The conformance project used by Motif comes from the public [Machine commit
f150e2a005ce639f7d68ef17fb0db25b2f6aaa3c](https://github.com/sillsdev/machine/tree/f150e2a005ce639f7d68ef17fb0db25b2f6aaa3c/conformance/edge-cases/deep-optional-affix-nesting).
The consumed project database, writing-system files, and YAML oracle are provisioned by
Get-ConformanceFixture.ps1 and verified against evals/conformance.lock.json. The assessment inputs
come from three bounded UTF-8 spans in the pinned oracle; the test does not parse the YAML document.
The adjacent XML grammar and phonology mutation file are not consumed.

The upstream directory is conformance/edge-cases/deep-optional-affix-nesting/. Upstream fieldworks/
maps to the project database, writing-system directory, and phonology mutation file. These eight
source files were byte-verified against the recorded Motif fixture at the pinned commit. SHA-256
values below are provenance metadata only; no grammar or project contents are included.

| Upstream path | Bytes | SHA-256 |
|---|---:|---|
| conformance/edge-cases/deep-optional-affix-nesting/grammar.xml | 12,955 | ca08b180c5eccbee358c0d3609df3d0b43130b13c0d8be5900235f4f69c63112 |
| conformance/edge-cases/deep-optional-affix-nesting/words.yaml | 102,026 | 92330f36ae3a9cafefa93f7cc36ff1e102f55fe66412c9e6e52d2e4d49f18b8f |
| conformance/edge-cases/deep-optional-affix-nesting/fieldworks/phonology-mutations.yaml | 625 | fa225c85e7efd05c556bd0c3e3ccf027f121b495adb49148589e011b63a2c81f |
| conformance/edge-cases/deep-optional-affix-nesting/fieldworks/project.fwdata | 69,873 | c75d8e8877a3b5bb4125c141cbe5cb7dac76c0fa1ecafc4fd07f62d8d810c2b9 |
| conformance/edge-cases/deep-optional-affix-nesting/fieldworks/WritingSystemStore/de.ldml | 823 | 2a0570a1e53f79739859b2622061b31da0bbcb579b328882f23f7f42385b2cfb |
| conformance/edge-cases/deep-optional-affix-nesting/fieldworks/WritingSystemStore/en.ldml | 763 | 9015ac69b98dee7eb594a2f2c3cf6b865a2152f51ad2cd3df68a52ad6c24361d |
| conformance/edge-cases/deep-optional-affix-nesting/fieldworks/WritingSystemStore/es.ldml | 912 | 9fd89776026d62fcff7fefb22b8b499e8fa88752c3c71ed72a422f67c1c21952 |
| conformance/edge-cases/deep-optional-affix-nesting/fieldworks/WritingSystemStore/fr.ldml | 792 | 9b8c132cb5d7cfd8873889d94308014afec5f5407aeb77fe1ec3f235078a0dee |

Only project.fwdata and WritingSystemStore/{de,en,es,fr}.ldml are copied into the private test
project. The verified words.yaml stays in the private input cache and supplies the three assessment
inputs through byte offsets and lengths recorded in the lock. The other two files remain identified
here so the removed fixture's source record is preserved without retaining or rehosting those payloads.
