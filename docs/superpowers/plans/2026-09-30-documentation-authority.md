# Shared documentation authority

People should receive the same explanation from Motif's window, terminal and website. Authored content belongs beside the module that understands it, while each reader chooses how much to show.

**Goal:** One authoritative source per product fact, with shared metadata, localization, full pages and stable links.

## Ownership and levels

Short labels, contextual explanations and complete guides serve different needs without requiring separate copies of the same facts. Developer contracts and historical decisions remain distinct documents with explicit authority.

- `SIL.Motif.Help` owns authored user documentation and its catalog. Move `help/` to `src/SIL.Motif.Help/Content/`, preserving locale directories and embedded logical resource names.
- Command/control titles and descriptions remain authored metadata where their brevity is a product requirement. Full pages are separate detail levels within the same source package.
- Guide titles and summaries are derived once from their Markdown by Help, rather than independently by App and site. Navigation grouping/order is presentation data referencing catalog codes, not another page inventory.
- CLI, App and site consume that catalog. Generated website pages, exports and media are outputs, never a second editing location.
- `CONTEXT.md` remains the binding developer glossary. Normative wire/semantic contracts own developer rules; one maintained architecture overview links to them. README covers orientation and setup, then links onward.
- ADRs retain decision history. Superseded plans are visibly historical and do not compete with the current overview.

## Task 1: restore real-source website synchronization

The website must accept the actual pages that Motif ships. Fixture success cannot substitute for publishing the real Help export.

**Files:** `site/scripts/sync-core.mjs`, its tests, and the documentation validation script added below.

- [ ] Reproduce `motif help --all --json` export followed by `sync.mjs --help-export <export>` failing on Guide `pangloss`.
- [ ] Include PanGloss in the current Guide outline as the immediate repair. Keep failure on unknown pages so omissions remain visible.
- [ ] Add a regression using actual authored content and a supplied real export in an isolated output directory; do not write test artifacts into generated production content.
- [ ] Run `npm test --prefix site`, then repeat synchronization with a freshly built CLI. Assert expected PanGloss page and links exist.

## Task 2: deepen the Help catalog

Readers should ask Help for a page instead of implementing their own resource lookup and Markdown metadata rules. Nested Guide paths need stable identities that are independent of display titles.

**Files:** `src/SIL.Motif.Help/HelpCatalog.cs`, Help catalog tests, and Help project resource configuration.

- [ ] Add Guide as a catalog entry kind. Enumerate embedded Guide resources, including `agents/` and `learn/`; preserve hierarchical codes instead of passing them through the flat command slug algorithm.
- [ ] Derive title and summary using one Markdown metadata implementation. Preserve original full Markdown separately; display truncation is a reader choice.
- [ ] Apply existing per-field English fallback to translated Guide metadata/content. Test partial locale availability, missing pages, nested paths and duplicate codes.
- [ ] Centralize routes for commands, terms, controls, Guides and Learn. Resolve the current UI route mismatch (`/reference/ui` versus the site's `/reference/controls`).
- [ ] Extend cross-link validation/resolution to `guide:` alongside `cmd:`, `term:` and `ui:`. Test escaped multiword codes and unknown targets.
- [ ] Preserve released command title limits and catalog completeness tests. Amend ADR 0047's implementation description to match the actual descriptor/catalog join rather than inventing redundant descriptor fields.

## Task 3: adapt all three readers

The same exported page and metadata should appear in all readers, with their existing presentation styles. Qualified Guide lookup avoids ambiguity with commands having the same name.

**Files:** CLI Help handlers/export records, `HelpPopupViewModel`, App Help link handling, site sync core and site landing-page source.

- [ ] Support `motif help guide:overview --full` and nested Guide codes. Include Guides in `help --all --json`, with stable kinds/codes/routes and full content.
- [ ] Replace App's private Guide resource reader and metadata parser with catalog calls, including the PanGloss page. Preserve localized control overrides and existing popup behavior.
- [ ] Have site synchronization render exported pages and metadata. Do not replace exported content by rereading a different physical Help tree.
- [ ] Reference exported codes in Guide navigation and home cards; derive human labels/summaries from the catalog. Keep site-specific layout and ordering, validating missing, duplicate and nonexistent entries.
- [ ] Preserve the authored Learn index rather than overwriting its body with an independently generated substitute.
- [ ] Add parity checks for representative command, term, control, Guide, nested agent Guide and Learn pages. Assert catalog content/metadata and public routes, not identical HTML across readers.

## Task 4: move content once and repair current orientation

Locating shared content with Help makes its owner obvious. Moving files must preserve public behavior and translation inputs without introducing a second fallback source.

**Files:** `help/**` moved to `src/SIL.Motif.Help/Content/**`, Help `.csproj`, all source/test/tool references, translation configuration, README and current architecture/API documentation.

- [ ] Integrate vocabulary edits before moving their content files. Inventory references with `rg` before the move, including translation configuration and site fixtures.
- [ ] Change physical paths and resource includes together while preserving `help/...` logical names. Update site/tool/test inputs explicitly; add no old-path compatibility reader.
- [ ] Write one current architecture overview with the actual dependency graph, typed front-end seam, parser process boundary, SQLite coordination and lifecycle ownership. Link normative contracts instead of restating them.
- [ ] Remove current-facing claims about retired net48/netstandard targets, CLI-only GUI communication, obsolete parser flags and retired store topology.
- [ ] Mark superseded plans as historical, linking their replacement. Keep ADR history and rationale intact unless recording an explicit new decision.
- [ ] Make CLI API prose point to shared agent Guides and generated reference. Keep developer XML API documentation with code; user guides must not expose implementation details unnecessarily.

## Task 5: validate documentation as an output pipeline

Documentation should be generated from the same reviewed build and walkthrough evidence. A missing real export or required asset must fail clearly.

**Files:** a repository documentation validation script, site sync arguments, walkthrough artifact validation, `.github/workflows/ci.yml` or a dedicated documentation workflow.

- [ ] Build with `./build.ps1`; export Help into a fresh run directory; require a nonempty valid export and the expected Guide inventory.
- [ ] Generate walkthrough artifacts into that run's private media directory. Verify manifests and required screenshots before syncing.
- [ ] Add production sync options accepting explicit export/media/API paths and refusing fixture fallback. Keep fixtures usable for isolated unit tests.
- [ ] Sync and build the site with those exact inputs; upload the output for review. Deployment is outside this task.
- [ ] Require screenshots/manifests in every documentation validation and videos additionally in release validation. The release lane must require the encoder and expected video formats; do not silently accept optional-encoder behavior there.
- [ ] Run the repository test script, site tests and real-source sync/build. Report fixture checks separately from end-to-end publishing checks.

## Integration order

Behavioral corrections come first, then shared catalog ownership, then reader integration and content relocation. This avoids parallel content edits being lost during a directory move.

Use separate workers for the immediate site repair and current orientation, then one Help catalog owner. CLI/App adapters can proceed in disjoint worktrees once the catalog contract is committed; site rendering follows the same contract. The parent reviews routes, localization, authoritative paths and the final real-source pipeline.
