# Motif

Motif helps you understand and improve a FieldWorks grammar using your own texts. **Motif is a tech demo**: keep a FieldWorks backup and try it on a copy of your project.

## What Motif helps with

- **Grammar Guide** records the story of the grammar and its open questions.
- **Texts** lets you work from Sources and see analyses in context.
- **Words** helps you inspect and work on individual analyses.
- **Progress** shows how much of your text has approved analyses, guesses or no analysis.
- **Proposals** lets you review and compare proposed changes before choosing what to Apply.
- **Claude** can help investigate evidence and draft Proposals; a person makes the decisions.

Diagnostics, including parser trace and grammar warnings, is available behind a switch. Motif works with your saved FieldWorks project; review changes before applying them.

## Get started

Download an invited-test build from [Releases](https://github.com/sillsdev/motif/releases) and follow the [install guide](src/SIL.Motif.Help/Content/en/guide/install.md). Then [open a project](src/SIL.Motif.Help/Content/en/guide/open-a-project.md) and read [what Motif does](src/SIL.Motif.Help/Content/en/guide/what-is-motif.md).

Explore [texts](src/SIL.Motif.Help/Content/en/guide/texts.md), [Proposals](src/SIL.Motif.Help/Content/en/guide/proposals.md) and [building a grammar](src/SIL.Motif.Help/Content/en/guide/building-a-grammar.md). For parser diagnosis, see [parser help](docs/parser-help/README.md); if you need help, start with [when something goes wrong](src/SIL.Motif.Help/Content/en/guide/when-something-goes-wrong.md). The [release notes](docs/release-notes/) describe the available builds.

Paratext integration is the separate motif-Bible direction, described in [ADR 0025](docs/adr/0025-motif-bible-is-an-expanded-interlinearizer-on-fw-lite.md); it is not a promise that every feature is shipped.

## Work with Claude

For a new grammar, begin with the [starter morphology interview](plugin/skills/linguistic-consultant/references/starter-morphology-interview.md). Read about [AI assistants](src/SIL.Motif.Help/Content/en/guide/ai-assistants.md), [working with an AI consultant](src/SIL.Motif.Help/Content/en/guide/learn/working-with-an-ai-consultant.md), the [agent guide](src/SIL.Motif.Help/Content/en/guide/agents/start-here.md), and the [Motif plugin](plugin/).

## Contribute

Install the .NET 10 SDK and use the repository gates:

```powershell
./build.ps1
./test.ps1       # Unit and Integration; -All is the merge gate
```

[AGENTS.md](AGENTS.md) covers build prerequisites, testing and contribution rules; [CONTEXT.md](CONTEXT.md) is the binding glossary. Find contracts and references in the [documentation map](docs/README.md), development practice in [development workflow](docs/development-workflow.md), settings in [configuration](docs/configuration.md), and decisions in the [ADR index](docs/adr/README.md). English Help in [Content/en](src/SIL.Motif.Help/Content/en/) is the translation source; translations arrive through Crowdin. The [site](site/) builds from that Help.

## Licence

Motif software is MIT-licensed. Do not commit grammar or FieldWorks project data, including synthetic payloads; [ADR 0002](docs/adr/0002-grammar-data-never-belongs-in-motif.md) states the rule and the named third-party document exception. External evaluation data has [its own terms](https://github.com/sillsdev/motif-test-grammars/blob/v0.3.0/evals/DATA-LICENSES.md).
