import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import test from 'node:test';
import { validateHelpLinks } from '../scripts/sync-core.mjs';

test('the CLI Help export resolves every Help page link', async () => {
	const helpExport = JSON.parse(await readFile(new URL('../../tests/SIL.Motif.Tests.Cli/Golden/help-all-current.json', import.meta.url), 'utf8'));
	const unknownLinks = validateHelpLinks(helpExport.entries);

	assert.equal(unknownLinks.length, 0, `Unresolved Help links:\n${unknownLinks.join('\n')}`);
});
