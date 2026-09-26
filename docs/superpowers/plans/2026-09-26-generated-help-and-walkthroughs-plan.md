# Generated help, Walkthroughs and the docs site — implementation plan

**In plain terms:** this builds what ADR 0047 decided. Every released command gets a Title, a Description and
a Help page that the CLI prints and the website shows; the window's first Walkthrough — open a project and
see its Overview — runs as a test and produces screenshots and a silent clip; and a Starlight site assembles
all of it, ready to deploy to Cloudflare once the account exists. English only; every string sits where
Crowdin can find it.

Three packages run in parallel from branch `feat/generated-help`; a fourth follows once they are merged.
The interfaces below are fixed so that no package waits on another. A package that finds one of them
unworkable stops and reports rather than changing it alone.

## Shared interfaces (binding on every package)

### Help content layout — repository root `help/`

```
help/
  en/
    commands.json          { "<code>": { "title": "...", "description": "...", "source": "..." } }
    ui.json                same shape, keyed by AutomationId
    terms.json             same shape, keyed by glossary term code (lowercase, e.g. "proposal")
    commands/<slug>.md     Help page for a command
    ui/<slug>.md           Help page for a control (optional per control)
    terms/<slug>.md        Help page for a term (optional)
    walkthroughs/<id>.json { "title": "...", "description": "...", "steps": { "<stepId>": "caption" } }
```

- `<code>` for a command is the `CommandCatalog` name verbatim (`apply --all-pending`, `jobs show`).
- **Slug rule:** lowercase; every run of characters outside `[a-z0-9]` becomes one `-`; trim `-` from both
  ends. `apply --all-pending` → `apply-all-pending`; `Motif.Overview.Refresh` → `motif-overview-refresh`.
- `title` ≤ 30 characters. `description` 1–3 sentences, not equal to or a restatement of the title.
  `source` is free text naming where the prose came from (`hand-written`, a FieldWorks string, a doc path).
- Help pages are plain CommonMark: no MDX, no HTML components, no front matter. Cross-links use
  `[text](cmd:<code>)`, `[text](term:<code>)`, `[text](ui:<AutomationId>)`; images use
  `![alt](shot:<walkthroughId>/<stepId>)`. Readers resolve these into their own kind of link.
- A new language is a sibling folder (`help/es/...`) with the same shape; English is the fallback per key.

### Help URL

`https://motif-docs.pages.dev` is the placeholder site root until the domain is chosen; it lives in exactly
one constant. English pages have no language prefix; other languages use `/<lang>/`.
Command page: `{root}/reference/commands/<slug>/`. Term: `{root}/reference/terms/<slug>/`.
Walkthrough: `{root}/guide/walkthroughs/<id>/`.

### `motif help --all --json` — the export every other reader consumes

```json
{ "locale": "en", "siteRoot": "https://motif-docs.pages.dev",
  "entries": [ { "kind": "command", "code": "apply --all-pending", "slug": "apply-all-pending",
                 "title": "...", "description": "...", "helpPage": "markdown or null",
                 "usage": ["Usage: motif ..."], "surface": "Released", "url": "..." } ] }
```

`kind` is `command`, `ui` or `term`. `usage` and `surface` are present for commands only.

### Walkthrough scripts and outputs

- Scripts: `walkthroughs/<id>.walkthrough.json`, validated by `walkthroughs/walkthrough.schema.json`
  (JSON Schema 2020-12). Steps address controls by AutomationId only.
- Captions come from `help/<lang>/walkthroughs/<id>.json`, keyed by step id — never from the script.
- When `MOTIF_WALKTHROUGH_OUTPUT` names a directory, each Walkthrough writes `<dir>/<id>/`:
  `manifest.json`, `steps/NN-<stepId>.png`, `steps/NN-<stepId>-annotated.png`, `captions.<lang>.vtt`, and,
  when `MOTIF_WALKTHROUGH_CLIPS=1` and `ffmpeg` is on PATH, `clip.webm`, `clip.mp4`, `clip.webp`,
  `poster.png`.
- `manifest.json`:

```json
{ "id": "open-project-overview", "locale": "en", "title": "...", "description": "...",
  "width": 1280, "height": 720, "fps": 30,
  "steps": [ { "id": "open", "caption": "...", "startMs": 0, "endMs": 3000,
               "screenshot": "steps/01-open.png", "annotated": "steps/01-open-annotated.png",
               "callouts": [ { "x": 0, "y": 0, "width": 0, "height": 0, "label": "1" } ] } ],
  "clip": { "webm": "clip.webm", "mp4": "clip.mp4", "webp": "clip.webp", "poster": "poster.png" } }
```

`clip` is `null` when clips were not rendered.

## Package A — Help core, CLI help, Title rename (`gh/help`)

1. New project `src/SIL.Motif.Help` (net10.0, no LibLCM reference) embedding `help/**` as resources, with a
   `HelpCatalog` that loads a culture with per-key English fallback, finds an entry by kind and code, lists
   all entries, returns Help page Markdown, builds URLs, and validates cross-links.
2. CLI: `motif help` lists released commands with their Titles; `motif help <verb>` prints Title,
   Description, usage lines and URL; `--full` adds the Help page; `--json` emits the entry;
   `motif help --all --json` emits the export above.
3. Content: Title, Description and English Help page for every `CommandSurface.Released` command, written
   from `docs/cli-api.md`, `CONTEXT.md` and the handlers, in the glossary's vocabulary. Terms entries for at
   least Proposal, Dry Run, Assessment, Baseline, Preflight, Drift, Default Selection, Overview, Walkthrough.
4. Tests: every released command has a Title ≤ 30, a Description that is 1–3 sentences and not its Title,
   and an English Help page; every JSON key names a real code; every `cmd:`/`term:`/`ui:` link resolves;
   `help --all --json` matches the shape above.
5. Rename the manifest's `Label` column to `Title` in `manifest/kind-descriptions.tsv` and everything that
   reads it in `src/SIL.Motif.Generator` (a rename, no alias). Regenerate whatever the generator emits.

## Package B — AutomationIds and the Walkthrough engine (`gh/walk`)

1. `AutomationIds`: one static class of constants in the App; views set
   `AutomationProperties.AutomationId` from it (`{x:Static}`). Tests: every constant is used exactly once
   in a view, and every AutomationId in a view comes from the class. Cover every control the pilot touches.
2. Script format and schema as above; a loader that rejects unknown step kinds and fields. Step kinds:
   `click`, `type`, `waitFor`, `highlight`, `hold`, `capture` — each with `id`; `capture` marks a
   screenshot. No coordinates in scripts.
3. Runner: one xUnit theory over every script, reusing `WalkthroughWindow`/`WalkthroughSteps` and the
   seeded fixtures, asserting every step. Fixed clock and fixed paths; bundled SIL font (Andika, OFL, under
   the test project's assets) for renders; fixed size 1280×720, scale 1.
4. Outputs as above when `MOTIF_WALKTHROUGH_OUTPUT` is set: clean and annotated screenshots (numbered
   callouts drawn from resolved control bounds), WebVTT captions per step, and a silent clip composed with
   SkiaSharp — synthetic cursor moving between targets, click ripple, gentle zoom to the highlighted
   control — encoded with ffmpeg. No ffmpeg: clips are skipped with a message, never a failure.
5. Baselines: reviewed PNGs for the pilot under the test project, compared with a documented tolerance;
   `MOTIF_WALKTHROUGH_UPDATE_BASELINES=1` rewrites them.
6. Pilot `open-project-overview`: open a seeded project, reach the Overview, highlight its headline number.
   No PanGloss. Captions in `help/en/walkthroughs/open-project-overview.json` (package B owns this file).

## Package C — Starlight site and API reference (`gh/site`)

1. `site/`: Astro Starlight, pinned versions and a lockfile. English at the root, i18n configured so adding
   a locale is a config line plus a folder. Sections: **Guide**, **Reference** (commands, terms, API) and
   **Developers** (every ADR and the public design docs under `docs/`, synced at build time, not copied into
   git).
2. `site/scripts/sync.mjs` builds content from three inputs, each with a checked-in sample under
   `site/fixtures/` so the site builds without .NET: the `help --all --json` export, `help/<lang>/**/*.md`
   (resolving `cmd:`/`term:`/`ui:`/`shot:` links), and a Walkthrough output directory.
3. A `Walkthrough` component: the looping muted clip beside its step captions, the current step highlighted
   as it plays, a click on a step seeks, and a step-by-step view (screenshot, callout, caption, Next) under
   `prefers-reduced-motion`, in print, and on narrow screens; WebVTT track attached.
4. API reference: XML documentation enabled for `SIL.Motif.Contract` and converted to Markdown under
   Reference/API by a small tool under `tools/`, without weakening the comment gate or warnings-as-errors.
5. `llms.txt` published if a maintained plugin exists; otherwise note why not.
6. `npm ci && npm run build` passes offline from fixtures; Pagefind search works in the built output.

## Package D — after A, B and C are merged

- App help pop-up: F1 or a help button shows Title, Description and the Help page (rendered Markdown), plus
  "Open online" to the URL. Tooltips on AutomationId'd controls come from `help/<lang>/ui.json`.
- `.github/workflows/docs.yml` on `windows-latest`: `./build.ps1`, `./test.ps1` with
  `MOTIF_WALKTHROUGH_OUTPUT` and `MOTIF_WALKTHROUGH_CLIPS=1`, `motif help --all --json`, site sync and build,
  upload the site as an artifact; deploy with Wrangler Direct Upload only when `CLOUDFLARE_API_TOKEN` and
  `CLOUDFLARE_ACCOUNT_ID` secrets exist.

## Gates for every package

`./build.ps1` and `./test.ps1` green (comment and token hygiene included), plus `npm run build` for C.
Commits on the package branch only; no merge, no push.
