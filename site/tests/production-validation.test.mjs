import assert from 'node:assert/strict';
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import * as productionInputs from '../scripts/production-inputs.mjs';

test('production sync requires explicit real inputs rather than defaults', () => {
	assert.throws(
		() => productionInputs.validateProductionSyncArguments(['--production'], { repository: 'repo', site: 'repo/site' }),
		/Production sync requires explicit paths: --help-export, --help-root, --walkthrough-output, --api-xml/,
	);
});

test('production sync rejects fixture inputs even when explicitly supplied', async (t) => {
	const root = await mkdtemp(path.join(os.tmpdir(), 'motif-sync-production-fixture-'));
	t.after(() => rm(root, { recursive: true, force: true }));
	const repository = path.join(root, 'repository');
	const site = path.join(repository, 'site');
	const args = productionArguments(repository, {
		helpExport: path.join(site, 'fixtures', 'help-export.json'),
	});

	assert.throws(
		() => productionInputs.validateProductionSyncArguments(args, { repository, site }),
		/Production sync refuses fixture input for --help-export/,
	);
});

test('production sync rejects video requirements outside production mode', () => {
	assert.throws(
		() => productionInputs.validateProductionSyncArguments(['--require-videos'], {
			repository: 'repo',
			site: 'repo/site',
		}),
		/--require-videos requires --production/,
	);
});

test('production sync binds API and sample outputs to the selected configuration', async (t) => {
	const root = await mkdtemp(path.join(os.tmpdir(), 'motif-sync-production-config-'));
	t.after(() => rm(root, { recursive: true, force: true }));
	const repository = path.join(root, 'repository');
	const site = path.join(repository, 'site');

	assert.throws(
		() => productionInputs.validateProductionSyncArguments(productionArguments(repository, {
			apiXml: path.join(repository, 'bin', 'Debug', 'SIL.Motif.Contract.xml'),
		}), { repository, site }),
		/--api-xml must be SIL.Motif.Contract.xml from the selected Release build output/,
	);
	assert.throws(
		() => productionInputs.validateProductionSyncArguments(productionArguments(repository, {
			samplesOut: path.join(repository, 'bin', 'Debug', 'samples'),
		}), { repository, site }),
		/--samples-out must come from the selected Release build output/,
	);
});

test('production Help and walkthrough inputs must come from the same fresh validation run', () => {
	const repository = path.join(path.sep, 'synthetic', 'repository');
	assert.throws(
		() => productionInputs.validateProductionSyncArguments(productionArguments(repository, {
			helpExport: path.join(repository, 'bin', 'Release', 'documentation-validation', 'old-run', 'help-export.json'),
		}), { repository, site: path.join(repository, 'site') }),
		/from the same documentation validation run/,
	);
});

test('production Help validation discovers nested Guide pages and rejects stale exports', async (t) => {
	const root = await mkdtemp(path.join(os.tmpdir(), 'motif-help-export-validation-'));
	t.after(() => rm(root, { recursive: true, force: true }));
	const helpRoot = path.join(root, 'content');
	const guideRoot = path.join(helpRoot, 'en', 'guide');
	await mkdir(path.join(guideRoot, 'agents'), { recursive: true });
	await writeFile(path.join(guideRoot, 'overview.md'), '# Overview');
	await writeFile(path.join(guideRoot, 'agents', 'start-here.md'), '# Start here');
	const exportPath = path.join(root, 'help-export.json');
	const exportData = {
		locale: 'en',
		entries: [
			{ kind: 'guide', code: 'overview' },
			{ kind: 'guide', code: 'agents/start-here' },
		],
	};
	await writeFile(exportPath, JSON.stringify(exportData));

	await productionInputs.validateProductionHelpExport(exportPath, helpRoot);
	await writeFile(exportPath, JSON.stringify({
		...exportData,
		entries: exportData.entries.slice(0, 1),
	}));

	await assert.rejects(
		productionInputs.validateProductionHelpExport(exportPath, helpRoot),
		/Guide inventory does not match authored pages/,
	);
});

function productionArguments(repository, overrides = {}) {
	const runRoot = path.join(repository, 'bin', 'Release', 'documentation-validation', 'current-run');
	const values = {
		'--help-export': path.join(runRoot, 'help-export.json'),
		'--help-root': path.join(repository, 'help-content'),
		'--walkthrough-output': path.join(runRoot, 'walkthroughs'),
		'--api-xml': path.join(repository, 'bin', 'Release', 'SIL.Motif.Contract.xml'),
		'--samples-root': path.join(repository, 'samples'),
		'--samples-out': path.join(repository, 'bin', 'Release', 'samples'),
		'--docs-root': path.join(repository, 'docs'),
		'--configuration': 'Release',
		...Object.fromEntries(Object.entries(overrides).map(([key, value]) => [
			'--' + key.replace(/[A-Z]/g, (match) => '-' + match.toLowerCase()),
			value,
		])),
	};
	return ['--production', ...Object.entries(values).flatMap(([key, value]) => [key, value])];
}
