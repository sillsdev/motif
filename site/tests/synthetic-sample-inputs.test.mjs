import assert from 'node:assert/strict';
import { mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { resolveSyntheticSampleRoot } from '../scripts/synthetic-sample-inputs.mjs';

const repository = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');

test('site input resolver rejects a partial sample cache', async (t) => {
	const root = await mkdtemp(path.join(os.tmpdir(), 'motif-sample-inputs-'));
	t.after(() => rm(root, { recursive: true, force: true }));
	await assert.rejects(resolveSyntheticSampleRoot(repository, root), /exactly the catalog directories/);
});
