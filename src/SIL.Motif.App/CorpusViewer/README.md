# Corpus Viewer Bundle

The read-only client uses CodeMirror 6.0.2. Its exact transitive dependencies and esbuild 0.25.12 are pinned in `package-lock.json`; see `THIRD-PARTY-NOTICES.txt` for the MIT licences. Rebuild from this directory with `npm ci` and `npm run build`. The App embeds the resulting `client.bundle.js`.

Run `npm test` to exercise source positions, keyboard focus, localization and clearing in jsdom 26.1.0 (MIT). Native WebView focus traversal still needs a desktop check.
