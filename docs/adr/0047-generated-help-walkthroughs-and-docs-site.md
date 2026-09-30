# ADR 0047 — Help text, Walkthroughs and the docs site are generated from one source

**Status:** accepted, 2026-09-26. Adds **Walkthrough**, **Help text**, **Title** and **Help page** to `CONTEXT.md`
and widens **Description** from operations to everything documented. Extends ADR 0023's
label-and-description pattern, renaming its label to Title; supersedes nothing else. The implementation
plan is `docs/superpowers/plans/2026-09-26-generated-help-and-walkthroughs-plan.md`.

**In plain terms:** Motif gets a public documentation website, and every screenshot and short animated clip on
it is produced automatically from scripts that also run as tests, so the pictures can never go out of date
with the app. Every command and control has help text in four lengths — its code name, a short title, a
one-to-three-sentence description, and a full page — written once and shown by the window, the CLI and the
website alike. Everything is English until about 1.0, but it is laid out so that translators can add
languages through Crowdin without anyone touching code.

## Context

Motif is open source and will be used by linguists in many languages, most of whom will learn it from
documentation rather than from a person. Hand-taken screenshots go stale the week a layout changes, and
help written separately for the window, the CLI and a website drifts three ways. The App test suite already
drives the real window headlessly and captures rendered frames, so the machinery to generate pictures exists;
what is missing is one source they are all generated from. The owner answered the open questions on
2026-09-25 and 2026-09-26.

## Decisions

### 1. One Walkthrough is a test, its screenshots and its clip

A **Walkthrough** is a declarative, versioned script of steps — click, type, wait for, highlight — against
the real window on seeded, fixed data. The same script runs three ways from one pass of the headless
harness: as an ordinary xUnit test in `./test.ps1`, asserting each step's outcome; as a screenshot at every
marked step; and as a silent clip when clip rendering is enabled. A step that names a missing control, or
whose expected state never appears, fails the test, so a picture cannot outlive the UI it shows.

Steps address controls by `AutomationProperties.AutomationId`, never by display text or coordinates. The
identifiers live in one generated constants class that both the views and the Walkthroughs use, so a
misspelt or removed identifier fails the build. Display text translates; identifiers do not.

### 2. Clips are silent loops with translated step captions, not narrated video and not GIF

The primary moving picture is a short, silent, looping clip (WebM and MP4) shown beside its step captions as
page text, with a step-by-step view — screenshot, callout, caption, Next — for reduced motion, print and
narrow screens. Animated WebP or AVIF covers contexts that accept only images. GIF is not produced: for
screen recordings it is several times larger than video and dithers UI text to 256 colours.

Captions are text on the page, so translating a clip means translating strings; the clip is re-rendered per
language only so that the window shown is in that language. Narrated video is not planned. If it is ever
wanted, it is an additional output of the same Walkthrough with pluggable, local text-to-speech, and needs
its own decision.

Screenshots and clips render with a bundled SIL font at a fixed size and scale, and are compared against a
small set of reviewed baselines with a published tolerance.

### 3. Help text comes in four lengths, from one source

Every documented thing — an operation kind, a command, a window control, a glossary term — has:

| Length | Example | Shown by |
| --- | --- | --- |
| Code | `apply --all-pending` | CLI, Walkthroughs, URLs; never translated |
| **Title**, at most 30 characters | Apply pending changes | buttons, menus, CLI help lists |
| **Description**, one to three sentences | Writes every pending change to… | tooltips, `motif help <verb>`, search results, an agent's tool description |
| **Help page**, a paragraph to half a page | explanation and examples | the website, `motif help <verb> --full`, the in-app help pop-up |

This is ADR 0023's pattern, extended: operation kinds already carry a short name and a Description in the
manifest, and those are the same two lengths, not a parallel pair; the manifest's `Label` column is renamed
`Title` so one word means one thing. A Proposal's `label` field is unrelated and keeps its name. ADR 0023's bars now apply to every
Description — required, must not restate its Title, and must record its source. The manifest stays the English
source for operation kinds; everything else keeps its English Titles and Descriptions in JSON keyed by the
code, and all of it is exported to Crowdin as JSON. Help pages are per-language Markdown files keyed by the
code, shipped inside the build, so the CLI and the window show them offline. `motif help --all --json` emits
every length of every entry, for an agent to read in one call. A test requires a Title, Description and English
Help page for every released command.

Help pages are plain Markdown with no site-only syntax, because three readers render them. They link to each
other by code — `[apply](cmd:apply)`, `[Proposal](term:proposal)` — and each reader resolves that into its
own kind of link. Screenshots, clips and anything else richer than text are **online only**: the window and
the CLI show the text and link to the page by URL.

### 4. The site is Starlight on Cloudflare Pages

The website is built with Starlight (Astro): built-in language routing with English fallback, static search,
and modern code blocks for CLI examples. It has two sections, the user guide and the developer
documentation; everything is public, the split is by reader. It carries the API reference generated from XML
documentation comments, required by 1.0, and publishes one version, the latest, until 1.0.

The Windows CI job generates screenshots and clips, builds the site, and uploads it as an artifact; deployment
to Cloudflare Pages by Direct Upload runs only when its credentials exist. Direct Upload is chosen because the
pictures must be generated on the Windows runner that already runs the App tests, and it cannot later be
converted to a Git-integrated project.

### 5. English first, translation-ready from the start

Only English ships before about 1.0. Every string a translator will touch is already in a per-language JSON
file or Markdown folder that Crowdin can read, with English as the fallback, so adding a language is a
translation task and never a code change.

## Considered options

- **DocFX** fits .NET best and generates API reference natively, but has no built-in language routing and a
  dated default theme. Because Help pages are portable Markdown, the choice is reversible.
- **Narrated video** as the primary format makes every translation a voice-and-timing problem, and speech
  locked in audio cannot be searched, read by screen readers, or read by agents.
- **Screen recording** of a real desktop (OBS, WinAppDriver) depends on a live display, focus and DPI, and
  cannot be the canonical source in CI.

## Consequences

- The CLI joins each command to its Help entry by stable code. HelpCatalog owns titles, descriptions and pages; the App gives Walkthrough controls stable AutomationIds.
- The screenshot fixture loses its live clock and machine paths.
- The public assemblies begin emitting XML documentation.
- Node enters the repository for the site build only; the product build stays .NET.
