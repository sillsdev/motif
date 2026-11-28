import assert from 'node:assert/strict';
import { fileURLToPath } from 'node:url';
import test from 'node:test';
import { readHelpExport, validateHelpLinks } from '../scripts/sync-core.mjs';

test('the CLI Help export resolves every Help page link', async () => {
	const helpExport = await readHelpExport(
		fileURLToPath(new URL('../../tests/SIL.Motif.Tests.Cli/Golden/help', import.meta.url)),
		fileURLToPath(new URL('../../src/SIL.Motif.Help/Content', import.meta.url)));
	const unknownLinks = validateHelpLinks(helpExport.entries);

	assert.equal(unknownLinks.length, 0, `Unresolved Help links:\n${unknownLinks.join('\n')}`);
});
