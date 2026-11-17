# ADR 0029 — Help, Walkthroughs and the docs site come from one source

**Status:** Accepted, 2026-11-14.

## Context

Guidance authored separately for the window, CLI and website drifts. Shared Help content and declarative
Walkthroughs keep instructions and generated media tied to the product, while allowing offline text and
translation without product-code changes.

## Decisions

### D1. A Walkthrough supplies tests, marked screenshots and clips

A Walkthrough is a declarative, versioned script of click, type, wait-for and highlight steps against the real
window controls on seeded fixed data in the headless harness. The script runs as an xUnit test through
`./test.ps1`, asserting each outcome. A missing control or expected state fails that test. When media generation
is requested, the same replay produces screenshots at marked steps and a silent clip when clip rendering is
enabled. Screenshots and clips are optional outputs, never test gates, under
[ADR 0030](0030-all-automated-testing-is-headless.md).

Steps address `AutomationProperties.AutomationId`, never display text or coordinates. Views and scripts share one
generated constants class so removed or misspelt ids fail the build. Ids never translate. Fixture clocks and
machine paths are deterministic. Fixture data is created at runtime or provisioned externally under
[ADR 0002](0002-grammar-data-never-belongs-in-motif.md); this decision authorizes no committed project payload.

### D2. Moving pictures are silent loops with separate translated captions

Use short looping WebM and MP4 beside step captions as page text. Provide a step-by-step screenshot, callout,
caption and Next view for reduced motion, print and narrow screens. Animated WebP or AVIF serves image-only
contexts. Do not produce GIF: its size and text dithering are unsuitable for UI recordings.

Captions remain searchable and accessible page text. Translate strings; re-render a clip per language only to
show that language in the window. Render screenshots and clips with a bundled SIL font at fixed size and scale.
Narration is not planned; any future narration needs a separate decision and would be an additional output of
the same Walkthrough with pluggable local text-to-speech. Pixel comparison and reviewed picture baselines are not
acceptance criteria.

### D3. Every documented thing has four lengths of Help

Commands, operation kinds, controls and glossary terms have stable code, a **Title** of at most 30 characters,
a **Description** of one to three sentences, and a **Help page**, normally a paragraph to half a page.

| Length | Use |
| --- | --- |
| Code | CLI, Walkthroughs and URLs; never translated |
| Title | Buttons, menus and CLI Help lists |
| Description | Tooltips, `motif help <verb>`, search results and agent tool descriptions |
| Help page | Site, `motif help <verb> --full` and in-app Help |

Descriptions are required, add information beyond the Title and record their source. Operation kinds use the
manifest's existing Title and Description, not a parallel pair; its column is `Title`, while a Proposal's
unrelated `label` remains unchanged. The manifest is the English source for operation kinds. HelpCatalog owns
catalog Titles, Descriptions and pages, joining commands by stable code.

Every Released command has an English Title, Description and Help page, enforced by the catalog test.
`motif help --all --json` exports every length of every entry in one call. The built export and committed Help
golden remain synchronized. Help pages ship inside the build so CLI and window can read them offline.

### D4. Help content stays JSON and CommonMark; window resources supply control Titles

Help, command and Walkthrough catalogs keep English JSON keyed by code and per-language Markdown files/folders.
Help pages use plain CommonMark with no front matter, MDX, HTML or site-only syntax. Links name stable codes,
such as `[Apply](cmd:apply)` and `[Proposal](term:proposal)`, with multi-word codes percent-encoded and commands
limited to Released ones. Each reader resolves these into its own links. Screenshots, clips and richer media are
online only; window and CLI present text and a page URL.

The window's labels, tooltips, messages, accessible names and shortcut labels use .resx under
[ADR 0027](0027-the-window-follows-fieldworks.md). The site reads neutral .resx for control Titles. Shared Help
content remains JSON/Markdown because the CLI and site consume it; it is not extracted into window resources.

### D5. Publish one public Starlight site for users and developers

The site uses Starlight on Astro, with language routing, English fallback, static search and modern CLI code
blocks. User guide and developer documentation are separate public reader sections. XML documentation generates
the API reference, required by 1.0; public assemblies emit XML docs. Publish only the latest documentation until
1.0. Node is needed for the site build only; product builds remain .NET.

Cited sources and condensed engineering findings share the single `docs/references/` folder and its README.
There is no separate singular reference folder.

A changed decision is edited in place in its current ADR; git keeps its history. Allocate a new number
(0032 onward) only for a new topic, and keep no superseded ADRs or redirect chains. The crosswalk in
`docs/adr/README.md` is the only place historical ADR numbers appear. AGENTS.md must state this maintenance
rule. The documentation link check rejects duplicate ADR numbers as well as unresolved references.

Website validation and generation in CI are enabled explicitly by `publish_website`. The Windows documentation
job generates media, builds and validates the site and uploads an artifact; ordinary product gates do not require a
site build. Cloudflare Pages is the deployment target, using Direct Upload only when credentials exist. The
build artifact does not itself deploy. Direct Upload lets generated media accompany the build and cannot later
be converted to a Git-integrated Cloudflare project. Media generation follows
`tools/Build-Media.ps1` and the media manifest; a separately recorded desktop session is not the canonical CI
source.

### D6. Ship English first with translation-ready resources

English is the source and fallback; only English ships before about 1.0. Help JSON and Markdown are organized
for translators to add sibling languages without changing code. Window text follows the same English-first
policy through .resx. There is no separate Motif Crowdin project; resources join FieldWorks' project at the
codebase merge. Do not hard-code English where a catalog Title or Description supplies the text.

## Consequences

One set of authored Help serves three readers, and stable control ids tie tested behavior to requested media.
Portable CommonMark keeps the website framework replaceable. Narrated audio and desktop recording cannot be the
primary shared source because they complicate translation, accessibility, reproducibility and search.

Control and Walkthrough tests assert behavior. Headless layout, contrast, reachability and automation-id checks
validate the UI. A person requests captures to judge appearance; generating or comparing a picture does not
decide readiness. Installed-product replay and its real-parser twins are defined by
[ADR 0031](0031-motif-ships-as-one-velopack-payload-per-runtime.md).
